# Estrategia de pruebas

Regla: una funcionalidad no está terminada si sus pruebas no corren de verdad. Lo que dependa de un
proveedor externo se prueba contra su **sandbox real**; los fakes solo sirven para pruebas unitarias
y nunca para declarar una integración terminada.

## Niveles

| Nivel | Herramienta | Qué cubre | Dónde corre |
|---|---|---|---|
| Unitarias (dominio) | xUnit | Totales, impuestos incluidos/no incluidos, redondeo, cambio, transiciones de estado de venta/pago/documento, política de existencias, numeración | CI en cada PR |
| Integración (persistencia) | xUnit + **PostgreSQL real** (Testcontainers, D-16) | Restricciones e índices únicos, filtros por empresa, concurrencia de saldos, idempotencia, outbox en la misma TX | CI |
| API / contrato | `WebApplicationFactory` + PostgreSQL | Códigos de error, `Idempotency-Key`, `If-Match`, permisos por endpoint, OpenAPI sin cambios incompatibles | CI |
| **Aislamiento multiempresa** | Generadas por endpoint | Con tokens de empresa A, leer/escribir ids de empresa B en **todos** los endpoints → 404/403, nunca datos | CI (obligatoria, bloquea merge) |
| Arquitectura | NetArchTest (D-16) | Un módulo no referencia el dominio ni las tablas de otro | CI |
| Integración con pasarela | Sandbox del proveedor | Crear transacción, webhook firmado real, consulta de estado, reembolso | Manual/nocturno con credenciales de sandbox |
| Integración fiscal | Ambiente de pruebas del proveedor/DIAN | Emitir, consultar, rechazo, nota crédito | Manual/nocturno |
| Frontend unitarias | Vitest (Angular 22) | Stores, cálculos, componentes | CI |
| E2E | Playwright (D-16), `tahona-backerypos-frontend/e2e` | Flujo completo del POS y backoffice contra API real + PostgreSQL (efectivo, anulación, devolución, cierre, informe) | Staging, antes de cada paso a producción (`npm run e2e`); en CI solo compila |
| Carga | k6, `backend/tests/load/sales-load.js` | 20 cajas vendiendo en efectivo: p95 < 800 ms, < 1 % de errores | Staging, antes de producción |

## Escenarios críticos de fallo (automatizados)

1. Reintento de `POST /ventas` con la misma `Idempotency-Key` → una sola venta.
2. Timeout al crear transacción en la pasarela → intento `unknown`; el worker la resuelve por consulta; no se crea un segundo cobro.
3. Webhook duplicado → un solo cambio de estado; webhook con firma inválida → rechazado y auditado.
4. Webhook nunca llega → el worker consulta y confirma la venta.
5. Pago aprobado pero falla la transacción de confirmación → el reintento confirma una vez; inventario descontado una vez.
6. Pago aprobado de una venta cancelada → queda marcado para reembolso en conciliación.
7. Timeout al emitir el documento → `unknown`; no hay reenvío sin consulta previa; mismo número.
8. Rechazo fiscal → `rejected` con motivos; ningún estado "aceptado" ficticio.
9. Dos cajas venden la última unidad con política `block` → una venta pasa, la otra recibe `EXISTENCIAS_INSUFICIENTES`.
10. Devolución que excede lo vendido → 422; devoluciones parciales sucesivas suman correctamente.
11. Cierre de caja con pago pendiente → bloqueado (D-12).
12. Caída del proceso entre pasos → los workers retoman desde el estado persistido.

## Criterios de salida del MVP

- Todas las suites de CI en verde, incluidas aislamiento y arquitectura.
- Flujo E2E completo demostrado contra **sandbox real** de la pasarela y ambiente de pruebas fiscal:
  empresa → sucursal → producto con precio y existencias → caja abierta → venta → pago confirmado →
  movimientos consistentes → documento con estado verificable → cierre de caja.
- Escenarios 1–12 automatizados y pasando.
- Reconciliación de inventario y conciliación de pagos sin diferencias en el set de pruebas.
