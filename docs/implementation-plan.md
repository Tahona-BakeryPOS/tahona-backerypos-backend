# Implementation plan — MVP v1.1

It starts only once this design and the blocking decisions of each phase are approved
([`decisions.md`](decisions.md)). Each phase delivers API, tests, documentation and
acceptance criteria; it stops if critical tests fail.

| # | Phase | Deliverables | Reused from the previous system | Decisions | Acceptance criteria |
|---|---|---|---|---|---|
| 1 ✅ (local tests; CI not yet run on GitHub) | Technical foundation and security | .NET 10, PostgreSQL (I-18), modules by folder, company filter, `Idempotency-Key`, outbox, inbound events, audit, OpenAPI, CI with real PostgreSQL, architecture tests | Layers, Result, MediatR, automatic audit, JWT, CI | D-01, D-02, D-13, D-14, D-16 | Green CI with the initial migration on PostgreSQL; sample isolation test |
| 2 ✅ backend (local tests) | Companies, branches and users | Company onboarding (platform), memberships, per-branch roles, backoffice login + company selection, refresh tokens, PIN lockout | Backoffice: Company, Branches, Users, device pairing | D-08, D-09 | Two demo companies without cross access (per-endpoint tests) |
| 3 ✅ backend (compiles; tests not run) | Catalog and prices | Products, base and per-branch price with validity, per-branch availability, taxes with DIAN codes | Catalog, categories, taxes, `sale-math` | D-07 | Correct current price per branch and date (tests) |
| 4 ✅ backend (compiles; tests not run) | Basic inventory | Locations, movements, balances, initial stock, authorized adjustments, reconciliation, stock and movement screens | — | D-06 | Balance = Σ movements; idempotency; concurrency |
| 5 ✅ backend (compiles; tests not run) | Registers and shifts | Cash sessions, movements, closing with the expected amount (includes refunds), blocking on pending items | Shifts, cash, closing (change fix) | D-12 | Correct expected amount in mixed scenarios |
| 6 ✅ backend with Bold (compiles; **the Bold sandbox test is missing**, an acceptance criterion) | Payment gateway | Wompi adapter (or the chosen one), attempts, signed webhooks, recovery worker, reconciliation, encrypted credentials | Payment methods | D-03 | Tests against the **real sandbox** + scenarios 2–6 |
| 7 ✅ backend with Siigo (compiles; **an accepted document in the test environment is missing**, an acceptance criterion) | Electronic invoicing | Tax configuration, numbering, documents, issue and query worker, artifacts, notes | — | D-04, D-05, D-15 | Document accepted in the test environment with verifiable CUFE/CUDE; scenarios 7–8 |
| 8 ✅ backend (compiles; tests not run; **the E2E in the Bold and Siigo sandboxes is missing**) | Full POS integration | Sale as a persistent process, payment with live status, receipt with tax status; offline mode removed | POS screens | — | E2E cash sale and Nequi/QR in the sandbox |
| 9 ✅ backend (compiles; tests not run; **credit note not tested in the Siigo test environment**) | Audit, returns and queries | Returns with refund, compensating inventory and note; voids; payment/document/movement queries | Sales, audit | D-11 | Scenario 10 and refunds |
| 10 ✅ files and guides ready (they build; **nothing deployed or run**: E2E, load and restore pending on staging) | End-to-end tests and deployment | Full E2E, basic load, migrations bundle, IaC/compose, recovery runbooks | Docker, CI | D-10 | Exit criteria of `testing-strategy.md` |

## New dependencies to approve (D-16)

| Package | What for |
|---|---|
| Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL provider (I-18) |
| Testcontainers.PostgreSql | Real PostgreSQL in integration tests |
| NetArchTest.Rules | Module boundary tests |
| Microsoft.Extensions.Http.Resilience | Retries/timeouts towards the gateway and the tax provider |
| Azure.Storage.Blobs (or equivalent) | Retention of fiscal XML/PDF |

## Main risks

| Risk | Mitigation |
|---|---|
| Ambiguous electronic POS regulation (UVT, annex, calendar) | Validate with the accountant before phase 7 (D-05) |
| Tax provider without a suitable API for multi-NIT SaaS | Evaluate two in a sandbox before committing |
| Gateway without an API refund for Nequi/QR | Manual refund recorded and approved |
| Card terminal that cannot be integrated | External method with reference and manual reconciliation |
| DIAN enablement for each client company lengthens onboarding | Include the enablement status in company onboarding |
