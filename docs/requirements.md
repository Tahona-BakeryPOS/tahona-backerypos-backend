# Requirements — Tahona.BakeryPOS MVP v1.1

Status: **draft for review**. There is no new functional code until this document and the blocking
decisions in [`decisions.md`](decisions.md) are approved.

## 1. Starting point: the previous system

This backend starts from a previous system (a .NET API with PostgreSQL and an offline-first POS). What is reusable is kept; the rest changes as the table indicates.

| Area | Previous system | What changes in the MVP |
|---|---|---|
| Backend | .NET 9, Clean Architecture + CQRS (MediatR), EF Core, **PostgreSQL**, JWT, Result pattern | .NET 10 LTS, **PostgreSQL** (I-18), modular monolith with explicit boundaries |
| Companies | One demo organization by seed; no company onboarding | Company onboarding and configuration |
| Security | Owner/Cashier roles, permissions as claims, PIN per paired device, backoffice email login | Kept; adds user–company membership and isolation tests |
| Catalog | Products, categories, taxes, price list with validity | Prices **per branch** |
| Inventory | Does not exist | Locations, auditable movements, balances, adjustments |
| Cash | Shifts, income/withdrawals, closing with difference | The model is kept; a cash session per cashier |
| Sales | Complete sale in one step (`POST /sales`), void | Sale as a **persistent process** with separate states |
| Payments | Manual recording (cash, card with reference) | A real gateway with attempts, webhooks and reconciliation |
| Invoicing | Does not exist | POS equivalent document / electronic invoice through a technology provider |
| Returns | Do not exist | Authorized returns with refund and restocking |
| Audit | Automatic in `SaveChangesAsync` (before/after) | Kept; extended to integration events |

Reusable without deep changes: layer structure, Result/errors, claim-based permissions,
automatic audit, device pairing, money rules (`sale-math`).
What is removed: the offline queue, synchronization (`/sincronizacion`), the local ticket consecutive.

## 2. Scope

### Inside the MVP
1. Company onboarding and configuration.
2. Branches.
3. Users, roles and permissions.
4. Basic catalog of sellable products.
5. Prices per branch.
6. Basic stock control with auditable movements.
7. Configuration of cash registers and cashiers.
8. Opening, movements and closing of the cash session.
9. POS with a cart and totals calculation.
10. Payments in cash and the supported electronic methods.
11. Integration with a real payment gateway.
12. Integration with an electronic-invoicing technology provider.
13. Querying sales, payments, documents and movements.
14. Authorized voids and returns.
15. Audit and strict isolation between companies.

### Outside the MVP
Recipes and production · purchasing and suppliers · costing · batches and expiry · transfers between
warehouses · offline mode · formal accounting · microservices/Kubernetes.

## 3. Actors

| Actor | Description |
|---|---|
| Platform administrator | Operates the SaaS: onboards companies, sees their status. Does not see companies' sales data. |
| Owner | The company owner: configures branches, catalog, prices, taxes, invoicing, payments and users. Authorizes voids, returns and adjustments. |
| Branch administrator | Manages their branch: local prices, stock, registers, queries. |
| Cashier | Opens and closes the cash session, sells, charges, records income/withdrawals. |
| Payment gateway | External system: confirms payments and sends events. |
| Technology provider | External system: validates and transmits fiscal documents to the DIAN. |

## 4. User stories and acceptance criteria

Format: *As … I want … so that …* · **AC** = verifiable acceptance criteria.

### M1. Companies and branches
- **US-1.1** As a platform administrator I want to onboard a company with its owner so that it can start operating.
  **AC:** legal name, NIT with a valid check digit, regime and tax responsibilities are mandatory; NIT unique on the platform; default roles, payment methods and taxes are created; the owner receives access with a one-time password that must be changed.
- **US-1.2** As an owner I want to edit my company's data (contact, logo, time zone).
  **AC:** changes audited with before/after; the time zone defines the "day" of reports and closings.
- **US-1.3** As an owner I want to create, edit and deactivate branches.
  **AC:** code unique per company; a branch with sales is not deleted, it is deactivated; creating it creates its default inventory location.

### M2. Security
- **US-2.1** As an owner I want to create users and assign them a role per branch.
  **AC:** the user only operates in branches with a current assignment; the register PIN is stored as a hash; email unique among users with backoffice access.
- **US-2.2** As a user I want to belong to more than one company with the same email (accountant, advisor).
  **AC:** on signing in they choose the company; the token carries a single company; switching company issues a new token. *(Decision D-08.)*
- **US-2.3** As the system I want to reject any access to another company's resources even if the identifier is valid.
  **AC:** every query by id returns 404 if the resource belongs to another company; an automated test per endpoint with an id.
- **US-2.4** As a cashier I want to sign in on a paired register by tapping my name and entering my PIN.
  **AC:** 5 failed attempts lock the user for 5 minutes; the lock is audited.

### M3. Catalog and prices
- **US-3.1** As an owner I want to create sellable products with code, name, category, unit, tax and barcode.
  **AC:** code unique per company; a product with sales is only deactivated; mass units accept decimal quantities.
- **US-3.2** As an owner I want to define a product's base price and different prices per branch.
  **AC:** the current price at a branch is the branch's if it exists, otherwise the base one; a change opens a new validity period without deleting the previous one; validity periods do not overlap.
- **US-3.3** As a branch administrator I want to mark products as unavailable at my branch.
  **AC:** an unavailable product does not appear in that branch's POS.

### M4. Inventory
- **US-4.1** As a branch administrator I want to record each product's initial stock.
  **AC:** it is recorded as an `initial` movement; the balance is not edited directly.
- **US-4.2** As an administrator I want to query stock by branch and location.
- **US-4.3** As the system I want to deduct stock when a sale is confirmed (payment approved), exactly once.
  **AC:** retrying the confirmation does not duplicate the movement (idempotent key).
- **US-4.4** As an owner I want to authorize inventory adjustments with a reason.
  **AC:** adjustment with a mandatory reason and authorizing user; audited.
- **US-4.5** As the system I want to apply the insufficient-stock policy defined in D-06.
- **US-4.6** As an owner I want to see whether balances match the sum of movements (reconciliation).

### M5. Cash registers and shifts
- **US-5.1** As an owner I want to create registers per branch and pair devices to each register (one-time code).
- **US-5.2** As a cashier I want to open my cash session declaring the opening cash.
  **AC:** one open session per register; opening cash ≥ 0.
- **US-5.3** As a cashier I want to record cash income and withdrawals with a reason; a withdrawal requires authorization.
- **US-5.4** As a cashier I want to close the cash session by counting the cash.
  **AC:** expected = opening + net cash from sales (received − change) − cash returned + income − withdrawals; difference visible; it cannot be closed with sales in an unresolved pending-payment state (see D-12).

### M6. POS and sales
- **US-6.1** As a cashier I want to add products by button, search or barcode and see subtotal, taxes and total.
- **US-6.2** As a cashier I want to apply discounts with a reason (requires the `sales.discount` permission or authorization).
- **US-6.3** As a cashier I want to identify the customer (final consumer by default, or NIT/ID if they ask for an invoice).
- **US-6.4** As a cashier I want to charge in cash (with change), with the gateway's electronic methods, or with a mixed payment.
  **AC:** the sale is only confirmed when all its payments are approved by the backend; no client marks a payment as approved.
- **US-6.5** As a cashier I want to see an electronic payment's status on screen (waiting, approved, declined) and retry with another method if it fails.
- **US-6.6** As a cashier I want to print/show the receipt with the fiscal document's status.

### M7. Payments
- **US-7.1** As the system I want to create the payment at the gateway with a unique, idempotent reference.
- **US-7.2** As the system I want to receive and validate signed events from the gateway, ignoring duplicates.
- **US-7.3** As the system I want to resolve payments with an unknown result by querying the gateway before retrying.
- **US-7.4** As an owner I want to reconcile approved electronic payments against sales and registers.

### M8. Electronic invoicing
- **US-8.1** As an owner I want to configure the company's tax data, the authorized numbering and the provider's credentials (test and production separate).
- **US-8.2** As the system I want to issue the fiscal document of each confirmed sale **when the company has invoicing turned on** and follow its status until there is a verifiable response; with invoicing off the sale stays "not invoiced" (D-17).
- **US-8.6** As an owner I want to turn electronic invoicing on or off as I need, without affecting payments or what was already invoiced.
- **US-8.3** As the system I want to query the status before retrying a submission with an unknown result.
- **US-8.4** As an owner I want to see rejected documents, their reason, and correct/reissue them according to the defined procedure.
- **US-8.5** As the system I want to issue the note (credit/adjustment) corresponding to a return or void of an already documented sale.

### M9. Voids and returns
- **US-9.1** As an owner I want to void a same-day sale before the fiscal document is accepted *(exact rule in D-11)*.
- **US-9.2** As an authorized cashier I want to record a full or partial return of a sale.
  **AC:** requires authorization and a reason; generates a refund (cash or through the gateway), a compensating inventory movement and a fiscal note; no return exceeds what was sold.

### M10. Queries and audit
- **US-10.1** As an owner I want to query sales, payments, fiscal documents, cash and inventory movements with filters.
- **US-10.2** As an owner I want to see the change history and integration events.

## 5. Screens and flows

The screens belong to the clients; they are listed here because they define what the API must support. Screen text is Spanish.

### 5.1 POS (register)
| Screen | Purpose | Notes |
|---|---|---|
| Pair device | Associate the tablet/PC to a register with a code | Reuses the previous system |
| Cashier login | Choose a user and enter a PIN | Reuses the previous system; adds attempt lockout |
| Open cash session | Opening cash; resume an open session | Reuses the previous system |
| Sale | Catalog by category, search/scanner, ticket, customer | Branch prices; shows low stock |
| Payment | Cash, gateway (Nequi/QR/link), mixed | **New**: live payment status, cancel/retry |
| Receipt | Ticket + fiscal status (pending/accepted/rejected) | **New** fiscal status |
| Shift sales | List, detail, request void/return | Return is **new** |
| Cash | Income/withdrawals, expected amount | Reuses the previous system |
| Close cash session | Count, difference, pending items to resolve | Blocked by pending payments |

**Main sale flow:** Sale → (customer) → Charge → choose method → [cash: confirm] /
[electronic: wait for the backend's confirmation] → sale confirmed → inventory deducted →
fiscal document in progress → receipt.

### 5.2 Backoffice
It reuses the previous system's 8-module navigation. New or extended screens:

| Module | Screen | Status |
|---|---|---|
| Platform | Companies (onboarding, status) — platform administrator only | New |
| My business | Company (tax data), Branches, Points of sale | Extended |
| Products and inventory | Catalog, Categories, Units, **Prices per branch**, **Stock**, **Movements**, **Adjustments** | New: prices, stock, movements, adjustments |
| Sales and cash | Sales, Shifts, Closings, **Payments**, **Returns**, Payment methods | New: payments, returns |
| Invoicing | **Tax configuration**, **Numberings**, **Documents** (status, retry, downloads) | New module |
| Integrations | **Payment gateway** (credentials, webhooks), **Tax provider** | New |
| Reports | Sales, **Payment reconciliation**, **Inventory reconciliation**, Audit | New: reconciliations |
| Configuration | Users, Roles, Taxes, History | Reused |

## 6. Non-functional requirements

| Topic | Requirement |
|---|---|
| Isolation | No data of one company is visible or modifiable from another; a mandatory automated test per endpoint. |
| Consistency | Sale, payments, inventory and fiscal document with separate states and explicit transitions; no external operation inside a SQL transaction. |
| Idempotency | Every operation that can be retried (create sale, pay, deduct inventory, issue document, process webhook) is idempotent. |
| Audit | Sales, payments, returns, inventory and fiscal documents audited with user, date and before/after. |
| Security | Third-party credentials encrypted at rest; secrets outside the repository; HTTPS mandatory; no card data in our database. |
| Availability | The POS requires a connection (no offline mode). If the gateway or the tax provider fail, the cash sale continues and the fiscal document stays pending submission. |
| Performance | Sale screen with 500 products usable on a mid-range tablet; cash-sale confirmation < 1 s on the server. |
| Currency and time | COP with 2 decimals; dates in UTC; operating day according to the company's time zone. |
