# API contracts — Tahona.BakeryPOS MVP v1.1

Status: **proposal for review**. It will be published as OpenAPI 3.1 generated from the code
(`/openapi/v1.json`) once approved.

> Route paths, query parameters, error codes and the error body keys are Spanish in the current API (`/ventas`, `codigo`,
> `NO_ENCONTRADO`...). They are kept so existing clients keep working; a rename would ship as a new API version.
> Permission codes and the values stored in the database are English.

## Conventions

| Topic | Rule |
|---|---|
| Base | `/api/v1`, JSON, HTTPS mandatory |
| Company | Comes from the token; never travels as a free parameter. An id of another company answers 404 |
| Idempotency | `Idempotency-Key` header (UUID) mandatory on POSTs that create sales, payments, returns, adjustments and cash movements. Same key + same body → same response; same key + another body → 409 `IDEMPOTENCIA_CONFLICTO` |
| Concurrency | `ETag`/`If-Match` on master-data PUTs → 412 if it changed, 428 if missing *(I-13)* |
| Money | Decimal number with 2 decimals |
| Dates | ISO 8601 UTC |
| Pagination | `?page=&pageSize=` (max 200) → `{ items, page, pageSize, totalCount }` |
| Errors | `{ codigo, mensaje, detalles?: { field: [messages] }, correlationId }` |

## Standard errors

| HTTP | Code | When |
|---|---|---|
| 400 | `VALIDACION` | Invalid data |
| 401 | `NO_AUTENTICADO` | Missing or expired token |
| 403 | `SIN_PERMISO`, `DISPOSITIVO_NO_VINCULADO` | Insufficient permission or scope |
| 404 | `NO_ENCONTRADO` | Does not exist in the token's company |
| 409 | `IDEMPOTENCIA_CONFLICTO`, `CAJA_YA_ABIERTA`, `CAJA_CERRADA`, `ESTADO_INVALIDO` | State conflicts |
| 412 | `CONFLICTO_VERSION` | `If-Match` does not match |
| 422 | `EXISTENCIAS_INSUFICIENTES` (product with `block` policy and no authorizer), `PAGOS_NO_CUBREN_TOTAL`, `DEVOLUCION_EXCEDE_VENTA`, `PAGO_PENDIENTE` | Business rules |
| 429 | `LIMITE_EXCEDIDO` | Rate limit (login, pairing, webhooks) |
| 502/503 | `PROVEEDOR_NO_DISPONIBLE` | Gateway or tax provider down (the operation stays pending, not failed) |

## Endpoints

### Authentication and platform
| Method and route | Permission | Description |
|---|---|---|
| `POST /auth/login` | anonymous | Email + password → list of companies, or a token if there is one |
| `POST /auth/select-company` | session | Issues a token for one company |
| `POST /auth/refresh` | refresh token | Rotates the refresh token |
| `POST /auth/login-pin` | device token (`X-Device-Token`) | `{ userId, pin }` → cashier token; 5 failures → 423 for 5 min |
| `POST /auth/cambiar-contrasena` | session | Mandatory after the one-time password; closes the other sessions *(I-15)* |
| `POST /auth/logout` | session | Revokes the refresh token sent *(I-15)* |
| `POST /dispositivo/vincular` | anonymous + code | `{ code, deviceName }` → device token (delivered once) |
| `GET /dispositivo/usuarios` | device token | Users with a PIN and a current assignment at the register's branch *(I-15)* |
| `POST /plataforma/empresas` | `platform.admin` | Company onboarding + owner |
| `GET /plataforma/empresas` | `platform.admin` | List with status (no sales data) |

### Companies, branches, users
| Method and route | Permission |
|---|---|
| `GET/PUT /empresa` | `organization.manage` |
| `GET/POST /sucursales`, `PUT /sucursales/{id}`, `POST /sucursales/{id}/desactivar` | `organization.manage` |
| `GET/POST /cajas`, `POST /cajas/{id}/codigos-vinculacion`, `GET /dispositivos`, `POST /dispositivos/{id}/desactivar` | `organization.manage` |
| `GET/POST /usuarios`, `GET/PUT /usuarios/{id}`, `POST /usuarios/{id}/asignaciones`, `DELETE /usuarios/{id}/asignaciones/{asignacionId}` | `organization.manage` |
| `GET /roles` | `organization.manage` |

### Catalog and prices
| Method and route | Permission |
|---|---|
| `GET /productos?sucursalId=&buscar=&categoriaId=&incluirInactivos=&page=&pageSize=` | `products.view` — paginated; current price (`price`: the branch's or, if none, the base one), `basePrice`, `branchPrice`, `priceIncludesTax` and the branch's availability (`isAvailable`) |
| `GET /productos/{id}?sucursalId=` | `products.view` — with `ETag` |
| `POST /productos` `{ sku, name, categoryId, unitId, taxId, barcode?, trackStock, basePrice? }` | `catalog.manage` + Idempotency-Key; `basePrice` opens the first current base price right away *(I-20)* |
| `PUT /productos/{id}` (no SKU), `POST /productos/{id}/desactivar`, `POST /productos/{id}/activar` | `catalog.manage` (PUT with If-Match) |
| `POST /productos/{id}/precios` `{ branchId?, amount, includesTax?=true, validFrom? }` | `catalog.manage` (branch: also `prices.branch` + scope) + Idempotency-Key. Without `validFrom` = from now; past dates or dates earlier than the last validity are not accepted |
| `GET /productos/{id}/precios` | `products.view` — history (base and per branch) |
| `PUT /sucursales/{id}/productos/{productoId}/disponibilidad` `{ isAvailable }` | `prices.branch` + scope |
| `GET/POST /categorias`, `PUT /categorias/{id}` (name, order, status) | `catalog.manage` (POST with Idempotency-Key, PUT with If-Match) |
| `GET/POST /impuestos`, `PUT /impuestos/{id}` (name and status; the rate does not change) | `catalog.manage` |
| `GET /unidades` | `catalog.manage` — global catalog |


### Inventory
| Method and route | Permission |
|---|---|
| `GET /existencias?sucursalId=&ubicacionId=&buscar=&page=&pageSize=` | `inventory.view` — each product with `trackStock` per active location (0 and `hasMovements=false` if it has no movements); paginated by product |
| `GET /movimientos-inventario?sucursalId=&ubicacionId=&productoId=&tipo=&desde=&hasta=&page=&pageSize=` | `inventory.view` — `tipo`: initial, sale, return, void_reversal, adjustment_in, adjustment_out |
| `GET /ubicaciones?sucursalId=`, `POST /ubicaciones` `{ branchId, code, name }` | `inventory.view` / `inventory.adjust` + Idempotency-Key |
| `GET /inventario/motivos` | `inventory.view` |
| `POST /inventario/inicial` `{ locationId, lines: [{ productId, quantity }], note? }` | `inventory.adjust` + Idempotency-Key; once per product and location (409 `INVENTARIO_INICIAL_REGISTRADO`) |
| `POST /inventario/ajustes` `{ locationId, productId, quantity(±), reasonId, note? }` | `inventory.adjust` + Idempotency-Key; whoever records it is the authorizer; the reason limits the sign |
| `POST /inventario/conteos` `{ locationId, lines: [{ productId, counted }], note? }` | `inventory.adjust` + Idempotency-Key — adjusts to the counted amount with the "Conteo físico" reason (`PHYSICAL_COUNT`) and returns the differences |
| `GET /inventario/reconciliacion?sucursalId=` | `inventory.view` — balances that do not match Σ movements (should come back empty) |
| Products: `negativeStockPolicy` (`allow`/`block`) in `POST/PUT /productos` and in their responses | D-06 |

### Cash
| Method and route | Permission |
|---|---|
| `POST /cajas/{id}/sesiones` `{ openingAmount }` | `shifts.open` + Idempotency-Key — 409 `CAJA_YA_ABIERTA` |
| `GET /cajas/{id}/sesion-abierta` | `shifts.open` — 404 if there is none (cashier handover) |
| `GET /motivos-caja` | `cash.movements` |
| `POST /sesiones-caja/{id}/movimientos` `{ kind: income\|expense, amount, reasonId, note? }` | `cash.movements` (+ `cash.withdrawal` for withdrawals: the supervisor sends their token) + Idempotency-Key — 422 `RETIRO_EXCEDE_SALDO` |
| `GET /sesiones-caja/{id}/movimientos` | `cash.movements` |
| `GET /sesiones-caja/{id}/resumen` | `shifts.close` — itemized expected amount and `pendingPayments` |
| `POST /sesiones-caja/{id}/cierre` `{ countedAmount, notes? }` | `shifts.close` + Idempotency-Key — 422 `PAGO_PENDIENTE` (D-12), 409 `CAJA_CERRADA` |
| `GET /sesiones-caja?desde=&hasta=&sucursalId=&estado=open\|closed` | `reports.view` — by opening date |

### Sales
| Method and route | Permission | Response |
|---|---|---|
| `POST /ventas` | `sales.create` + Idempotency-Key | `{ id, estado:'draft', lineas, totales }` — prices and taxes are computed by the server |
| `PUT /ventas/{id}/cliente` | `sales.create` | — |
| `POST /ventas/{id}/pagos` `{ medioPagoId, monto, montoRecibido?, telefono?, referencia? }` | `sales.create` + Idempotency-Key | `{ pagoId, intentoId, estado, accion?: { tipo:'qr'\|'push'\|'redirect', datos } }` |
| `GET /ventas/{id}` | `sales.view` | Sale with payment, inventory and fiscal states |
| `POST /ventas/{id}/autorizar-existencias` | `inventory.adjust` (the authorizer's token) | D-06; only before confirming |
| `POST /ventas/{id}/cancelar` | `sales.create` | Only `draft`/`pending_payment`; cancels pending attempts |
| `POST /ventas/{id}/anulacion` `{ reason }` | `sales.void` (the authorizer's token) | D-11: only while the sale's shift is open (otherwise 422 `ANULACION_NO_PERMITIDA`) |
| `GET /ventas?desde=&hasta=&sucursalId=&sesionCajaId=&estado=&page=&pageSize=` | `sales.view` | Paginated |

Implemented in phases 8 (I-25) and 9 (I-26). The POS queries `GET /ventas/{id}` while a gateway payment is pending; there is no separate `/estado` endpoint. Specific errors: `EXISTENCIAS_INSUFICIENTES` (422, with the missing products), `PAGO_EN_CURSO` (409), `ESTADO_INVALIDO` (409).

### Returns
| Method and route | Permission |
|---|---|
| `POST /ventas/{id}/devoluciones` `{ cashRegisterId, reason, lines:[{saleLineId, quantity, restock}] }` | `returns.approve` (the authorizer's token) + Idempotency-Key |
| `GET /devoluciones?desde=&hasta=&sucursalId=&page=&pageSize=` | `reports.view` |
| `GET /informes/ventas?desde=yyyy-MM-dd&hasta=yyyy-MM-dd&sucursalId=` | `reports.view` |

Phase 9 (D-11, I-26): a return is authorized and completed on the spot (there is no `/aprobar`); the refund is always in
cash from the indicated register. `GET /ventas?numero=` looks up a sale by its consecutive number.

### Payments
| Method and route | Permission |
|---|---|
| `GET /medios-pago` | session — used by the POS to charge |
| `POST /medios-pago` `{ code, name, kind, gatewayMethod?, requiresReference, displayOrder }`, `PUT /medios-pago/{id}` | `organization.manage` (POST with Idempotency-Key, PUT with If-Match); a Bold method is only activated with the integration active |
| `GET /pagos/{id}` | session — the POS queries it while waiting for the card terminal |
| `GET /pagos?desde=&hasta=&estado=&sesionCajaId=` | `reports.view` |
| `POST /pagos/{id}/resolver` `{ resolution: approved\|declined\|cancelled, approvalNumber?, note }` | `payments.resolve` — audited manual resolution when the webhook did not arrive *(I-23)* |
| `POST /webhooks/pagos/bold` | anonymous; `x-bold-signature` signature mandatory (401 if invalid); answers 200 immediately |
| `GET/PUT /integraciones/pasarela` `{ environment, identityKey?, secretKey?, sellerEmail, activate }` | `organization.manage` — never returns the keys, only whether they are configured |
| `GET /integraciones/pasarela/datafonos`, `GET /integraciones/pasarela/metodos` | `organization.manage` — live query to Bold (503 `PROVEEDOR_NO_DISPONIBLE` if it does not respond) |
| `GET /integraciones/pasarela/cajas`, `PUT /cajas/{id}/datafono` `{ terminalModel, terminalSerial }` | `organization.manage` — the Bold card terminal of each register |
| `POST /integraciones/pasarela/prueba` `{ cashRegisterId, paymentMethodId, amount }` | `organization.manage` + Idempotency-Key — test charge without a sale on the card terminal of an open register |
| `POST /pagos/intentos/{id}/consultar`, `GET /pagos/conciliacion` | *Do not apply with Bold:* its card-terminal API has no status query (payment-integration.md §11) |

### Electronic invoicing
| Method and route | Permission |
|---|---|
| `GET/PUT /facturacion/configuracion` `{ username?, accessKey?, invoiceDocumentTypeId, creditNoteDocumentTypeId, sellerId, productAccountGroupId, consumerFinalIdentification, retentionYears, activate }` | `fiscal.manage` — never returns the Siigo credentials *(I-24)* |
| `GET /facturacion/siigo/catalogos` | `fiscal.manage` — Siigo's FV/NC document types, sellers, taxes and payment methods (503 if it does not respond) |
| `GET/PUT /facturacion/mapeos` `{ mappings: [{ kind: tax\|payment_method, localId, externalId }] }` | `fiscal.manage` |
| `POST /facturacion/prueba` `{ productId, quantity, paymentMethodId, confirmTestEnvironment: true }` | `fiscal.manage` + Idempotency-Key — test invoice to a final consumer (it goes to the DIAN in the company's Siigo environment) |
| `GET /documentos-fiscales?estado=&desde=&hasta=` | `sales.view` |
| `GET /documentos-fiscales/{id}/intentos` | `fiscal.manage` — each creation, query or submission to the DIAN |
| `POST /documentos-fiscales/{id}/reintentar` | `fiscal.manage` — rejected, errored or unconfirmed; the submission repeats the same Idempotency-Key |
| `GET /documentos-fiscales/{id}/pdf` | `sales.view` — from Siigo. **No `/xml`**: Siigo's API does not expose it (it stays in Siigo Nube) |
| Numberings | No endpoints of their own: Siigo keeps them; the screen shows its document types |

Electronic invoicing is optional per company (D-17): with it off, no fiscal document is created and the sale keeps `fiscal_status = not_required`.

### Audit
| Method and route | Permission |
|---|---|
| `GET /auditoria?entidad=&entidadId=&desde=&hasta=&page=&pageSize=` | `organization.manage` — `entidad` is the table with its schema (`cat.products`); newest first; each record's `action` is `create`, `update` or `delete` |
| `GET /auditoria/entidades` | `organization.manage` — tables with records, for filtering |

## Permissions added relative to the previous system

`platform.admin`, `prices.branch`, `inventory.view`, `inventory.adjust`, `returns.create`,
`returns.approve`, `fiscal.manage`, `payments.resolve` (phase 6), `sales.discount` (phase 8). The previous system's permissions are kept.
