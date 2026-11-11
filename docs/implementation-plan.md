# Plan de implementación — MVP v1.1

Empieza solo cuando se aprueben este diseño y las decisiones bloqueantes de cada fase
([`decisions.md`](decisions.md)). Cada fase entrega API, pruebas, documentación y
criterios de aceptación; se detiene si fallan pruebas críticas.

| # | Fase | Entregables | Reutiliza de v1 | Decisiones | Criterio de aceptación |
|---|---|---|---|---|---|
| 1 ✅ (pruebas locales; CI sin ejecutar en GitHub) | Fundación técnica y seguridad | .NET 10, PostgreSQL (I-18), módulos por carpeta, filtro por empresa, `Idempotency-Key`, outbox, inbound events, auditoría, OpenAPI, CI con PostgreSQL real, pruebas de arquitectura | Capas, Result, MediatR, auditoría automática, JWT, CI | D-01, D-02, D-13, D-14, D-16 | CI verde con migración inicial en PostgreSQL; prueba de aislamiento de ejemplo |
| 2 ✅ backend (pruebas locales) | Empresas, sucursales y usuarios | Alta de empresas (plataforma), pertenencias, roles por sucursal, login backoffice + selección de empresa, refresh tokens, bloqueo de PIN | Backoffice: Empresa, Sucursales, Usuarios, vinculación de dispositivos | D-08, D-09 | Dos empresas demo sin acceso cruzado (pruebas por endpoint) |
| 3 ✅ backend (compila; pruebas sin ejecutar) | Catálogo y precios | Productos, precio base y por sucursal con vigencia, disponibilidad por sucursal, impuestos con códigos DIAN | Catálogo, categorías, impuestos, `sale-math` | D-07 | Precio vigente correcto por sucursal y fecha (pruebas) |
| 4 ✅ backend (compila; pruebas sin ejecutar) | Inventario básico | Ubicaciones, movimientos, saldos, inicial, ajustes autorizados, reconciliación, pantallas de existencias y movimientos | — | D-06 | Saldo = Σ movimientos; idempotencia; concurrencia |
| 5 ✅ backend (compila; pruebas sin ejecutar) | Cajas y turnos | Sesiones de caja, movimientos, cierre con esperado (incluye reembolsos), bloqueo por pendientes | Turnos, caja, cierre (fix del cambio) | D-12 | Esperado correcto en escenarios mixtos |
| 6 ✅ backend con Bold (compila; **falta la prueba en el sandbox de Bold**, criterio de aceptación) | Pasarela de pagos | Adaptador Wompi (o el elegido), intentos, webhooks firmados, worker de recuperación, conciliación, credenciales cifradas | Medios de pago | D-03 | Pruebas contra **sandbox real** + escenarios 2–6 |
| 7 ✅ backend con Siigo (compila; **falta un documento aceptado en habilitación**, criterio de aceptación) | Facturación electrónica | Configuración fiscal, numeración, documentos, worker de emisión y consulta, artefactos, notas | — | D-04, D-05, D-15 | Documento aceptado en ambiente de pruebas con CUFE/CUDE verificable; escenarios 7–8 |
| 8 ✅ backend (compila; pruebas sin ejecutar; **falta el E2E en los sandbox de Bold y Siigo**) | Integración completa del POS | Venta como proceso persistente, cobro con estado en vivo, comprobante con estado fiscal; se retira el modo offline | Pantallas del POS | — | E2E venta efectivo y Nequi/QR en sandbox |
| 9 ✅ backend (compila; pruebas sin ejecutar; **nota crédito sin probar en habilitación de Siigo**) | Auditoría, devoluciones y consultas | Devoluciones con reembolso, inventario compensatorio y nota; anulaciones; consultas de pagos/documentos/movimientos | Ventas, auditoría | D-11 | Escenarios 10 y reembolsos |
| 10 ✅ archivos y guías listos (compilan; **nada desplegado ni ejecutado**: E2E, carga y restauración pendientes en staging) | Pruebas integrales y despliegue | E2E completo, carga básica, bundle de migraciones, IaC/compose, runbooks de recuperación | Docker, CI | D-10 | Criterios de salida de `testing-strategy.md` |

## Dependencias nuevas a aprobar (D-16)

| Paquete | Para qué |
|---|---|
| Npgsql.EntityFrameworkCore.PostgreSQL | Proveedor PostgreSQL (igual que la v1, I-18) |
| Testcontainers.PostgreSql | PostgreSQL real en pruebas de integración |
| NetArchTest.Rules | Pruebas de límites entre módulos |
| Microsoft.Extensions.Http.Resilience | Reintentos/timeouts hacia pasarela y proveedor fiscal |
| Azure.Storage.Blobs (o equivalente) | Conservación de XML/PDF fiscales |

## Riesgos principales

| Riesgo | Mitigación |
|---|---|
| Normativa POS electrónico ambigua (UVT, anexo, calendario) | Validar con el contador antes de la fase 7 (D-05) |
| Proveedor fiscal sin API adecuada para SaaS multi-NIT | Evaluar dos en sandbox antes de comprometer |
| Pasarela sin reembolso por API para Nequi/QR | Reembolso manual registrado y aprobado |
| Datáfono no integrable | Medio externo con referencia y conciliación manual |
| Habilitación DIAN por cada empresa cliente alarga el onboarding | Incluir el estado de habilitación en el alta de empresas |
