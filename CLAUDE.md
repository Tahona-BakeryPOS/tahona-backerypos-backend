# Tahona.BakeryPOS — Backend

Bakery POS SaaS for Colombia (MVP v1.1). This repo is the **backend** (.NET API) and is fully independent: it is built,
tested, documented and deployed on its own, with no code, build or deployment of any client.
Clients consume only the API contract (`docs/api-contracts.md`; OpenAPI at `/openapi/v1.json`).
Working branch: `main`.

Repo: <https://github.com/Tahona-BakeryPOS/tahona-backerypos-backend>

## Language

Everything technical is in English: database (schemas, tables, columns, constraint values and stored codes), API routes and
query parameters, error codes and error body keys (`code`, `message`, `details`), code identifiers, code comments, logs, commit messages and
documentation. The only thing that stays Spanish on purpose is **what users read**: validation and error `message` texts, and the display
names seeded for each company (roles, units, payment methods, adjustment reasons...). The product is for Colombian bakeries.

Stored codes (permissions, system roles, default reasons and payment methods, audit actions) are English, for example
`sales.void`, `OWNER`, `WASTE`, `CASH`, `create`. A new stored code is added in English.

## Before implementing

1. Read `docs/requirements.md`, `docs/architecture.md`, `docs/data-model.md` and `docs/decisions.md`.
2. Confirm that the blocking decisions of the phase are approved (`docs/decisions.md`).
3. Follow the order of `docs/implementation-plan.md`.
4. Use-case status and next steps: `docs/use-case-status.md` (read it at the start of every session).
5. Deployment and operations: `docs/runbooks.md` (files in `deploy/`).

The HTTP contract is in `docs/api-contracts.md`: an incompatible change requires a new API version; do not break the current one.

## Non-negotiable rules

- **Company isolation**: every business table has `company_id`; global filter + validation in
  every handler + a cross-access test per endpoint.
- **Separate states**: sale, payment, inventory and fiscal document each have their own state.
- **Nothing external inside a SQL transaction**: the payment gateway and the tax provider are called outside; steps are
  idempotent and workers resume them from the persisted state (outbox / inbound events).
- **A payment is only approved by the backend** (webhook with a valid signature or a server-to-server query).
- **A fiscal document is only "accepted" with verifiable confirmation from the provider**; if the result of a
  submission is unknown, the status is queried before resending, with the same number.
- **Inventory**: the balance is never edited; every change is an idempotent movement.
- **No invented endpoints or responses** for external providers; anything unverified is marked
  "to verify" in `docs/`.
- **Secrets** stay out of the repository; third-party credentials are encrypted per company; never in logs.
- Ask for approval before: changing business decisions, adding major dependencies, changing
  the approved data model, destructive migrations, configuring production integrations or
  changing the scope.

Conventions: Clean Architecture + CQRS (MediatR), one Command/Query + Handler (+ Validator) per use case,
Result/`Error` with uppercase codes, FluentValidation, EF Core with DTO projection in queries,
UUID v7, permissions as claims `[RequirePermission("module.action")]`, automatic auditing in `SaveChangesAsync`.

## Status

**Phases 1 to 10 implemented** (6 not tested against the Bold sandbox, 7 and 9 without accepted documents in the test environment, and 8 without E2E). The sale (`Sales`, I-25) orchestrates payments, inventory, cash and invoice through contracts in `SaleConfirmation`; voids and returns (D-11, I-26) are in `SaleReversals`, and the report in `SalesReport`. Modules: `Companies` (company, branches, platform onboarding),
`Identity` (users, memberships, roles, per-branch assignments, refresh tokens, devices and PIN),
`Catalog` (categories, global units, taxes, products, prices with base/branch validity and
per-branch availability), `Inventory` (locations, reasons, insert-only movements, balances,
initial stock, adjustments, count and reconciliation), `Cash` (registers, sessions, cash movements, reasons and
closing; the expected amount is `CashMovement.Expected`, the signed sum of the movements) and `Payments` (payment
methods, payments and attempts, encrypted Bold accounts, a card terminal per register; `GatewayPaymentService` charges outside the
transaction and the webhook `POST /api/v1/webhooks/payments/bold` approves with a valid signature; see
`docs/payment-integration.md` §11) and `Invoicing` (Siigo: configuration, pairings, fiscal documents;
`IFiscalDocumentIssuer` creates the sale's invoice and the outbox sends it with `FiscalSubmitHandler`; invoicing is **optional per company** (D-17: `FiscalSettings.IsActive`; when off, the sale stays `not_required`, and whatever was already created is still sent); see
`docs/electronic-invoicing.md` §9). Every stock movement goes through `InventoryLedger` (idempotency by source and the D-06
policy). The audit trail is queried through `IAuditLogReader` (`Application/Auditing`, implemented in Infrastructure).
The rest arrives in phases (`docs/implementation-plan.md`).

- A module does not touch another module's DbSet or entities: what it needs it asks for through `Application/Common/Contracts`
  (`ICompanyDirectory`, `IBranchDirectory`, `ICashRegisterDirectory`, `IIdentityProvisioning`, `ICatalogProvisioning`, `IProductDirectory`,
  `IInventoryProvisioning`, `IUserDirectory`, `ICashProvisioning`, `IPendingPaymentsProbe`, `ICashSessionDirectory`, `IPaymentsProvisioning`, `IFiscalDocumentIssuer`).
- Third-party keys: always through `ISecretProtector` (Data Protection); never in logs or responses.
- Prices (D-07): tax included by default. `ProductPrice.Open` opens a validity period and closes the previous one;
  `ProductPrice.EffectiveAt` gives a branch's price (its own current price or the base one).
- Flows with no company in the session that look up by secret (login, refresh, pairing, PIN) turn off only the
  `QueryFilters.Tenant` filter and then work only with the company carried by the row.
- Master-data `PUT`s require `If-Match` (`Request.IfMatch()` → `IAppDbContext.ExpectVersion`).
- Permissions: catalog in `Common/Security/Permissions.cs` (seeded by migration); default roles in
  `Identity/IdentityProvisioning.cs` (`DefaultRoles`). A new permission requires a migration.

## Stack

.NET 10 (LTS) · ASP.NET Core · EF Core 10 · **PostgreSQL 17** (Npgsql) · MediatR 12 ·
FluentValidation · native OpenAPI (`Microsoft.AspNetCore.OpenApi`, document at `/openapi/v1.json`) ·
Serilog · xUnit + Testcontainers (real PostgreSQL) + NetArchTest.

## Structure (modular monolith by folders, D-14)

```
src/
  Tahona.BakeryPOS.Domain/<Module>/                 entities and rules (Companies/…)
  Tahona.BakeryPOS.Application/<Module>/            use cases: Query/Command + Handler (+ Validator)
  Tahona.BakeryPOS.Application/Common/              abstractions: ITenantContext, IAppDbContext, IOutbox, IInboundEventStore
  Tahona.BakeryPOS.Infrastructure/
    Persistence/                                    AppDbContext (single), per-schema configurations, migrations
    Integration/                                    OutboxProcessor, InboundEventStore, IdempotencyStore
    Security/                                       HttpTenantContext, JwtTokenService
  Tahona.BakeryPOS.Api/                             controllers, [Idempotent], [RequirePermission], middlewares
tests/
  Tahona.BakeryPOS.UnitTests                        domain and pure rules
  Tahona.BakeryPOS.ArchitectureTests                layers and modules (a module does not reference another)
  Tahona.BakeryPOS.IntegrationTests                 API + real PostgreSQL (Docker): isolation, idempotency, outbox…
```

## Foundation pieces (always use them)

- **Multi-company**: every business entity implements `ITenantEntity`. `AppDbContext` applies the filter
  named `Tenant` (EF Core 10) and rejects saving rows of another company (`TenantIsolationException`
  → 403). The company comes only from the token's `company_id` claim.
- **Idempotency**: `[Idempotent]` on every POST that creates something retryable; it requires `Idempotency-Key`.
- **Outbox**: `IOutbox.Enqueue(type, payload)` before `SaveChangesAsync` (same transaction); one
  idempotent `IOutboxHandler` per type. Never call third parties inside a transaction.
- **Webhooks**: `IInboundEventStore.RecordAsync` first (deduplicates by provider + id), then process.
- **Audit**: automatic in `SaveChangesAsync` (`aud.audit_logs`); do not write it by hand. Sensitive fields
  (PIN, passwords, credentials) are masked by name in `AuditTrail`.
- **Errors**: `Result`/`Error` with a code in `SharedKernel/Results/Error.cs`; HTTP status in
  `ResultExtensions.StatusCodeOf`. Body: `{ code, message, details?, correlationId }`.

## Commands

```bash
dotnet tool restore
dotnet build
dotnet test tests/Tahona.BakeryPOS.UnitTests
dotnet test tests/Tahona.BakeryPOS.ArchitectureTests
dotnet test tests/Tahona.BakeryPOS.IntegrationTests          # requires Docker (downloads postgres:17-alpine)
dotnet ef migrations add <Name> --project src/Tahona.BakeryPOS.Infrastructure --startup-project src/Tahona.BakeryPOS.Infrastructure --output-dir Persistence/Migrations
```

### Database (PostgreSQL — I-18)

- Connection string in `ConnectionStrings:DefaultConnection` (`appsettings.Development.json`, or the variable
  `ConnectionStrings__DefaultConnection`, which takes precedence).
- Development Postgres in a container: base compose + dev override, with `-p` and
  `--env-file .env.dev` (not versioned). It publishes port **15432** (5432 = local Windows PostgreSQL,
  5433 = the previous system's database). The API is optional (`--profile api`).

```bash
docker compose -p bakerypos-dev --env-file .env.dev -f docker-compose.yml -f docker-compose.dev.yml up -d db
```

Persistence details: the row version (`row_version`, `bytea`) is renewed by `AppDbContext` on every
change and works as a concurrency token (If-Match); dates are `timestamptz` in UTC; index filters
use double quotes (`"deleted_at" IS NULL`); the outbox claims batches with `FOR UPDATE SKIP
LOCKED`. PostgreSQL compares text case-sensitively: codes and emails are normalized before saving.

`dotnet ef database update` without `ConnectionStrings__DefaultConnection` points at a design database
(`bakerypos_design`); for `bakerypos_dev` it is enough to start the API in Development.

First sign-in in development: set `Seed:PlatformAdminEmail` and
`Seed:PlatformAdminPassword` in user-secrets (and optionally `Seed:PlatformAdminName`); on startup the
platform administrator is created. With it: `POST /api/v1/auth/login` → `POST /api/v1/platform/companies` (returns the
owner's one-time password) → the owner signs in and changes it at `/auth/change-password`.

Development: the API applies migrations on startup only in Development. `Jwt:Secret` goes in user-secrets
(the project has its own `UserSecretsId`).

On this machine, Windows App Control blocks the freshly built `Tahona.BakeryPOS.Api.exe`:
run with `dotnet src/Tahona.BakeryPOS.Api/bin/Debug/net10.0/Tahona.BakeryPOS.Api.dll` (or from Visual Studio).

## Secrets

Never in versioned files. Dev: user-secrets (`Jwt:Secret`; the Postgres password can go in
`ConnectionStrings:DefaultConnection` in user-secrets, which overrides the one in `appsettings.Development.json`) and
`.env.dev`. Other environments: environment variables (`ConnectionStrings__DefaultConnection`, `Jwt__Secret`)
from a secrets manager.

## Deployment (phase 10, D-10)

`deploy/`: staging/production compose (PostgreSQL, migrations bundle from `Dockerfile.migrations`, API and Caddy as the API's HTTPS proxy; no frontend), `Caddyfile`, `.env.deploy.example` and scripts (`deploy.sh`, `backup.sh`, `restore.sh`, `bootstrap-admin.sh`).
Outside development the API requires `DataProtection__KeysPath` and behind Caddy it uses `ReverseProxy__Enabled=true`. Full
guide: `docs/runbooks.md`. Load test: `tests/load/sales-load.js` (k6).
