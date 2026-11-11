# Use-case status — Tahona.BakeryPOS

Starting point for any new session: what is done, what is left to verify and what comes next. Updated on
11/11/2026. Detail of each decision in `decisions.md`; contract in `api-contracts.md`;
operations in `runbooks.md`. Screen names appear as they are in the clients' Spanish UI (for example `Plataforma → Empresas`).

**Legend.** ✅ implemented (backend + screen, compiles) · 🔎 implemented, real verification missing ·
⏳ pending · ❌ outside the MVP for now.

> The backend's unit (62), architecture (20) and integration (58, real PostgreSQL) tests pass. Nothing has been tested end to
> end in the browser or against the sandboxes, and the E2E tests have not been run.

## 1. Use cases covered (phases 1 to 10)

### M1. Companies and branches — phase 2
| US | Use case | Status | Where |
|---|---|---|---|
| US-1.1 | Onboard a company with its owner (platform administrator) | ✅ | `Plataforma → Empresas`; `POST /platform/companies` |
| US-1.2 | Edit the company's data | ✅ (logo pending: file storage is missing, I-17) | `Mi negocio → Empresa` |
| US-1.3 | Create, edit and deactivate branches and registers | ✅ | `Mi negocio → Sucursales y cajas` |

### M2. Security — phase 2
| US | Use case | Status | Where |
|---|---|---|---|
| US-2.1 | Users with a role per branch | ✅ | `Configuración → Usuarios, Roles` |
| US-2.2 | A user in several companies (D-08) | ✅ | Backoffice login → choose a company |
| US-2.3 | Isolation between companies | ✅ (isolation tests pass) | Global company filter |
| US-2.4 | Sign in to the register with name + PIN | ✅ | POS → `/login` |
| — | First platform administrator in a new environment | ✅ | `bootstrap-admin` (phase 10) |

### M3. Catalog and prices — phase 3
| US | Use case | Status | Where |
|---|---|---|---|
| US-3.1 | Products with category, unit, tax and barcode | ✅ | `Productos → Catálogo y precios, Categorías, Unidades` |
| US-3.2 | Base and per-branch price with validity (D-07: VAT included) | ✅ | Catalog → Price |
| US-3.3 | A product unavailable at a branch | ✅ | Catalog (filter by branch) |
| — | Taxes with a DIAN code | ✅ (rates to be confirmed with the accountant, I-20) | `Configuración → Impuestos` |

### M4. Inventory — phase 4
| US | Use case | Status | Where |
|---|---|---|---|
| US-4.1 | Initial stock | ✅ | `Inventario → Ajustes` |
| US-4.2 | Stock by branch and location | ✅ | `Inventario → Existencias` |
| US-4.3 | Deduct when the sale is confirmed, exactly once | ✅ | `SaleConfirmation` |
| US-4.4 | Authorized adjustments with a reason | ✅ | `Inventario → Ajustes` |
| US-4.5 | Insufficient stock per product + PIN (D-06) | ✅ | Sale → supervisor authorization |
| US-4.6 | Reconciliation balance = Σ movements | ✅ (on demand; no nightly job) | `Inventario → Movimientos` |

### M5. Registers and shifts — phase 5
| US | Use case | Status | Where |
|---|---|---|---|
| US-5.1 | Pair devices to registers with a one-time code | ✅ | `Mi negocio → Puntos de venta`; POS → `/pair-device` |
| US-5.2 | Open the cash session with opening cash | ✅ | POS → Open cash session |
| US-5.3 | Income and withdrawals (authorized withdrawal) | ✅ | POS → Cash |
| US-5.4 | Close by counting; block with pending payments (D-12) | ✅ | POS → Close cash session; backoffice → `Turnos, Cierres` |

### M6. POS and sales — phase 8
| US | Use case | Status | Where |
|---|---|---|---|
| US-6.1 | Add by button, search or barcode | ✅ | POS → Sale |
| US-6.2 | Discount with a reason (`sales.discount`) | ✅ (authorization by another user pending, I-25) | Ticket → % |
| US-6.3 | Identify the customer (NIT/ID or final consumer) | ✅ | Ticket |
| US-6.4 | Charge in cash with change, through the gateway or mixed | ✅ cash/manual · 🔎 Bold | Payment |
| US-6.5 | See the electronic payment's status live | 🔎 (no Bold sandbox) | Payment |
| US-6.6 | Receipt with the invoice status | ✅ | Receipt |
| — | Sale as a persistent process with 4 states (I-25) | ✅ | `POST /sales` … |

### M7. Payments — phase 6 (D-03: Bold)
| US | Use case | Status | Where |
|---|---|---|---|
| US-7.1 | Charge at the gateway with an idempotent reference | 🔎 | `GatewayPaymentService` |
| US-7.2 | Signed webhook, no duplicates | 🔎 | `POST /webhooks/payments/bold` |
| US-7.3 | Resolve unknown payments | ✅ audited manual (Bold has no query, I-23) | `Ventas y caja → Pagos` |
| US-7.4 | Reconcile payments against sales and registers | ✅ query · ⏳ reconciliation with a statement | `Pagos`, `Informe de ventas` |
| — | Payment methods and encrypted credentials | ✅ | `Métodos de pago`, `Integraciones` |

### M8. Electronic invoicing — phases 7 and 9 (D-04 Siigo, D-17 invoicing optional per company)
| US | Use case | Status | Where |
|---|---|---|---|
| US-8.1 | Tax configuration, numbering and credentials | ✅ | `Configuración → Facturación electrónica, Numeraciones` |
| US-8.2 | Issue each sale's invoice (if invoicing is on) and follow its status | 🔎 (no accepted document in the test environment) | Outbox → Siigo |
| US-8.3 | Query before retrying | ✅ | Same Idempotency-Key |
| US-8.4 | See rejected ones and retry | ✅ | `Ventas y caja → Facturas electrónicas` |
| US-8.5 | Credit note for a void (reason 2) or return (reason 1) | 🔎 (`taxed_price` to verify) | Phase 9 |
| US-8.6 | Turn electronic invoicing on or off per company | ✅ | `Configuración → Facturación electrónica` (D-17) |

### M9. Voids and returns — phase 9 (D-11)
| US | Use case | Status | Where |
|---|---|---|---|
| US-9.1 | Void a sale (only with its shift open, supervisor PIN) | ✅ | POS → Shift sales → Void |
| US-9.2 | Full or partial return, cash refund, per-line restock | ✅ | POS → Shift sales → Return; backoffice → `Devoluciones` |

### M10. Queries and audit — phases 3 to 9
| US | Use case | Status | Where |
|---|---|---|---|
| US-10.1 | Query sales, payments, documents, cash and inventory | ✅ | `Ventas y caja`; `Inventario` |
| US-10.2 | Change history | ✅ | `Informes → Auditoría`; `Configuración → Historial` |
| — | Sales report (day, branch, payment method, top products) | ✅ (query error fixed on 28/10) | `Informes → Ventas` |
| — | Dashboard of the day | ✅ | `Panel de control` |

### Phase 10 — Deployment and tests (D-10: VPS + Docker Compose)
| Use case | Status | Where |
|---|---|---|
| Deploy / update / roll back | 🔎 (images build; nothing deployed) | `deploy/`, `runbooks.md` §2–3 |
| Daily backups (database + keys) and restore | 🔎 | `backup.sh`, `restore.sh`, `runbooks.md` §4–5 |
| Load test | 🔎 (written, not run) | `tests/load/sales-load.js` |

## 2. Verifications pending before production

What is implemented but does not yet count as finished (exit criteria of `testing-strategy.md`):

1. **Test the full flow** against the local API (demo): pair → PIN → open cash session → sell → void → return → close → report.
2. **Bold in the sandbox**: test charge, card-terminal sale, real signed webhook (phase 6).
3. **Siigo in the test environment**: invoice, credit note for a void and for a return accepted with CUFE/CUDE (phases 7 and 9).
4. **Staging**: first deployment, a full API walkthrough (sign in, cash, sale, void, return, close), load (k6) and a backup restore (phase 10).
5. **Accountant**: D-17 (optional invoicing; confirm whether the company is obliged), D-11 (void/return), D-15 (5 years), tax rates (I-20), the Cashier's permissions.

## 3. Next use cases

### High priority (to operate)
| Use case | Why | Dependencies |
|---|---|---|
| Demo data (company, branch, register, products, cashier and supervisor with a PIN) | Run the demo without manual onboarding | Development only |
| Authorizing discounts by a supervisor | Today only whoever holds `sales.discount` can give a discount | I-25 |
| Cash spot checks (partial cash count without closing) | Menu "Próximamente" in `Ventas y caja` | Phase 5 |
| Reconciling Bold payments against the statement | Complete US-7.4 | Bold in the sandbox |
| Nightly inventory reconciliation job | Today only on demand (I-21) | — |

### Menus of the previous system still shown as "Próximamente" (no backend)
| Menu | Use cases |
|---|---|
| `Mi negocio` | Warehouses (several locations per branch) |
| `Productos e inventario` | Transfers between branches/locations |
| `Producción` | Recipes, Production orders, Ingredient consumption, Batches, Waste |
| `Abastecimiento` | Suppliers, Purchases, Receipts |
| `Informes` | Costs, Margins, Inventory, Waste |

Suggested order: Warehouses → Transfers → Suppliers/Purchases/Receipts → Recipes/Production/Consumption → Batches/Waste →
Costs/Margins reports. Each needs business decisions (D-xx) before implementing.

### Outside the MVP for now
❌ Offline sales (removed in phase 8) · ❌ physical receipt printing · ❌ company logo ·
❌ container registry and automatic deployment CI.

## 4. How to work in the next sessions

1. **One session per bounded task** (a phase, a menu or a verification), not one long conversation for everything.
   At the start: "Read `CLAUDE.md` and `docs/use-case-status.md` and continue with <task>".
2. **The source of truth is the files, not the conversation:** `CLAUDE.md`,
   `docs/decisions.md`, `docs/implementation-plan.md` and this file. They are updated when each task closes.
3. **Commit when each task is done** (after your review): it leaves a return point and allows reviewing in parts.
4. **Business decisions always in writing** in `decisions.md` before implementing (the D-xx ones that block).
5. **Verify before moving on:** each new phase starts after testing the previous one in the browser or sandbox.
