# Tahona.BakeryPOS — Backend

API for a bakery point-of-sale (POS) SaaS in Colombia. It manages several companies with their branches and cash registers,
the catalog and prices, inventory, cash sessions, sales, card-terminal payments (Bold) and electronic
invoicing (Siigo).

This is an independent repository: it is built, tested, documented and deployed on its own. Any client (cashier POS,
backoffice, other applications) consumes the HTTP API described in [`docs/api-contracts.md`](docs/api-contracts.md) and at
`/openapi/v1.json`.

> Code, database, API (routes, error codes, fields) and documentation are in English. User-facing messages and the display names
> seeded for each company are in Spanish (see the "Language" section of [`CLAUDE.md`](CLAUDE.md)).

## What it does

| Module | Responsibility |
|---|---|
| `Companies` | Companies, branches and onboarding from the platform |
| `Identity` | Users, roles and permissions, sign-in, refresh tokens, devices and PIN |
| `Catalog` | Categories, units, taxes, products, prices with validity and per-branch availability |
| `Inventory` | Locations, movements, balances, adjustments, count and reconciliation |
| `Cash` | Cash registers, sessions, cash income and withdrawals, closing |
| `Payments` | Payment methods, card-terminal payment (Bold) and signed webhook |
| `Invoicing` | Electronic invoicing with Siigo: numberings, pairings and fiscal documents |
| `Sales` / `SaleReversals` / `SalesReport` | Sale, voids, returns and sales report |

## Design principles

- **Company isolation**: every business table carries `company_id`, with a global filter and validation in every use case.
- **Separate states** for the sale, the payment, the inventory and the fiscal document.
- **Nothing external inside a SQL transaction**: the payment gateway and the tax provider are called outside; steps are
  idempotent and workers resume them (outbox / inbound events).
- **A payment is only approved by the backend** (webhook with a valid signature or a server-to-server query).
- **Inventory is never edited**: every change is an idempotent movement.
- **Secrets** stay out of the repository; third-party credentials are stored encrypted per company.

## Technology

.NET 10 · ASP.NET Core · EF Core 10 · PostgreSQL 17 (Npgsql) · MediatR · FluentValidation · native OpenAPI ·
Serilog · xUnit + Testcontainers + NetArchTest. Clean architecture with CQRS in a modular monolith.

## Structure

```
src/
  Tahona.BakeryPOS.Domain/          entities and business rules
  Tahona.BakeryPOS.Application/     use cases (Command/Query + Handler)
  Tahona.BakeryPOS.Infrastructure/  EF Core, migrations, outbox, security
  Tahona.BakeryPOS.Api/             controllers, filters and middlewares
  Tahona.BakeryPOS.SharedKernel/    Result, Error and common types
tests/
  Tahona.BakeryPOS.UnitTests/
  Tahona.BakeryPOS.ArchitectureTests/
  Tahona.BakeryPOS.IntegrationTests/   API + real PostgreSQL (Docker)
  load/                                load test (k6)
deploy/    docker compose, Caddyfile and deployment, backup and restore scripts
docs/      requirements, architecture, data model, API contracts, decisions and runbooks
```

## Requirements

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Docker (development PostgreSQL and integration tests)

## Getting started

1. Development database (publishes port `15432`). Copy `.env.dev.example` to `.env.dev` and adjust the values:

   ```bash
   docker compose -p bakerypos-dev --env-file .env.dev -f docker-compose.yml -f docker-compose.dev.yml up -d db
   ```

2. Development secrets (user-secrets):

   ```bash
   cd src/Tahona.BakeryPOS.Api
   dotnet user-secrets set "Jwt:Secret" "<a long secret>"
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=15432;Database=bakerypos_dev;Username=<user>;Password=<password>"
   dotnet user-secrets set "Seed:PlatformAdminEmail" "<email>"
   dotnet user-secrets set "Seed:PlatformAdminPassword" "<password>"
   ```

3. Run the API (in Development it applies migrations on startup):

   ```bash
   dotnet run --project src/Tahona.BakeryPOS.Api
   ```

   The OpenAPI document is served at `/openapi/v1.json`.

4. First sign-in: `POST /api/v1/auth/login` with the platform administrator and then
   `POST /api/v1/platform/companies` to create a company. The response includes the owner's one-time password, which the
   owner must change at `/api/v1/auth/change-password`.

## Tests

```bash
dotnet test tests/Tahona.BakeryPOS.UnitTests
dotnet test tests/Tahona.BakeryPOS.ArchitectureTests
dotnet test tests/Tahona.BakeryPOS.IntegrationTests    # requires Docker
```

## Migrations

```bash
dotnet ef migrations add <Name> \
  --project src/Tahona.BakeryPOS.Infrastructure \
  --startup-project src/Tahona.BakeryPOS.Infrastructure \
  --output-dir Persistence/Migrations
```

## Deployment

`deploy/` contains the compose file for a VPS (PostgreSQL, migrations, API and Caddy as the API's HTTPS proxy), the `Caddyfile` and the
scripts `deploy.sh`, `backup.sh`, `restore.sh` and `bootstrap-admin.sh`. Outside development you must define
`ConnectionStrings__DefaultConnection`, `Jwt__Secret`, `DataProtection__KeysPath` and `Cors__AllowedOrigins__0` (the origin of the
web client that calls the API from the browser). Full guide in
[`docs/runbooks.md`](docs/runbooks.md).

## CI

`.github/workflows/backend-ci.yml`: builds, runs the unit, architecture and integration tests, and
checks that there are no pending EF Core model changes.

## Documentation

- [`docs/general-guide.md`](docs/general-guide.md) — general guide for non-technical readers, with diagrams
- [`docs/requirements.md`](docs/requirements.md) — requirements
- [`docs/architecture.md`](docs/architecture.md) — architecture
- [`docs/data-model.md`](docs/data-model.md) — data model
- [`docs/database-diagram.md`](docs/database-diagram.md) — database diagram (Mermaid)
- [`docs/api-contracts.md`](docs/api-contracts.md) — API contracts
- [`docs/payment-integration.md`](docs/payment-integration.md) — payments with Bold
- [`docs/electronic-invoicing.md`](docs/electronic-invoicing.md) — electronic invoicing with Siigo
- [`docs/inventory.md`](docs/inventory.md) — inventory
- [`docs/testing-strategy.md`](docs/testing-strategy.md) — testing strategy
- [`docs/decisions.md`](docs/decisions.md) — decisions
- [`docs/runbooks.md`](docs/runbooks.md) — operations
