# Basic inventory

Status: **implemented in phase 4** (D-06 = option C; details in I-21). It tracks stock of sellable products; no recipes,
production, batches or transfers.

## 1. Model

- **Location** (`inv.stock_locations`): each branch has a default location ("Mostrador", code `COUNTER`).
  More can be created (e.g. "Vitrina", "Bodega") but there are no transfers in the MVP.
- **Movement** (`inv.stock_movements`): insert-only ledger. Signed quantity.
- **Balance** (`inv.stock_balances`): `on_hand` projection per (location, product), updated in the
  **same transaction** as the movement, with optimistic concurrency (`row_version`).
- Only products with `track_stock = true` generate movements.

## 2. Movement types

| Type | Sign | Origin | Authorization |
|---|---|---|---|
| `initial` | + | Initial registration per branch | `inventory.adjust` |
| `sale` | − | Confirmed sale line | Automatic |
| `return` | + | Approved return line (if the product goes back on sale) | `returns.approve` |
| `void_reversal` | + | Void of a confirmed sale | `sales.void` |
| `adjustment_in` / `adjustment_out` | ± | Count, waste, damage, internal use | `inventory.adjust` + reason |

Bakery waste (unsold product of the day) is recorded as `adjustment_out` with the "Merma" reason (code `WASTE`) in the
MVP; costing it is out of scope.

## 3. Rules

1. **There is no direct balance editing.** Any change is a movement.
2. **Idempotency**: unique index `(company_id, kind, source_type, source_id, source_line_id)`.
   Confirming the same sale twice does not deduct twice.
3. **Deduction per confirmed sale**: it happens in the confirmation transaction (step 4 of the flow),
   never when adding to the cart or when creating the draft sale.
4. **Return**: a compensating movement; the return may state "does not restock" (damaged
   product), in which case it generates no `return` movement, only the return record.
5. **Void**: `void_reversal` for each line, linked to the sale.
6. **Audit**: each movement records user, device, reason and authorizer.

## 4. Insufficient stock policy — D-06 approved: option C

| Option | Behavior | Advantages | Risks |
|---|---|---|---|
| A. Always block | The sale is not confirmed if `on_hand < quantity` | Balances always ≥ 0 | In a bakery bread is produced and sold without entries being recorded in time; it stalls the register |
| B. Allow negative with a warning | It sells and the balance goes negative; alert to the administrator | Never stalls the sale | Unreliable balances if nobody corrects them |
| **C. Configurable per product + authorization (recommended)** | Each product defines `negative_stock_policy` = `block` or `allow`. With `block`, the cashier can ask for the owner's authorization (PIN) and it is audited | Daily bread on `allow`, packaged products on `block` | A bit more configuration |

Option C approved (27/10/2026): `products.negative_stock_policy` (`allow` by default). `InventoryLedger` applies it: with
`block`, a movement that would leave the balance negative requires `authorized_by`.

## 5. Reconciliation

- Query `GET /inventory/reconciliation`: compares `stock_balances.on_hand` with `Σ stock_movements`
  per (location, product) and lists differences (they should be zero).
- Nightly job per company: runs the reconciliation and records the result; a difference raises an
  alert (it indicates a software error, not an operational one).
- Physical count: the administrator records the count and the system proposes `adjustment_in/out` for the
  difference, which are confirmed with the "Conteo físico" reason (code `PHYSICAL_COUNT`).

## 6. Concurrency

Two registers sell the same product at the same time: the balance update uses `row_version`; on
conflict the confirmation transaction is retried (up to 3 times). With the `block` policy, the
stock check is done inside that transaction with a row lock
(`SELECT … FOR UPDATE`) so the last unit is not sold twice.
