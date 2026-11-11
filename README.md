# Tahona.BakeryPOS — Backend

API del punto de venta (POS) SaaS para panaderías en Colombia. Gestiona varias empresas con sus sucursales y cajas,
el catálogo y los precios, el inventario, las sesiones de caja, la venta, los cobros con datáfono (Bold) y la
facturación electrónica (Siigo).

Es un repositorio independiente: se compila, prueba, documenta y despliega por sí solo. Cualquier cliente (POS de caja,
backoffice, otras aplicaciones) consume el API HTTP descrito en [`docs/api-contracts.md`](docs/api-contracts.md) y en
`/openapi/v1.json`.

## Qué hace

| Módulo | Responsabilidad |
|---|---|
| `Companies` | Empresas, sucursales y alta desde la plataforma |
| `Identity` | Usuarios, roles y permisos, inicio de sesión, refresh tokens, dispositivos y PIN |
| `Catalog` | Categorías, unidades, impuestos, productos, precios con vigencia y disponibilidad por sucursal |
| `Inventory` | Ubicaciones, movimientos, saldos, ajustes, conteo y reconciliación |
| `Cash` | Cajas, sesiones, ingresos y retiros de efectivo, cierre |
| `Payments` | Medios de pago, cobro en el datáfono (Bold) y webhook firmado |
| `Invoicing` | Facturación electrónica con Siigo: numeraciones, emparejamientos y documentos fiscales |
| `Sales` / `SaleReversals` / `SalesReport` | Venta, anulaciones, devoluciones e informe de ventas |

## Principios de diseño

- **Aislamiento por empresa**: toda tabla de negocio lleva `company_id`, con filtro global y validación en cada caso de uso.
- **Estados separados** para la venta, el pago, el inventario y el documento fiscal.
- **Nada externo dentro de una transacción SQL**: la pasarela y el proveedor fiscal se llaman fuera; los pasos son
  idempotentes y los retoman workers (outbox / eventos entrantes).
- **Un pago solo lo aprueba el backend** (webhook con firma válida o consulta servidor a servidor).
- **El inventario nunca se edita**: todo cambio es un movimiento idempotente.
- **Secretos** fuera del repositorio; las credenciales de terceros se guardan cifradas por empresa.

## Tecnología

.NET 10 · ASP.NET Core · EF Core 10 · PostgreSQL 17 (Npgsql) · MediatR · FluentValidation · OpenAPI nativo ·
Serilog · xUnit + Testcontainers + NetArchTest. Arquitectura limpia con CQRS en un monolito modular.

## Estructura

```
src/
  Tahona.BakeryPOS.Domain/          entidades y reglas de negocio
  Tahona.BakeryPOS.Application/     casos de uso (Command/Query + Handler)
  Tahona.BakeryPOS.Infrastructure/  EF Core, migraciones, outbox, seguridad
  Tahona.BakeryPOS.Api/             controllers, filtros y middlewares
  Tahona.BakeryPOS.SharedKernel/    Result, Error y tipos comunes
tests/
  Tahona.BakeryPOS.UnitTests/
  Tahona.BakeryPOS.ArchitectureTests/
  Tahona.BakeryPOS.IntegrationTests/   API + PostgreSQL real (Docker)
  load/                                prueba de carga (k6)
deploy/    docker compose, Caddyfile y scripts de despliegue, copia de seguridad y restauración
docs/      requisitos, arquitectura, modelo de datos, contratos del API, decisiones y runbooks
```

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Docker (PostgreSQL de desarrollo y pruebas de integración)

## Puesta en marcha

1. Base de datos de desarrollo (publica el puerto `15432`). Copia `.env.dev.example` a `.env.dev` y ajusta los valores:

   ```bash
   docker compose -p bakerypos-dev --env-file .env.dev -f docker-compose.yml -f docker-compose.dev.yml up -d db
   ```

2. Secretos de desarrollo (user-secrets):

   ```bash
   cd src/Tahona.BakeryPOS.Api
   dotnet user-secrets set "Jwt:Secret" "<un secreto largo>"
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=15432;Database=bakerypos_dev;Username=<usuario>;Password=<clave>"
   dotnet user-secrets set "Seed:PlatformAdminEmail" "<correo>"
   dotnet user-secrets set "Seed:PlatformAdminPassword" "<contraseña>"
   ```

3. Ejecutar la API (en Development aplica las migraciones al arrancar):

   ```bash
   dotnet run --project src/Tahona.BakeryPOS.Api
   ```

   El documento OpenAPI queda en `/openapi/v1.json`.

4. Primer acceso: `POST /api/v1/auth/login` con el administrador de plataforma y luego
   `POST /api/v1/plataforma/empresas` para crear una empresa. La respuesta incluye la contraseña de un solo uso del
   propietario, que debe cambiarla en `/api/v1/auth/cambiar-contrasena`.

## Pruebas

```bash
dotnet test tests/Tahona.BakeryPOS.UnitTests
dotnet test tests/Tahona.BakeryPOS.ArchitectureTests
dotnet test tests/Tahona.BakeryPOS.IntegrationTests    # requiere Docker
```

## Migraciones

```bash
dotnet ef migrations add <Nombre> \
  --project src/Tahona.BakeryPOS.Infrastructure \
  --startup-project src/Tahona.BakeryPOS.Infrastructure \
  --output-dir Persistence/Migrations
```

## Despliegue

`deploy/` contiene el compose para un VPS (PostgreSQL, migraciones, API y Caddy como proxy HTTPS del API), el `Caddyfile` y los
scripts `deploy.sh`, `backup.sh`, `restore.sh` y `bootstrap-admin.sh`. Fuera de desarrollo hay que definir
`ConnectionStrings__DefaultConnection`, `Jwt__Secret`, `DataProtection__KeysPath` y `Cors__AllowedOrigins__0` (origen del
cliente web que llama al API desde el navegador). Guía completa en
[`docs/runbooks.md`](docs/runbooks.md).

## CI

`.github/workflows/backend-ci.yml`: compila, ejecuta las pruebas unitarias, de arquitectura y de integración, y
verifica que no haya cambios pendientes del modelo de EF Core.

## Documentación

- [`docs/guia-general.md`](docs/guia-general.md) — guía general para personas no técnicas, con diagramas
- [`docs/requirements.md`](docs/requirements.md) — requisitos
- [`docs/architecture.md`](docs/architecture.md) — arquitectura
- [`docs/data-model.md`](docs/data-model.md) — modelo de datos
- [`docs/api-contracts.md`](docs/api-contracts.md) — contratos del API
- [`docs/payment-integration.md`](docs/payment-integration.md) — pagos con Bold
- [`docs/electronic-invoicing.md`](docs/electronic-invoicing.md) — facturación electrónica con Siigo
- [`docs/inventory.md`](docs/inventory.md) — inventario
- [`docs/testing-strategy.md`](docs/testing-strategy.md) — estrategia de pruebas
- [`docs/decisions.md`](docs/decisions.md) — decisiones
- [`docs/runbooks.md`](docs/runbooks.md) — operación
