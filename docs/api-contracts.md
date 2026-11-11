# API contracts — Tahona.BakeryPOS MVP v1.1

Status: **proposal for review**. It will be published as OpenAPI 3.1 generated from the code
(`/openapi/v1.json`) once approved.

> Everything technical in the contract is English: route paths, query parameters, request/response fields, error codes, error body keys,
> permission codes and stored values. Only the human-readable `message` of an error and the seeded display names are Spanish, because users read them.
> The contract was renamed in place (still `/api/v1`) because the product was not in production yet.

## Conventions

| Topic | Rule |
|---|---|
| Base | `/api/v1`, JSON, HTTPS mandatory |
| Company | Comes from the token; never travels as a free parameter. An id of another company answers 404 |
| Idempotency | `Idempotency-Key` header (UUID) mandatory on POSTs that create sales, payments, returns, adjustments and cash movements. Same key + same body → same response; same key + another body → 409 `IDEMPOTENCY_CONFLICT` |
| Concurrency | `ETag`/`If-Match` on master-data PUTs → 412 if it changed, 428 if missing *(I-13)* |
| Money | Decimal number with 2 decimals |
| Dates | ISO 8601 UTC |
| Pagination | `?page=&pageSize=` (max 200) → `{ items, page, pageSize, totalCount }` |
| Errors | `{ code, message, details?: { field: [messages] }, correlationId }` |

## Standard errors

| HTTP | Code | When |
|---|---|---|
| 400 | `VALIDATION` | Invalid data |
| 401 | `UNAUTHENTICATED` | Missing or expired token |
| 403 | `PERMISSION_DENIED`, `DEVICE_NOT_PAIRED` | Insufficient permission or scope |
| 404 | `NOT_FOUND` | Does not exist in the token's company |
| 409 | `IDEMPOTENCY_CONFLICT`, `CASH_SESSION_ALREADY_OPEN`, `CASH_SESSION_CLOSED`, `INVALID_STATE` | State conflicts |
| 412 | `VERSION_CONFLICT` | `If-Match` does not match |
| 422 | `INSUFFICIENT_STOCK` (product with `block` policy and no authorizer), `PAYMENTS_DO_NOT_COVER_TOTAL`, `VALIDATION`, `PAYMENT_PENDING` | Business rules |
| 429 | `RATE_LIMIT_EXCEEDED` | Rate limit (login, pairing, webhooks) |
| 502/503 | `PROVIDER_UNAVAILABLE` | Gateway or tax provider down (the operation stays pending, not failed) |

## Endpoints

### Authentication and platform
| Method and route | Permission | Description |
|---|---|---|
| `POST /auth/login` | anonymous | Email + password → list of companies, or a token if there is one |
| `POST /auth/select-company` | session | Issues a token for one company |
| `POST /auth/refresh` | refresh token | Rotates the refresh token |
| `POST /auth/login-pin` | device token (`X-Device-Token`) | `{ userId, pin }` → cashier token; 5 failures → 423 for 5 min |
| `POST /auth/change-password` | session | Mandatory after the one-time password; closes the other sessions *(I-15)* |
| `POST /auth/logout` | session | Revokes the refresh token sent *(I-15)* |
| `POST /device/pair` | anonymous + code | `{ code, deviceName }` → device token (delivered once) |
| `GET /device/users` | device token | Users with a PIN and a current assignment at the register's branch *(I-15)* |
| `POST /platform/companies` | `platform.admin` | Company onboarding + owner |
| `GET /platform/companies` | `platform.admin` | List with status (no sales data) |

### Companies, branches, users
| Method and route | Permission |
|---|---|
| `GET/PUT /company` | `organization.manage` |
| `GET/POST /branches`, `PUT /branches/{id}`, `POST /branches/{id}/deactivate` | `organization.manage` |
| `GET/POST /cash-registers`, `POST /cash-registers/{id}/pairing-codes`, `GET /devices`, `POST /devices/{id}/deactivate` | `organization.manage` |
| `GET/POST /users`, `GET/PUT /users/{id}`, `POST /users/{id}/assignments`, `DELETE /users/{id}/assignments/{assignmentId}` | `organization.manage` |
| `GET /roles` | `organization.manage` |

### Catalog and prices
| Method and route | Permission |
|---|---|
| `GET /products?branchId=&search=&categoryId=&includeInactive=&page=&pageSize=` | `products.view` — paginated; current price (`price`: the branch's or, if none, the base one), `basePrice`, `branchPrice`, `priceIncludesTax` and the branch's availability (`isAvailable`) |
| `GET /products/{id}?branchId=` | `products.view` — with `ETag` |
| `POST /products` `{ sku, name, categoryId, unitId, taxId, barcode?, trackStock, basePrice? }` | `catalog.manage` + Idempotency-Key; `basePrice` opens the first current base price right away *(I-20)* |
| `PUT /products/{id}` (no SKU), `POST /products/{id}/deactivate`, `POST /products/{id}/activate` | `catalog.manage` (PUT with If-Match) |
| `POST /products/{id}/prices` `{ branchId?, amount, includesTax?=true, validFrom? }` | `catalog.manage` (branch: also `prices.branch` + scope) + Idempotency-Key. Without `validFrom` = from now; past dates or dates earlier than the last validity are not accepted |
| `GET /products/{id}/prices` | `products.view` — history (base and per branch) |
| `PUT /branches/{id}/products/{productId}/availability` `{ isAvailable }` | `prices.branch` + scope |
| `GET/POST /categories`, `PUT /categories/{id}` (name, order, status) | `catalog.manage` (POST with Idempotency-Key, PUT with If-Match) |
| `GET/POST /taxes`, `PUT /taxes/{id}` (name and status; the rate does not change) | `catalog.manage` |
| `GET /units` | `catalog.manage` — global catalog |


### Inventory
| Method and route | Permission |
|---|---|
| `GET /stock?branchId=&locationId=&search=&page=&pageSize=` | `inventory.view` — each product with `trackStock` per active location (0 and `hasMovements=false` if it has no movements); paginated by product |
| `GET /inventory-movements?branchId=&locationId=&productId=&type=&from=&to=&page=&pageSize=` | `inventory.view` — `type`: initial, sale, return, void_reversal, adjustment_in, adjustment_out |
| `GET /locations?branchId=`, `POST /locations` `{ branchId, code, name }` | `inventory.view` / `inventory.adjust` + Idempotency-Key |
| `GET /inventory/reasons` | `inventory.view` |
| `POST /inventory/initial` `{ locationId, lines: [{ productId, quantity }], note? }` | `inventory.adjust` + Idempotency-Key; once per product and location (409 `INITIAL_STOCK_ALREADY_RECORDED`) |
| `POST /inventory/adjustments` `{ locationId, productId, quantity(±), reasonId, note? }` | `inventory.adjust` + Idempotency-Key; whoever records it is the authorizer; the reason limits the sign |
| `POST /inventory/counts` `{ locationId, lines: [{ productId, counted }], note? }` | `inventory.adjust` + Idempotency-Key — adjusts to the counted amount with the "Conteo físico" reason (`PHYSICAL_COUNT`) and returns the differences |
| `GET /inventory/reconciliation?branchId=` | `inventory.view` — balances that do not match Σ movements (should come back empty) |
| Products: `negativeStockPolicy` (`allow`/`block`) in `POST/PUT /products` and in their responses | D-06 |

### Cash
| Method and route | Permission |
|---|---|
| `POST /cash-registers/{id}/sessions` `{ openingAmount }` | `shifts.open` + Idempotency-Key — 409 `CASH_SESSION_ALREADY_OPEN` |
| `GET /cash-registers/{id}/open-session` | `shifts.open` — 404 if there is none (cashier handover) |
| `GET /cash-reasons` | `cash.movements` |
| `POST /cash-sessions/{id}/movements` `{ kind: income\|expense, amount, reasonId, note? }` | `cash.movements` (+ `cash.withdrawal` for withdrawals: the supervisor sends their token) + Idempotency-Key — 422 `WITHDRAWAL_EXCEEDS_BALANCE` |
| `GET /cash-sessions/{id}/movements` | `cash.movements` |
| `GET /cash-sessions/{id}/summary` | `shifts.close` — itemized expected amount and `pendingPayments` |
| `POST /cash-sessions/{id}/close` `{ countedAmount, notes? }` | `shifts.close` + Idempotency-Key — 422 `PAYMENT_PENDING` (D-12), 409 `CASH_SESSION_CLOSED` |
| `GET /cash-sessions?from=&to=&branchId=&status=open\|closed` | `reports.view` — by opening date |

### Sales
| Method and route | Permission | Response |
|---|---|---|
| `POST /sales` `{ cashRegisterId, customerIdentification?, lines: [{ productId, quantity, discountAmount?, discountReason? }] }` | `sales.create` + Idempotency-Key | `SaleDto` (`id`, `status: 'draft'`, `lines`, totals…) — prices and taxes are computed by the server |
| `PUT /sales/{id}/customer` `{ customerIdentification? }` | `sales.create` | `SaleDto`; null = final consumer |
| `POST /sales/{id}/payments` `{ paymentMethodId, amount, amountTendered?, reference? }` | `sales.create` + Idempotency-Key | `SaleDto` with its payments and their status |
| `GET /sales/{id}` | `sales.view` | Sale with payment, inventory and fiscal states |
| `POST /sales/{id}/authorize-stock` | `inventory.adjust` (the authorizer's token) | D-06; only before confirming |
| `POST /sales/{id}/cancel` | `sales.create` | Only `draft`/`pending_payment`; cancels pending attempts |
| `POST /sales/{id}/void` `{ reason }` | `sales.void` (the authorizer's token) | D-11: only while the sale's shift is open (otherwise 422 `VOID_NOT_ALLOWED`) |
| `GET /sales?from=&to=&branchId=&cashSessionId=&status=&page=&pageSize=` | `sales.view` | Paginated |

Implemented in phases 8 (I-25) and 9 (I-26). The POS queries `GET /sales/{id}` while a gateway payment is pending; there is no separate `/status` endpoint. Specific errors: `INSUFFICIENT_STOCK` (422, with the missing products), `PAYMENT_IN_PROGRESS` (409), `INVALID_STATE` (409).

### Returns
| Method and route | Permission |
|---|---|
| `POST /sales/{id}/returns` `{ cashRegisterId, reason, lines:[{saleLineId, quantity, restock}] }` | `returns.approve` (the authorizer's token) + Idempotency-Key |
| `GET /returns?from=&to=&branchId=&page=&pageSize=` | `reports.view` |
| `GET /reports/sales?from=yyyy-MM-dd&to=yyyy-MM-dd&branchId=` | `reports.view` |

Phase 9 (D-11, I-26): a return is authorized and completed on the spot (there is no `/aprobar`); the refund is always in
cash from the indicated register. `GET /sales?number=` looks up a sale by its consecutive number.

### Payments
| Method and route | Permission |
|---|---|
| `GET /payment-methods` | session — used by the POS to charge |
| `POST /payment-methods` `{ code, name, kind, gatewayMethod?, requiresReference, displayOrder }`, `PUT /payment-methods/{id}` | `organization.manage` (POST with Idempotency-Key, PUT with If-Match); a Bold method is only activated with the integration active |
| `GET /payments/{id}` | session — the POS queries it while waiting for the card terminal |
| `GET /payments?from=&to=&status=&cashSessionId=` | `reports.view` |
| `POST /payments/{id}/resolve` `{ resolution: approved\|declined\|cancelled, approvalNumber?, note }` | `payments.resolve` — audited manual resolution when the webhook did not arrive *(I-23)* |
| `POST /webhooks/payments/bold` | anonymous; `x-bold-signature` signature mandatory (401 if invalid); answers 200 immediately |
| `GET/PUT /integrations/gateway` `{ environment, identityKey?, secretKey?, sellerEmail, activate }` | `organization.manage` — never returns the keys, only whether they are configured |
| `GET /integrations/gateway/terminals`, `GET /integrations/gateway/methods` | `organization.manage` — live query to Bold (503 `PROVIDER_UNAVAILABLE` if it does not respond) |
| `GET /integrations/gateway/cash-registers`, `PUT /cash-registers/{id}/terminal` `{ terminalModel, terminalSerial }` | `organization.manage` — the Bold card terminal of each register |
| `POST /integrations/gateway/test` `{ cashRegisterId, paymentMethodId, amount }` | `organization.manage` + Idempotency-Key — test charge without a sale on the card terminal of an open register |
| `POST /payments/attempts/{id}/consultar`, `GET /payments/conciliacion` | *Do not apply with Bold:* its card-terminal API has no status query (payment-integration.md §11) |

### Electronic invoicing
| Method and route | Permission |
|---|---|
| `GET/PUT /invoicing/settings` `{ username?, accessKey?, invoiceDocumentTypeId, creditNoteDocumentTypeId, sellerId, productAccountGroupId, consumerFinalIdentification, retentionYears, activate }` | `fiscal.manage` — never returns the Siigo credentials *(I-24)* |
| `GET /invoicing/siigo/catalogs` | `fiscal.manage` — Siigo's FV/NC document types, sellers, taxes and payment methods (503 if it does not respond) |
| `GET/PUT /invoicing/mappings` `{ mappings: [{ kind: tax\|payment_method, localId, externalId }] }` | `fiscal.manage` |
| `POST /invoicing/test` `{ productId, quantity, paymentMethodId, confirmTestEnvironment: true }` | `fiscal.manage` + Idempotency-Key — test invoice to a final consumer (it goes to the DIAN in the company's Siigo environment) |
| `GET /fiscal-documents?status=&from=&to=` | `sales.view` |
| `GET /fiscal-documents/{id}/attempts` | `fiscal.manage` — each creation, query or submission to the DIAN |
| `POST /fiscal-documents/{id}/retry` | `fiscal.manage` — rejected, errored or unconfirmed; the submission repeats the same Idempotency-Key |
| `GET /fiscal-documents/{id}/pdf` | `sales.view` — from Siigo. **No `/xml`**: Siigo's API does not expose it (it stays in Siigo Nube) |
| Numberings | No endpoints of their own: Siigo keeps them; the screen shows its document types |

Electronic invoicing is optional per company (D-17): with it off, no fiscal document is created and the sale keeps `fiscal_status = not_required`.

### Audit
| Method and route | Permission |
|---|---|
| `GET /audit?entity=&entityId=&from=&to=&page=&pageSize=` | `organization.manage` — `entity` is the table with its schema (`cat.products`); newest first; each record's `action` is `create`, `update` or `delete` |
| `GET /audit/entities` | `organization.manage` — tables with records, for filtering |

## Permissions added relative to the previous system

`platform.admin`, `prices.branch`, `inventory.view`, `inventory.adjust`, `returns.create`,
`returns.approve`, `fiscal.manage`, `payments.resolve` (phase 6), `sales.discount` (phase 8). The previous system's permissions are kept.
