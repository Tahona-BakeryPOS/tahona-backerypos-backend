# Architecture — Tahona.BakeryPOS MVP v1.1

Status: **draft for review**.

## 1. Inspected environment (27/10/2026)

| Tool | Found | Proposed use |
|---|---|---|
| .NET SDK | 9.0.319 and 10.0.401 | **.NET 10 (LTS)**. .NET 9 is STS and its support ends in November 2026. |
| ASP.NET Core runtime | 8.0, 9.0, 10.0.12 | 10.0 |
| PostgreSQL | 17 (container) | Development: `postgres:17-alpine` container (I-18) |
| Docker | 29.8 | Compose for PostgreSQL in development and integration tests |
| Git | 2.56 | Own repo, independent of any client — see D-13 |
| Tests | xUnit (unit, integration and architecture) | + integration tests with real PostgreSQL (see testing-strategy) |

## 2. Style: modular monolith

A single ASP.NET Core process and a single database, with **modules with explicit boundaries**. Each
module owns its tables (its own SQL schema) and exposes to the others only public commands/queries
or events; never entities or direct access to foreign tables.

| Module | SQL schema | Responsibility |
|---|---|---|
| Companies | `org` | Companies, branches, configuration, time zone |
| Identity | `sec` | Users, memberships, roles, permissions, devices, sessions |
| Catalog | `cat` | Products, categories, units, taxes, per-branch prices, availability |
| Inventory | `inv` | Locations, movements, balances, adjustments, reconciliation |
| Cash | `cash` | Cash registers, cash sessions, cash movements, closings |
| Sales | `sales` | Sales, lines, returns, orchestration of the sale process |
| Payments | `pay` | Payments, attempts, gateway events, refunds, reconciliation |
| Invoicing | `fis` | Tax configuration, numbering, documents, issuing attempts |
| Audit / Integration | `aud`, `int` | Audit log, outbox, inbound events |

### Solution structure (proposed)
```
backend/
  src/
    Tahona.BakeryPOS.Api/                 → host: controllers, auth, middlewares, OpenAPI
    Tahona.BakeryPOS.SharedKernel/        → Result, Error, money types, ids, common interfaces
    Modules/
      Companies/ Companies.Application  Companies.Domain  Companies.Infrastructure
      Identity/  …
      Catalog/   …
      Inventory/ …
      Cash/      …
      Sales/     …
      Payments/  … + Payments.Wompi   (gateway adapter)
      Invoicing/ … + Invoicing.<Provider> (tax adapter, after D-04)
    Tahona.BakeryPOS.Infrastructure.Persistence → a single DbContext with one schema per module (decision I-01)
  tests/
    *.UnitTests, *.IntegrationTests (real PostgreSQL), Architecture.Tests (boundaries between modules)
```
Lighter alternative (D-14): keep the 4 projects of the previous system and split modules by folder,
enforcing the boundaries with architecture tests. Recommended: **folders + architecture
tests** in the MVP; split into projects only if a module grows.

### Patterns kept from the previous system
CQRS with MediatR · Result/`Error` instead of exceptions for business rules · FluentValidation ·
EF Core with direct DTO projection in queries · automatic audit in `SaveChangesAsync` ·
permissions as claims with `[RequirePermission]` · UUID v7 as key.

## 3. Multi-company and security

- **Row-level isolation**: every business table carries `company_id`. Each DbContext applies a global
  filter `company_id = @tokenCompany`. Writes validate that the references (branch, register,
  product) belong to the same company (also with composite FKs `(company_id, id)` where applicable).
- **Defense in depth**: besides the filter, every handler validates ownership and permissions; the
  isolation tests try to read and write with ids of another company on **every** endpoint.
- **Authentication**:
  - Backoffice: email + password (BCrypt) → short JWT (15–60 min) + rotating refresh token
    stored as a hash. MFA is out of the MVP.
  - Register: paired device (revocable device token) + cashier PIN → session JWT.
  - Platform: a separate platform-administrator role, with no access to sales data.
- **Authorization**: permission + scope (company, branch, register) evaluated in the backend on every operation.
- **Secrets**: `Jwt:Secret`, connection strings and platform keys through environment variables /
  user-secrets / a secrets manager. Gateway and tax-provider credentials **per company** are
  stored encrypted (ASP.NET Data Protection with keys persisted outside the database; in the cloud, Key
  Vault). They are never written to logs (there is a precedent: the previous system once logged the PIN).

## 4. Sale flow: persistent process

A sale is not resolved in a single transaction. It is a process with persisted states and idempotent
steps; every external call happens **outside** SQL transactions.

```
[1] POST /sales (Idempotency-Key)            → TX: 'draft' sale + lines with frozen prices/taxes
[2] POST /sales/{id}/payments (cash|gateway)    → TX: payment attempt 'created' (unique reference)
      └─ if gateway: outside TX → create the gateway transaction → TX: store external id, 'pending'
[3] Webhook or status query                   → TX: attempt 'approved'/'declined' (+ deduplicated inbound event)
[4] Confirmation (when approved payments = total) → single TX:
        sale 'confirmed' + payments applied + inventory movements + cash movement (cash)
        + fiscal document 'pending' + outbox messages
[5] Invoicing worker (outbox)                 → outside TX: send to the provider → TX: store attempt/result
[6] Follow-up worker                          → query the status of 'unknown' documents/payments
```

Separate states (detail in `data-model.md`):
- Sale: `draft → pending_payment → confirmed → (voided)`; `cancelled` if it was never paid.
- Payment/attempt: `created → pending → approved | declined | cancelled | error | unknown`.
- Sale inventory: `not_applied → applied → (reversed)`.
- Fiscal document: `pending → submitting → accepted | rejected | unknown | temporary_error`.

### What happens if each step fails

| Failure | Effect | Recovery | Prevents duplicates |
|---|---|---|---|
| [1] network drops on create | The client does not know whether it was created | Retries with the same `Idempotency-Key` → returns the same sale | Unique idempotent key per company |
| [2] timeout creating the gateway transaction | Attempt in `unknown` | The worker queries by id/reference before any retry | Reference = attempt id, unique; no other attempt is created until resolved |
| [3] webhook does not arrive | The attempt stays `pending` | The worker queries the transaction every N seconds until a final state or expiry | Transitions only move forward; an event with an already processed id is ignored |
| [3] duplicate webhook or invalid signature | — | A 200 is returned with no effect / it is rejected and audited | Inbound events table with a unique id |
| [4] the confirmation transaction fails | Payment approved but sale not confirmed | The worker retries the confirmation (idempotent) | `inventory_status`, unique movement key `(source, line)` |
| [4] approved payment of a cancelled/expired sale | Money received without a sale | Flagged for **refund** and an alert in reconciliation | Daily reconciliation |
| [5] timeout issuing the document | Document `unknown` | **Query the status** at the provider before resending | Same number/consecutive and document id on retry |
| [5] provider/DIAN rejection | Document `rejected` with a reason | Correction and resubmission per the provider's procedure | New attempt linked to the same document |
| Process crash at any point | Intermediate states | Workers resume from the persisted state | All steps idempotent |

## 5. Integrations

- **Ports and adapters**: `IPaymentGateway` and `IFiscalDocumentProvider` in Application; one
  adapter per provider in Infrastructure. No business module knows the concrete provider.
- **Outbox** (table `int.outbox_messages`) written in the same transaction as the change; a
  `BackgroundService` publishes it with exponential retries and row locking.
- **Inbound events** (`int.inbound_events`): each webhook is stored raw, its signature is validated,
  it is deduplicated by the provider's id and processed asynchronously.
- **No simulations in production**: test adapters (fakes) exist only in tests; in
  production startup fails if the provider is not configured.

## 6. API clients

The backend does not depend on any client: it exposes a versioned HTTP/JSON API (`/api/v1`, OpenAPI document at
`/openapi/v1.json`) and is built, tested and deployed on its own. Clients (for example, a PWA for the POS and the
backoffice) live in their own repositories and depend only on the contract (`api-contracts.md`).

What the API requires of any client:
- It requires a connection: there is no offline mode or synchronization.
- `Idempotency-Key` on retryable POSTs and `If-Match` on master-data PUTs.
- Payment and fiscal-document status is queried with short polling.
- If the client runs on another origin, that origin is declared in `Cors:AllowedOrigins`.

## 7. Deployment (D-10)

One VPS per environment with Docker Compose (`deploy/`): PostgreSQL, the API, the migrations (a `dotnet ef` bundle, as a prior
step and never on startup in production) and Caddy as the API's HTTPS proxy. No client is built or served.
Operations guide: `runbooks.md`.
