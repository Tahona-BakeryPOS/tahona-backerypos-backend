# BakeryPOS — Contexto del proyecto

Backend de un sistema POS (punto de venta) tipo SaaS para panaderías de
barrio en Colombia, propiedad del autor (varias sedes). Diseñado como
Clean Architecture + CQRS en .NET.

## Estado actual

Backend v1 (módulo de ventas y caja) con los **11 casos de uso completos**.
Frontend (PWA) todavía no se ha empezado.

## Diseño de referencia

El diseño completo (28 tablas, 11 casos de uso, reglas de negocio, contrato
de API) vive en un Claude Doc, no en este repo:
https://claude.ai/artifact/PRzgamoUfHT8ufQ71EwFm2

Antes de implementar algo nuevo, consultar ese doc para reglas de negocio
y nombres de tablas/columnas en español/inglés.

## Arquitectura

- **Clean Architecture**: `Domain` → `Application` → `Infrastructure` → `Api`
- **CQRS con MediatR**: un Command/Query + Handler (+ Validator si aplica)
  por caso de uso, bajo `Application/<Feature>/{Commands,Queries}/`
- **PostgreSQL** (Npgsql), snake_case, soft delete vía filtro global
  (`DeletedAt`), columnas de auditoría (`CreatedAt/By`, `ModifiedAt/By`,
  `RowVersion`, `DeletedAt/By`) en `AuditableEntity`
- **UUID v7** como PK (`Guid.CreateVersion7()`), para idempotencia offline-first
- **JWT** con login por PIN (BCrypt) para cajeros; permisos como claims
  individuales (`permission`), chequeados con `[RequirePermission("codigo")]`
- **Patrón Result** (`Result` / `Result<T>`) en vez de excepciones para
  flujo de negocio; códigos de error en `SharedKernel/Results/Error.cs`
- **Multi-tenant** por `organization_id` (row-level, no schema-per-tenant)

## Decisión pendiente de ejecutar: quitar Dapper

El query-side de CQRS usa **EF Core + Dapper** (comandos con EF, queries con
Dapper/SQL crudo). Se decidió en conversación que **esto es sobre-ingeniería
para este proyecto** (solo-dev, volumen bajo) y se va a reemplazar por
**EF Core solo**, usando `.AsNoTracking()` + proyección directa a DTO
(`.Select(x => new Dto {...})`) en vez de SQL crudo.

Razones: SQL como string no se valida en compilación, duplica DTOs/mapeo,
dos paradigmas mentales que mantener. `AsNoTracking()` + proyección da el
mismo beneficio de performance (sin tracking overhead) sin la complejidad.
Dejar la puerta abierta a `FromSqlRaw` puntual si algún reporte específico
lo requiere en producción — no una capa paralela completa.

**Pendiente de hacer** (siguiente tarea al abrir este repo en Claude Code):
1. Reescribir estos 4 handlers de `IDapperQueryService` a EF Core con
   `AsNoTracking()` + `.Select()`:
   - `Sales/Queries/GetSaleByIdHandler.cs`
   - `Products/Queries/GetProductsHandler.cs`
   - `Reports/Queries/GetDailySalesReportHandler.cs`
   - `CashMovements/Queries/GetCashMovementsByShiftHandler.cs`
2. Quitar `Dapper` e `IDapperQueryService`/`DapperQueryService` del proyecto
   (`Application/Common/Interfaces/IDapperQueryService.cs`,
   `Infrastructure/Persistence/Queries/DapperQueryService.cs`)
3. Quitar el `PackageReference` de `Dapper` en
   `BakeryPOS.Infrastructure.csproj` y su registro en
   `InfrastructureServiceRegistration.cs`

## Los 11 casos de uso (v1)

| CU | Nombre | Dónde vive |
|---|---|---|
| CU-01 | Iniciar sesión con PIN | `Application/Auth/Commands/LoginByPinCommand` |
| CU-02 | Abrir turno | `Application/CashShifts/Commands/OpenCashShiftCommand` |
| CU-03 | Registrar venta | `Application/Sales/Commands/CreateSaleCommand` |
| CU-04 | Cobrar | Reglas de pago dentro de `CreateSaleHandler` (efectivo admite excedente/cambio, no-efectivo no) |
| CU-05 | Anular venta | `Application/Sales/Commands/VoidSaleCommand` |
| CU-06 | Ingreso/retiro de efectivo | `Application/CashMovements/Commands/RegisterCashMovementCommand` |
| CU-07 | Cerrar turno | `Application/CashShifts/Commands/CloseCashShiftCommand` |
| CU-08 | Consultar reportes | `Application/Reports/Queries/GetDailySalesReportQuery` |
| CU-09 | Gestionar catálogo y precios | `Application/Catalog/Commands/*`, `Application/Products/Commands/*` |
| CU-10 | Gestionar sucursales/cajas/usuarios | `Application/Organizations/Commands/*` |
| CU-11 | Sincronizar ventas pendientes | `Application/Sync/Commands/SyncSalesCommand` |

## Convenciones a seguir

- Namespace de feature = carpeta: `Application/<Feature>/Commands|Queries/`
- Cada Command/Query es un `record`; el Handler implementa
  `IRequestHandler<TRequest, Result<T>>` o `IRequestHandler<TRequest, Result>`
- Validación con FluentValidation, un `Validator` por Command si tiene
  reglas de entrada (no todas las queries lo necesitan)
- Nunca borrar físicamente: desactivar (`IsActive = false`) o soft-delete
  (`DeletedAt`/`DeletedBy`), según el caso de uso lo indique
- Códigos de permiso en punto: `turnos.abrir`, `ventas.crear`,
  `catalogo.gestionar`, `organizacion.gestionar`, etc. — ver
  `[RequirePermission(...)]` en cada controller para la lista completa
- Códigos de error en mayúsculas con guion bajo, agregar a `Error.cs` y a
  `ResultExtensions.ToErrorResult` (mapeo a status HTTP) cuando se cree uno nuevo

## Build

Requiere .NET 9 SDK.

```bash
dotnet restore
dotnet build
```

Falta: migraciones de EF (`dotnet ef migrations add Initial`), seed de
`permissions`/`roles`/`payment_methods` (el código asume que existen en BD,
p. ej. el medio de pago con `Code = "EFECTIVO"`).
