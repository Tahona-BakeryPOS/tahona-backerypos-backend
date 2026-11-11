# Modelo de datos — Tahona.BakeryPOS MVP v1.1

Estado: **propuesta para revisión** (PostgreSQL, I-18).

## Convenciones

| Tema | Regla |
|---|---|
| Nombres | `snake_case`, tablas en plural, esquema por módulo (`org`, `sec`, `cat`, `inv`, `cash`, `sales`, `pay`, `fis`, `aud`, `int`) |
| Clave primaria | `id uuid` (UUID v7 generado por la aplicación) |
| Multiempresa | `company_id` NOT NULL en toda tabla de negocio, primera columna de los índices; FK compuestas `(company_id, x_id)` en relaciones críticas |
| Dinero | `decimal(18,2)`; tarifas `decimal(7,4)`; cantidades `decimal(18,4)` |
| Fechas | `timestamptz(3)` en UTC |
| Concurrencia | `row_version bytea` en tablas editables (la renueva la aplicación en cada cambio) |
| Auditoría | `created_at/by`, `modified_at/by`; borrado lógico (`deleted_at/by`) solo en datos maestros |
| Estados | `varchar` + `CHECK`; un estado por concepto (venta ≠ pago ≠ inventario ≠ documento) |
| Idempotencia | Columnas `idempotency_key`/`external_id` con índice **único** filtrado |

## Diagrama de relaciones

El diagrama actualizado y completo (44 tablas, 10 esquemas, con columnas, claves y relaciones) está en
[`diagrama-base-de-datos.md`](diagrama-base-de-datos.md) y se ve directamente en GitHub. Se generó a partir del modelo
implementado; las tablas de más abajo describen el diseño y sus reglas.

## Tablas

### org — Empresas
| Tabla | Columnas clave | Reglas |
|---|---|---|
| `companies` | id, legal_name, trade_name, nit, nit_dv, tax_regime, tax_responsibilities (json), email, phone, address, city_code (DANE), time_zone, currency='COP', status (`active`,`suspended`) | UQ(nit); CHECK dígito de verificación en aplicación |
| `branches` | id, company_id, code, name, address, city_code, phone, is_active | UQ(company_id, code) |

### sec — Seguridad
| Tabla | Columnas clave | Reglas |
|---|---|---|
| `users` | id, email (nullable), full_name, password_hash, is_platform_admin, failed_logins, locked_until, is_active | UQ(email) filtrado WHERE email NOT NULL |
| `memberships` | id, company_id, user_id, pin_hash, is_active | UQ(company_id, user_id). El PIN es por empresa |
| `roles` | id, company_id, code, name, is_system | UQ(company_id, code) |
| `permissions` (global) | id, code, name, module | UQ(code); sembrado por migración |
| `role_permissions` | role_id, permission_id | PK compuesta |
| `role_assignments` | id, company_id, membership_id, role_id, branch_id (NULL = todas), valid_from, valid_to | UQ(membership_id, role_id, branch_id) |
| `devices` | id, company_id, cash_register_id, name, is_active, last_seen_at | — |
| `device_pairing_codes` | id, company_id, cash_register_id, code, expires_at, consumed_at, device_id | UQ(code) filtrado consumed_at IS NULL |
| `refresh_tokens` | id, user_id, company_id, token_hash, expires_at, revoked_at, replaced_by | UQ(token_hash) |

### cat — Catálogo y precios
| Tabla | Columnas clave | Reglas |
|---|---|---|
| `categories` | id, company_id, name, display_order, is_active | UQ(company_id, name) |
| `units` (global) | id, code (`UND`,`KG`,`G`,`LB`…), name, kind (`count`,`mass`,`volume`), dian_code | UQ(code) |
| `taxes` | id, company_id, code, name, kind (`iva`,`inc`,`none`), rate, dian_code, is_active | UQ(company_id, code); CHECK rate ≥ 0 |
| `products` | id, company_id, sku, name, category_id, unit_id, tax_id, barcode, track_stock, is_active | UQ(company_id, sku); UQ(company_id, barcode) filtrado |
| `product_prices` | id, company_id, product_id, branch_id (NULL = precio base), amount, includes_tax, valid_from, valid_to | CHECK amount ≥ 0; sin solapamiento por (product_id, branch_id) validado en aplicación + índice |
| `branch_products` | company_id, branch_id, product_id, is_available | PK(branch_id, product_id) |

Precio vigente en una sucursal = precio de la sucursal vigente; si no existe, precio base vigente.

### inv — Inventario
| Tabla | Columnas clave | Reglas |
|---|---|---|
| `stock_locations` | id, company_id, branch_id, code, name, is_default | UQ(branch_id, code); una `is_default` por sucursal |
| `stock_movements` | id, company_id, location_id, product_id, quantity (±), kind (`initial`,`sale`,`return`,`adjustment_in`,`adjustment_out`,`void_reversal`), source_type, source_id, source_line_id, reason_id, authorized_by, occurred_at | **Solo inserción**; UQ(company_id, kind, source_type, source_id, source_line_id) = idempotencia; CHECK quantity ≠ 0 |
| `stock_balances` | company_id, location_id, product_id, on_hand, row_version, last_movement_id | PK(location_id, product_id). Solo lo actualiza el módulo en la misma TX del movimiento |
| `adjustment_reasons` | id, company_id, code, name, direction | — |

### cash — Caja
| Tabla | Columnas clave | Reglas |
|---|---|---|
| `cash_registers` | id, company_id, branch_id, code, name, is_active | UQ(branch_id, code) |
| `cash_sessions` | id, company_id, cash_register_id, opened_by, opened_at, opening_amount, status (`open`,`closing`,`closed`), closed_by, closed_at, expected_amount, counted_amount, difference | UQ(cash_register_id) filtrado WHERE status <> 'closed' |
| `cash_movements` | id, company_id, cash_session_id, kind (`opening`,`sale_cash`,`change_given`,`refund_cash`,`income`,`expense`), amount, reason_id, source_type, source_id, authorized_by, occurred_at | Solo inserción; UQ(kind, source_type, source_id) para los derivados de ventas |
| `cash_reasons` | id, company_id, kind, name | — |

### sales — Ventas y devoluciones
| Tabla | Columnas clave | Reglas |
|---|---|---|
| `sales` | id, company_id, branch_id, cash_session_id, cashier_id, customer_party_id, idempotency_key, number (asignado al confirmar), status, payment_status, inventory_status, fiscal_status, subtotal, discount_total, tax_total, total, confirmed_at, voided_at, void_reason, voided_by | UQ(company_id, idempotency_key); `status` CHECK (`draft`,`pending_payment`,`confirmed`,`cancelled`,`voided`); `payment_status` (`unpaid`,`partially_paid`,`paid`,`refunded`,`partially_refunded`); `inventory_status` (`not_applied`,`applied`,`reversed`); `fiscal_status` (`not_required`,`pending`,`accepted`,`rejected`,`in_review`) |
| `sale_lines` | id, company_id, sale_id, line_no, product_id, sku, description, unit_code, quantity, unit_price, price_includes_tax, discount_amount, discount_reason, tax_code, tax_rate, taxable_base, tax_amount, line_total | UQ(sale_id, line_no); copia histórica de nombre/precio/impuesto |
| `parties` | id, company_id, kind (`person`,`company`), id_type, id_number, dv, name, email, phone, address, city_code, is_final_consumer | UQ(company_id, id_type, id_number); un consumidor final por empresa |
| `returns` | id, company_id, sale_id, branch_id, cash_session_id, reason, approved_by, total, fiscal_document_id, occurred_at | *Fase 9 (I-26)*: se completa al registrarse; sin estados intermedios ni `requested_by` |
| `return_lines` | id, return_id, sale_line_id, product_id, quantity, amount, restock | UQ(return_id, sale_line_id); Σ devuelto ≤ vendido (en aplicación) |

### pay — Pagos
| Tabla | Columnas clave | Reglas |
|---|---|---|
| `payment_methods` | id, company_id, code, name, kind (`cash`,`gateway`,`external_card`,`transfer_manual`), gateway_method (`NEQUI`,`BANCOLOMBIA_QR`, …), requires_reference, is_active | UQ(company_id, code) |
| `payments` | id, company_id, sale_id, payment_method_id, amount, amount_tendered, change_amount, status (`pending`,`approved`,`declined`,`cancelled`,`refunded`,`partially_refunded`), approved_at | Pago lógico de la venta |
| `payment_attempts` | id, company_id, payment_id, provider, reference (= id), external_id, status (`created`,`pending`,`approved`,`declined`,`voided`,`error`,`unknown`,`expired`), raw_status, amount_in_cents, last_checked_at, check_count, error_code | UQ(provider, reference); UQ(provider, external_id) filtrado |
| `refunds` | id, company_id, payment_id, sale_id, source_type (`void`,`return`), source_id, amount, method (`cash`,`gateway`,`manual`), status (`pending`,`completed`,`failed`) | UQ(payment_id, source_type, source_id). *Fase 9*: solo efectivo (D-11); `payments.refunded_amount` acumula |
| `register_terminals` | id, company_id, cash_register_id, provider, terminal_model, terminal_serial | UQ(cash_register_id, provider). Datáfono Bold de cada caja *(I-23)* |
| `gateway_accounts` | id, company_id, provider, environment (`sandbox`,`production`), public_key, encrypted_private_key, encrypted_events_secret, is_active | UQ(company_id, provider, environment) |

### fis — Facturación electrónica
| Tabla | Columnas clave | Reglas |
|---|---|---|
| `fiscal_settings` | id, company_id, provider (`siigo`), encrypted_username, encrypted_access_key, invoice_document_type_id, credit_note_document_type_id, seller_id, product_account_group_id, consumer_final_identification, retention_years, is_active | UQ(company_id). *Implementado así en la fase 7 (I-24)* |
| `fiscal_mappings` | id, company_id, kind (`tax`,`payment_method`,`product`), local_id, external_id | UQ(company_id, kind, local_id). Emparejamiento con Siigo *(I-24)* |
| `numbering_ranges` *(no se usa con Siigo: la numeración la lleva el proveedor)* | id, company_id, branch_id, document_kind (`pos_equivalent`,`invoice`,`credit_note`,`adjustment_note`), prefix, from_number, to_number, current_number, resolution_number, valid_from, valid_to, technical_key (encriptada si aplica), is_active | UQ(company_id, document_kind, prefix); asignación con bloqueo de fila |
| `fiscal_documents` | id, company_id, sale_id, return_id, kind, prefix, number, status (`pending`,`submitting`,`accepted`,`rejected`,`unknown`,`temporary_error`), provider_document_id, cufe_cude, dian_status, issued_at, accepted_at, xml_storage_key, pdf_storage_key, qr_data, rejection_reasons (json) | UQ(company_id, kind, prefix, number) |
| `fiscal_submission_attempts` | id, fiscal_document_id, attempt_no, kind (`submit`,`status_query`), request_hash, http_status, provider_status, response_storage_key, error_class (`none`,`transient`,`rejected`,`unknown`), started_at, finished_at | UQ(fiscal_document_id, attempt_no) |

### aud / int — Transversales
| Tabla | Columnas clave | Reglas |
|---|---|---|
| `audit_logs` | id, company_id, user_id, device_id, entity, entity_id, action, old_values (json), new_values (json), correlation_id, ip, occurred_at | Solo inserción; índice (company_id, entity, entity_id, occurred_at) |
| `outbox_messages` | id, company_id, type, payload (json), status (`pending`,`processing`,`done`,`failed`), attempts, next_attempt_at, locked_until, last_error | Índice (status, next_attempt_at) |
| `inbound_events` | id, provider, external_event_id, received_at, signature_valid, payload (json), status (`received`,`processed`,`ignored`,`failed`), processed_at | UQ(provider, external_event_id) |
| `idempotency_requests` | company_id, key, endpoint, request_hash, response_code, response_body, created_at | PK(company_id, key, endpoint); mismo key + otro hash = 409 |

## Reglas de integridad clave

1. Una venta `confirmed` tiene `payment_status = paid`, `inventory_status = applied` y Σ pagos aprobados = total (+ cambio en efectivo).
2. Un intento de pago solo avanza: `created → pending → {approved, declined, voided, error, expired}`; `unknown` solo sale por consulta al proveedor.
3. El saldo de inventario = Σ movimientos (verificable con la reconciliación); nunca se actualiza sin movimiento.
4. Un documento fiscal `unknown` no se reenvía sin una consulta de estado registrada.
5. Toda FK de negocio apunta a filas de la misma `company_id` (validado en aplicación y con FK compuestas en ventas, pagos, inventario y documentos).
6. Las anulaciones y devoluciones nunca borran filas: crean movimientos/documentos compensatorios.

## Migración desde la v1

La v1 usa PostgreSQL con otro esquema. Propuesta: **base nueva en PostgreSQL** (mismo motor, esquema nuevo) con migración inicial
limpia (no hay datos de producción). Un script opcional puede importar catálogo y usuarios de la base
demo si se pide. *(D-01)*
