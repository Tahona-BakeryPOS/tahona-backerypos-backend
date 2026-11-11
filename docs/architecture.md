# Arquitectura — Tahona.BakeryPOS MVP v1.1

Estado: **borrador para revisión**.

## 1. Entorno inspeccionado (27/10/2026)

| Herramienta | Encontrado | Uso propuesto |
|---|---|---|
| .NET SDK | 9.0.319 y 10.0.401 | **.NET 10 (LTS)**. .NET 9 es STS y su soporte termina en noviembre de 2026. |
| ASP.NET Core runtime | 8.0, 9.0, 10.0.12 | 10.0 |
| Node.js | 26.12.1 | Desarrollo con 26; CI con la LTS vigente que soporte Angular 22 (verificar matriz oficial). |
| Angular | 22.2 (en v1) | Angular 22, standalone, signals, zoneless (como v1) |
| TypeScript | 6.0 | Según Angular 22 |
| PostgreSQL | 17 (contenedor) | Desarrollo: contenedor `postgres:17-alpine` como en la v1 (I-18) |
| Docker | 29.8 | Compose para PostgreSQL en desarrollo y pruebas de integración |
| Git | 2.56 | Repo propio e independiente de cualquier cliente — ver D-13 |
| Pruebas | xUnit (unitarias, integración y arquitectura) | + pruebas de integración con PostgreSQL real y E2E (ver testing-strategy) |

## 2. Estilo: monolito modular

Un solo proceso ASP.NET Core y una sola base de datos, con **módulos de límites explícitos**. Cada
módulo es dueño de sus tablas (esquema SQL propio) y expone a los demás solo comandos/consultas
públicas o eventos; nunca entidades ni acceso directo a tablas ajenas.

| Módulo | Esquema SQL | Responsabilidad |
|---|---|---|
| Companies | `org` | Empresas, sucursales, configuración, zona horaria |
| Identity | `sec` | Usuarios, pertenencias, roles, permisos, dispositivos, sesiones |
| Catalog | `cat` | Productos, categorías, unidades, impuestos, precios por sucursal, disponibilidad |
| Inventory | `inv` | Ubicaciones, movimientos, saldos, ajustes, reconciliación |
| Cash | `cash` | Cajas, sesiones de caja, movimientos de efectivo, cierres |
| Sales | `sales` | Ventas, líneas, devoluciones, orquestación del proceso de venta |
| Payments | `pay` | Pagos, intentos, eventos de pasarela, reembolsos, conciliación |
| Invoicing | `fis` | Configuración fiscal, numeración, documentos, intentos de emisión |
| Audit / Integration | `aud`, `int` | Registro de auditoría, bandeja de salida (outbox), eventos entrantes |

### Estructura de la solución (propuesta)
```
backend/
  src/
    Tahona.BakeryPOS.Api/                 → host: controllers, auth, middlewares, OpenAPI
    Tahona.BakeryPOS.SharedKernel/        → Result, Error, tipos de dinero, ids, interfaces comunes
    Modules/
      Companies/ Companies.Application  Companies.Domain  Companies.Infrastructure
      Identity/  …
      Catalog/   …
      Inventory/ …
      Cash/      …
      Sales/     …
      Payments/  … + Payments.Wompi   (adaptador de pasarela)
      Invoicing/ … + Invoicing.<Proveedor> (adaptador fiscal, tras D-04)
    Tahona.BakeryPOS.Infrastructure.Persistence → un solo DbContext con un esquema por módulo (decisión I-01)
  tests/
    *.UnitTests, *.IntegrationTests (PostgreSQL real), Architecture.Tests (límites entre módulos)
```
Alternativa más ligera (D-14): mantener los 4 proyectos de la v1 y separar módulos por carpeta,
haciendo cumplir los límites con pruebas de arquitectura. Recomendado: **carpetas + pruebas de
arquitectura** en el MVP; partir en proyectos solo si un módulo crece.

### Patrones que se conservan de la v1
CQRS con MediatR · Result/`Error` en vez de excepciones para negocio · FluentValidation ·
EF Core con proyección directa a DTO en consultas · auditoría automática en `SaveChangesAsync` ·
permisos como claims con `[RequirePermission]` · UUID v7 como clave.

## 3. Multiempresa y seguridad

- **Aislamiento por fila**: toda tabla de negocio lleva `company_id`. Cada DbContext aplica un filtro
  global `company_id = @empresaDelToken`. Las escrituras validan que las referencias (sucursal, caja,
  producto) pertenezcan a la misma empresa (también con FK compuestas `(company_id, id)` donde aplica).
- **Defensa en profundidad**: además del filtro, cada handler valida pertenencia y permisos; las
  pruebas de aislamiento intentan leer y escribir con ids de otra empresa en **todos** los endpoints.
- **Autenticación**:
  - Backoffice: correo + contraseña (BCrypt) → JWT corto (15–60 min) + refresh token rotatorio
    almacenado como hash. MFA fuera del MVP.
  - Caja: dispositivo vinculado (token de dispositivo revocable) + PIN de cajero → JWT de sesión.
  - Plataforma: rol de administrador de plataforma separado, sin acceso a datos de ventas.
- **Autorización**: permiso + alcance (empresa, sucursal, caja) evaluados en backend en cada operación.
- **Secretos**: `Jwt:Secret`, cadenas de conexión y llaves de plataforma por variables de entorno /
  user-secrets / gestor de secretos. Credenciales de pasarela y proveedor fiscal **por empresa** se
  guardan cifradas (ASP.NET Data Protection con llaves persistidas fuera de la base; en nube, Key
  Vault). Nunca se registran en logs (ya hay un precedente: la v1 llegó a loguear el PIN).

## 4. Flujo de venta: proceso persistente

La venta no se resuelve en una sola transacción. Es un proceso con estados persistidos y pasos
idempotentes; cada llamada externa ocurre **fuera** de transacciones SQL.

```
[1] POST /ventas (Idempotency-Key)            → TX: venta 'borrador' + líneas con precios/impuestos congelados
[2] POST /ventas/{id}/pagos (efectivo|pasarela)→ TX: intento de pago 'creado' (referencia única)
      └─ si pasarela: fuera de TX → crear transacción en la pasarela → TX: guardar id externo, 'pendiente'
[3] Webhook o consulta de estado              → TX: intento 'aprobado'/'rechazado' (+ evento entrante deduplicado)
[4] Confirmación (cuando pagos aprobados = total) → TX única:
        venta 'confirmada' + pagos aplicados + movimientos de inventario + movimiento de caja (efectivo)
        + documento fiscal 'pendiente' + mensajes de outbox
[5] Worker de facturación (outbox)            → fuera de TX: enviar al proveedor → TX: guardar intento/resultado
[6] Worker de seguimiento                     → consultar estado de documentos/pagos 'desconocidos'
```

Estados separados (detalle en `data-model.md`):
- Venta: `borrador → pendiente_pago → confirmada → (anulada)`; `cancelada` si nunca se pagó.
- Pago/intento: `creado → pendiente → aprobado | rechazado | cancelado | error | desconocido`.
- Inventario de la venta: `no_aplicado → aplicado → (revertido)`.
- Documento fiscal: `pendiente → enviando → aceptado | rechazado | desconocido | error_temporal`.

### Qué pasa si falla cada paso

| Falla | Efecto | Recuperación | Evita duplicado |
|---|---|---|---|
| [1] se corta la red al crear | El cliente no sabe si se creó | Reintenta con la misma `Idempotency-Key` → devuelve la misma venta | Clave idempotente única por empresa |
| [2] timeout al crear transacción en pasarela | Intento en `desconocido` | Worker consulta por id/referencia antes de cualquier reintento | Referencia = id del intento, única; no se crea otro intento hasta resolver |
| [3] webhook no llega | Intento queda `pendiente` | Worker consulta la transacción cada N segundos hasta estado final o vencimiento | Transiciones solo hacia adelante; evento con id ya procesado se ignora |
| [3] webhook duplicado o con firma inválida | — | Se responde 200 sin efecto / se rechaza y se audita | Tabla de eventos entrantes con id único |
| [4] falla la transacción de confirmación | Pago aprobado pero venta sin confirmar | Worker reintenta la confirmación (idempotente) | `inventory_status`, clave de movimiento `(origen, línea)` única |
| [4] pago aprobado de una venta cancelada/vencida | Dinero recibido sin venta | Se marca para **reembolso** y alerta en conciliación | Conciliación diaria |
| [5] timeout al emitir documento | Documento `desconocido` | **Consultar estado** en el proveedor antes de reenviar | Mismo número/consecutivo e id de documento en el reintento |
| [5] rechazo del proveedor/DIAN | Documento `rechazado` con motivo | Corrección y nuevo envío según procedimiento del proveedor | Nuevo intento enlazado al mismo documento |
| Caída del proceso en cualquier punto | Estados intermedios | Workers retoman desde el estado persistido | Todos los pasos idempotentes |

## 5. Integraciones

- **Puertos y adaptadores**: `IPaymentGateway` e `IFiscalDocumentProvider` en Application; un
  adaptador por proveedor en Infrastructure. Ningún módulo de negocio conoce al proveedor concreto.
- **Outbox** (tabla `int.outbox_messages`) escrito en la misma transacción del cambio; un
  `BackgroundService` lo publica con reintentos exponenciales y bloqueo por fila.
- **Eventos entrantes** (`int.inbound_events`): cada webhook se guarda crudo, se valida la firma,
  se deduplica por id del proveedor y se procesa de forma asíncrona.
- **Sin simulaciones en producción**: los adaptadores de prueba (fakes) existen solo en pruebas; en
  producción el arranque falla si el proveedor no está configurado.

## 6. Clientes del API

El backend no depende de ningún cliente: expone un API HTTP/JSON versionado (`/api/v1`, documento OpenAPI en
`/openapi/v1.json`) y se compila, prueba y despliega por sí solo. Los clientes (por ejemplo, una PWA para el POS y el
backoffice) viven en repositorios propios y dependen solo del contrato (`api-contracts.md`).

Lo que el API exige a cualquier cliente:
- Requiere conexión: no existe modo sin conexión ni sincronización.
- `Idempotency-Key` en los POST reintentables e `If-Match` en los PUT de datos maestros.
- El estado del pago y del documento fiscal se consulta con *polling* corto.
- Si el cliente corre en otro origen, ese origen se declara en `Cors:AllowedOrigins`.

## 7. Despliegue (D-10)

Un VPS por ambiente con Docker Compose (`deploy/`): PostgreSQL, la API, las migraciones (bundle de `dotnet ef`, como paso
previo y nunca al arrancar en producción) y Caddy como proxy HTTPS del API. No se construye ni se sirve ningún cliente.
Guía de operación: `runbooks.md`.
