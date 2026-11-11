# Testing strategy

Rule: a feature is not finished if its tests do not really run. Anything that depends on an external
provider is tested against its **real sandbox**; fakes are only for unit tests
and never for declaring an integration finished.

## Levels

| Level | Tool | What it covers | Where it runs |
|---|---|---|---|
| Unit (domain) | xUnit | Totals, included/excluded taxes, rounding, change, sale/payment/document state transitions, stock policy, numbering | CI on every PR |
| Integration (persistence) | xUnit + **real PostgreSQL** (Testcontainers, D-16) | Constraints and unique indexes, company filters, balance concurrency, idempotency, outbox in the same TX | CI |
| API / contract | `WebApplicationFactory` + PostgreSQL | Error codes, `Idempotency-Key`, `If-Match`, permissions per endpoint, OpenAPI with no incompatible changes | CI |
| **Multi-company isolation** | Generated per endpoint | With company A tokens, read/write company B ids on **every** endpoint → 404/403, never data | CI (mandatory, blocks merge) |
| Architecture | NetArchTest (D-16) | A module does not reference another module's domain or tables | CI |
| Payment gateway integration | Provider sandbox | Create a transaction, real signed webhook, status query, refund | Manual/nightly with sandbox credentials |
| Tax integration | Provider/DIAN test environment | Issue, query, rejection, credit note | Manual/nightly |
| API walkthrough on staging | Against `/api/v1` (OpenAPI at `/openapi/v1.json`) | Full flow: sign in, pair a register, open the cash session, cash sale, void, return, close and report | Staging, before every release to production |
| Load | k6, `tests/load/sales-load.js` | 20 registers selling in cash: p95 < 800 ms, < 1 % errors | Staging, before production |

## Critical failure scenarios (automated)

1. Retry of `POST /sales` with the same `Idempotency-Key` → a single sale.
2. Timeout when creating the gateway transaction → attempt `unknown`; the worker resolves it by querying; no second charge is created.
3. Duplicate webhook → a single state change; webhook with an invalid signature → rejected and audited.
4. Webhook never arrives → the worker queries and confirms the sale.
5. Payment approved but the confirmation transaction fails → the retry confirms once; inventory deducted once.
6. Approved payment of a cancelled sale → left flagged for refund in reconciliation.
7. Timeout when issuing the document → `unknown`; no resend without a prior query; same number.
8. Tax rejection → `rejected` with reasons; no fake "accepted" state.
9. Two registers sell the last unit with the `block` policy → one sale goes through, the other gets `INSUFFICIENT_STOCK`.
10. Return exceeding what was sold → 422; successive partial returns add up correctly.
11. Cash close with a pending payment → blocked (D-12).
12. Process crash between steps → workers resume from the persisted state.

## MVP exit criteria

- All CI suites green, including isolation and architecture.
- Full E2E flow demonstrated against the **real sandbox** of the gateway and the tax test environment:
  company → branch → product with price and stock → open cash session → sale → confirmed payment →
  consistent movements → document with verifiable status → cash close.
- Scenarios 1–12 automated and passing.
- Inventory reconciliation and payment reconciliation without differences in the test set.
