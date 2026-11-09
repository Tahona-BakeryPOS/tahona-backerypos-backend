# Tahona.BakeryPOS v2 — Backend

POS SaaS para panaderías en Colombia (MVP v1.1). Este repo es **solo el backend** (API .NET); el frontend (PWA Angular)
vive en su propio repo: `tahona-backerypos-frontend`. Cada repo tiene su CI/CD y su documentación.
Rama de trabajo: `v2` (la v1 queda en el commit base `053f046`).

Repo: <https://github.com/Tahona-BakeryPOS/tahona-backerypos-backend>

## Antes de implementar

1. Leer `docs/requirements.md`, `docs/architecture.md`, `docs/data-model.md` y `docs/decisions.md`.
2. Confirmar que las decisiones bloqueantes de la fase estén aprobadas (`docs/decisions.md`).
3. Seguir el orden de `docs/implementation-plan.md`.
4. Estado de los casos de uso y próximos pasos: `docs/estado-casos-de-uso.md` (leerlo al iniciar cada sesión).
5. Despliegue y operación: `docs/runbooks.md` (archivos en `deploy/`).

El contrato HTTP que consume el frontend está en `docs/api-contracts.md`: un cambio de contrato se avisa al repo del frontend.

## Reglas no negociables

- **Aislamiento por empresa**: toda tabla de negocio con `company_id`; filtro global + validación en
  cada handler + prueba de acceso cruzado por endpoint.
- **Estados separados**: venta, pago, inventario y documento fiscal tienen cada uno su estado.
- **Nada externo dentro de una transacción SQL**: pasarela y proveedor fiscal se llaman fuera; los
  pasos son idempotentes y los retoman workers desde el estado persistido (outbox / inbound events).
- **Un pago solo se aprueba por el backend** (webhook con firma válida o consulta servidor-a-servidor).
- **Un documento fiscal solo es "aceptado" con confirmación verificable del proveedor**; si el
  resultado de un envío es desconocido, se consulta el estado antes de reenviar, con el mismo número.
- **Inventario**: el saldo nunca se edita; todo cambio es un movimiento idempotente.
- **Sin endpoints ni respuestas inventadas** para proveedores externos; lo no verificado se marca
  "por verificar" en `docs/`.
- **Secretos** fuera del repositorio; credenciales de terceros cifradas por empresa; nunca en logs.
- Pedir aprobación antes de: cambiar decisiones de negocio, añadir dependencias importantes, cambiar
  el modelo de datos aprobado, migraciones destructivas, configurar integraciones de producción o
  cambiar el alcance.

Convenciones: Clean Architecture + CQRS (MediatR), un Command/Query + Handler (+ Validator) por caso de uso,
Result/`Error` con códigos en mayúsculas, FluentValidation, EF Core con proyección a DTO en consultas,
UUID v7, permisos como claims `[RequirePermission("modulo.accion")]`, auditoría automática en `SaveChangesAsync`.

## Estado

**Fases 1 a 10 implementadas** (la 6 sin probar contra el sandbox de Bold, la 7 y la 9 sin documentos aceptados en habilitación y la 8 sin E2E). La venta (`Sales`, I-25) orquesta pagos, inventario, caja y factura vía contratos en `SaleConfirmation`; la anulación y las devoluciones (D-11, I-26) en `SaleReversals`, y el informe en `SalesReport`. Módulos: `Companies` (empresa, sucursales, alta de plataforma),
`Identity` (usuarios, pertenencias, roles, asignaciones por sucursal, refresh tokens, dispositivos y PIN),
`Catalog` (categorías, unidades globales, impuestos, productos, precios con vigencia base/sucursal y
disponibilidad por sucursal), `Inventory` (ubicaciones, motivos, movimientos de solo inserción, saldos,
inicial, ajustes, conteo y reconciliación) y `Cash` (cajas, sesiones, movimientos de efectivo, motivos y
cierre; el esperado es `CashMovement.Expected`, la suma con signo de los movimientos) y `Payments` (medios de
pago, pagos e intentos, cuentas de Bold cifradas, datáfono por caja; `GatewayPaymentService` cobra fuera de la
transacción y el webhook `POST /api/v1/webhooks/pagos/bold` aprueba con firma válida; ver
`docs/payment-integration.md` §11) e `Invoicing` (Siigo: configuración, emparejamientos, documentos fiscales;
`IFiscalDocumentIssuer` crea la factura de la venta y el outbox la envía con `FiscalSubmitHandler`; la facturación es **opcional por empresa** (D-17: `FiscalSettings.IsActive`; apagada, la venta queda `not_required`, y lo ya creado se sigue enviando); ver
`docs/electronic-invoicing.md` §9). Todo movimiento de
existencias pasa por `InventoryLedger` (idempotencia por origen y política D-06). La auditoría se consulta por
`IAuditLogReader` (`Application/Auditing`, implementado en Infrastructure). El resto llega por fases (`docs/implementation-plan.md`).

- Un módulo no toca DbSet ni entidades de otro: lo que necesita lo pide por `Application/Common/Contracts`
  (`ICompanyDirectory`, `IBranchDirectory`, `ICashRegisterDirectory`, `IIdentityProvisioning`, `ICatalogProvisioning`, `IProductDirectory`,
  `IInventoryProvisioning`, `IUserDirectory`, `ICashProvisioning`, `IPendingPaymentsProbe`, `ICashSessionDirectory`, `IPaymentsProvisioning`, `IFiscalDocumentIssuer`).
- Llaves de terceros: siempre por `ISecretProtector` (Data Protection); nunca en logs ni en respuestas.
- Precios (D-07): con impuesto incluido por defecto. `ProductPrice.Open` abre una vigencia y cierra la anterior;
  `ProductPrice.EffectiveAt` da el precio de una sucursal (el propio vigente o el base).
- Flujos sin empresa en sesión que buscan por secreto (login, refresh, vincular, PIN) apagan solo el
  filtro `QueryFilters.Tenant` y después trabajan únicamente con la empresa que trae la fila.
- `PUT` de datos maestros exige `If-Match` (`Request.IfMatch()` → `IAppDbContext.ExpectVersion`).
- Permisos: catálogo en `Common/Security/Permissions.cs` (sembrado por migración); roles por defecto en
  `Identity/IdentityProvisioning.cs` (`DefaultRoles`). Un permiso nuevo requiere migración.

## Stack

.NET 10 (LTS) · ASP.NET Core · EF Core 10 · **PostgreSQL 17** (Npgsql, igual que la v1) · MediatR 12 ·
FluentValidation · OpenAPI nativo (`Microsoft.AspNetCore.OpenApi`, documento en `/openapi/v1.json`) ·
Serilog · xUnit + Testcontainers (PostgreSQL real) + NetArchTest.

## Estructura (monolito modular por carpetas, D-14)

```
src/
  Tahona.BakeryPOS.Domain/<Módulo>/                 entidades y reglas (Companies/…)
  Tahona.BakeryPOS.Application/<Módulo>/            casos de uso: Query/Command + Handler (+ Validator)
  Tahona.BakeryPOS.Application/Common/              abstracciones: ITenantContext, IAppDbContext, IOutbox, IInboundEventStore
  Tahona.BakeryPOS.Infrastructure/
    Persistence/                                    AppDbContext (único), configuraciones por esquema, migraciones
    Integration/                                    OutboxProcessor, InboundEventStore, IdempotencyStore
    Security/                                       HttpTenantContext, JwtTokenService
  Tahona.BakeryPOS.Api/                             controllers, [Idempotent], [RequirePermission], middlewares
tests/
  Tahona.BakeryPOS.UnitTests                        dominio y reglas puras
  Tahona.BakeryPOS.ArchitectureTests                capas y módulos (un módulo no referencia a otro)
  Tahona.BakeryPOS.IntegrationTests                 API + PostgreSQL real (Docker): aislamiento, idempotencia, outbox…
```

## Piezas de la fundación (usar siempre)

- **Multiempresa**: toda entidad de negocio implementa `ITenantEntity`. `AppDbContext` aplica el filtro
  con nombre `Tenant` (EF Core 10) y rechaza al guardar filas de otra empresa (`TenantIsolationException`
  → 403). La empresa sale solo del claim `company_id` del token.
- **Idempotencia**: `[Idempotent]` en todo POST que cree algo reintentable; exige `Idempotency-Key`.
- **Outbox**: `IOutbox.Enqueue(tipo, payload)` antes de `SaveChangesAsync` (misma transacción); un
  `IOutboxHandler` por tipo, idempotente. Nunca llamar a terceros dentro de una transacción.
- **Webhooks**: `IInboundEventStore.RecordAsync` primero (deduplica por proveedor + id), luego procesar.
- **Auditoría**: automática en `SaveChangesAsync` (`aud.audit_logs`); no escribirla a mano. Campos
  sensibles (PIN, contraseñas, credenciales) se enmascaran por nombre en `AuditTrail`.
- **Errores**: `Result`/`Error` con código en `SharedKernel/Results/Error.cs`; status HTTP en
  `ResultExtensions.StatusCodeOf`. Cuerpo: `{ codigo, mensaje, detalles?, correlationId }`.

## Comandos

```bash
dotnet tool restore
dotnet build
dotnet test tests/Tahona.BakeryPOS.UnitTests
dotnet test tests/Tahona.BakeryPOS.ArchitectureTests
dotnet test tests/Tahona.BakeryPOS.IntegrationTests          # requiere Docker (descarga postgres:17-alpine)
dotnet ef migrations add <Nombre> --project src/Tahona.BakeryPOS.Infrastructure --startup-project src/Tahona.BakeryPOS.Infrastructure --output-dir Persistence/Migrations
```

### Base de datos (PostgreSQL, igual que la v1 — I-18)

- Cadena en `ConnectionStrings:DefaultConnection` (`appsettings.Development.json`, o la variable
  `ConnectionStrings__DefaultConnection`, que tiene prioridad).
- Postgres de desarrollo en contenedor, como en la v1: compose base + override de dev, con `-p` y
  `--env-file .env.dev` (no versionado). Publica el puerto **15432** (5432 = PostgreSQL local de Windows,
  5433 = base de la v1). La API es opcional (`--profile api`).

```bash
docker compose -p bakerypos-v2-dev --env-file .env.dev -f docker-compose.yml -f docker-compose.dev.yml up -d db
```

Detalles de la persistencia: la versión de fila (`row_version`, `bytea`) la renueva `AppDbContext` en cada
cambio y funciona como token de concurrencia (If-Match); las fechas son `timestamptz` en UTC; los filtros
de índice usan comillas dobles (`"deleted_at" IS NULL`); el outbox reclama lotes con `FOR UPDATE SKIP
LOCKED`. PostgreSQL compara textos distinguiendo mayúsculas: códigos y correos se normalizan antes de guardar.

`dotnet ef database update` sin `ConnectionStrings__DefaultConnection` apunta a una base de diseño
(`bakerypos_design`); para `bakerypos_v2` basta con arrancar la API en Development.

Primer acceso en desarrollo: define en user-secrets `Seed:PlatformAdminEmail` y
`Seed:PlatformAdminPassword` (y opcional `Seed:PlatformAdminName`); al arrancar se crea el administrador
de plataforma. Con él: `POST /api/v1/auth/login` → `POST /api/v1/plataforma/empresas` (devuelve la
contraseña de un solo uso del propietario) → el propietario entra y la cambia en `/auth/cambiar-contrasena`.

Desarrollo: la API aplica migraciones al arrancar solo en Development. `Jwt:Secret` va en user-secrets
(la v2 tiene su propio `UserSecretsId`).

En este equipo, Windows App Control bloquea el ejecutable `Tahona.BakeryPOS.Api.exe` recién compilado:
correr con `dotnet src/Tahona.BakeryPOS.Api/bin/Debug/net10.0/Tahona.BakeryPOS.Api.dll` (o desde Visual Studio).

## Secretos

Nunca en archivos versionados. Dev: user-secrets (`Jwt:Secret`; la contraseña de Postgres puede ir en
`ConnectionStrings:DefaultConnection` de user-secrets, que pisa la de `appsettings.Development.json`) y
`.env.dev`. Otros ambientes: variables de entorno (`ConnectionStrings__DefaultConnection`, `Jwt__Secret`)
desde un gestor de secretos.

## Despliegue (fase 10, D-10)

`deploy/`: compose de staging/producción (PostgreSQL, bundle de migraciones de `Dockerfile.migrations`, API y Caddy con el
frontend), `Caddyfile`, `.env.deploy.example` y scripts (`deploy.sh`, `backup.sh`, `restore.sh`, `bootstrap-admin.sh`).
Fuera de desarrollo la API exige `DataProtection__KeysPath` y detrás de Caddy usa `ReverseProxy__Enabled=true`. Guía
completa: `docs/runbooks.md`. Prueba de carga: `tests/load/sales-load.js` (k6).
