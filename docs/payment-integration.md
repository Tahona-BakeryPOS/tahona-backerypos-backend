# Payment gateway integration

Status: **D-03 approved: Bold, with its card-terminal API ("API Integrations")** (27/10/2026). What was verified
in the official documentation is in section 11; whatever is still *to verify* is confirmed in the merchant's sandbox
before production. Nothing is implemented unless it is in the official documentation.

## 1. Context: in-person payment in a bakery

Payment is in person. In Colombia the relevant methods are cash, card on a terminal,
Nequi/Daviplata, bank QR (Bre-B/Bancolombia) and transfers. Gateways with a public API
are oriented to e-commerce (the customer pays from their phone or with a link/QR); integrating
**physical card terminals** through an API has no verified public documentation.

## 2. Options evaluated

| Criterion | Wompi (Bancolombia) | Bold | Others (PayU, Mercado Pago, ePayco) |
|---|---|---|---|
| Public API documentation | **Yes** (docs.wompi.co) | Partial: a developer portal exists; details seen only in third-party sources | Yes (not evaluated in depth) |
| Sandbox | **Yes**, with separate event URLs for sandbox and production | *To verify* | — |
| Methods useful at the counter | `NEQUI` (push to the phone), `BANCOLOMBIA_QR` (scan in the app; natural persons only), `CARD` (card not present), `DAVIPLATA` (OTP) | Payment link / button; own card terminal | — |
| Status query | **Yes**: `GET /v1/transactions/{id}` with the private key | *To verify* | — |
| States | **`PENDING`, `APPROVED`, `DECLINED`, `VOIDED` (card only), `ERROR`** | *To verify* | — |
| Signed webhooks | **Yes**: `transaction.updated` event; SHA256 checksum of the `signature.properties` fields + `timestamp` + events secret; `X-Event-Checksum` header; 3 retries in 24 h | Webhook mentioned by third parties | — |
| Unique reference | **Yes**: `reference` must be unique | *To verify* | — |
| Explicit idempotency on creation | Not documented in what was reviewed | *To verify* | — |
| Card terminal integrated by API | No | Possible, with no public documentation | — |

Sources reviewed (27/10/2026): [Wompi events](https://docs.wompi.co/en/docs/colombia/eventos/),
[Wompi transaction tracking](https://docs.wompi.co/en/docs/colombia/seguimiento-de-transacciones/),
[Wompi payment methods](https://docs.wompi.co/en/docs/colombia/metodos-de-pago/). No accessible official
documentation was found for Bold; what was seen (payment-links endpoint, base URL) comes from
third-party plugins and **will not be used** as a basis.

## 3. Recommendation (to approve)

1. **Wompi** as the MVP gateway for electronic payments started from the POS: `NEQUI` (push) and
   `BANCOLOMBIA_QR` (QR on screen), and optionally a card payment link.
2. **Card on an external terminal** (Bold, Redeban, etc.) as a **non-integrated** method: the cashier charges
   on the terminal and records the approval number. It is marked `external_card` and goes into
   manual reconciliation. It is explicit that it is **not** a gateway confirmation.
3. Before implementing: create a sandbox account, confirm the points *to verify* in
   section 7 with Wompi and validate fees/settlement times with the owner.

## 4. Payment flow with a gateway

```
POS                 API (Payments)                       Wompi
 │ POST /sales/{id}/payments ─► TX: payment + attempt 'created'
 │                          (reference = attempt.id)
 │                          outside TX: POST transaction ──────────►
 │                          ◄── external id, PENDING (or timeout)
 │                          TX: attempt 'pending' (+ external_id) | 'unknown' on timeout
 │ ◄── { estado, accion: QR / push }
 │ GET /sales/{id} (polling 2–3 s)    
 │                                       ◄── webhook transaction.updated (signed)
 │                          TX: inbound_event (dedupe) → attempt 'approved' → confirm sale
 │ ◄── confirmed
```
- A payment is **never** approved from a client response or from a redirect return:
  only by a valid webhook or a server-to-server query with the private key.
- The amount is sent in cents and compared with the attempt's when the event arrives; if it does not
  match, the attempt moves to `error` and an alert is raised.

## 5. Idempotency and duplicates

| Risk | Control |
|---|---|
| The POS retries "charge" | `Idempotency-Key` on `POST /sales/{id}/payments` → same attempt |
| Two active attempts for the same sale | A single `pending/unknown` attempt per payment; a new one only if the previous one is final and not approved |
| Repeated webhook | `inbound_events` UQ(provider, external_event_id) or (provider, transaction id, status) — *exact key to verify* |
| Forged webhook | Checksum validated with the events secret; invalid event → 401 and audit |
| Timeout on create | State `unknown`; query by external id or by reference before creating another attempt |

## 6. Recovering unknown results

A worker every 15 s for `pending`/`unknown` attempts:
1. If there is an `external_id` → `GET /v1/transactions/{id}`.
2. If there is no `external_id` (timeout on create) → search by reference (*endpoint to verify*).
   If it does not exist at the gateway after N minutes → `expired`, and a new attempt is allowed.
3. After the method's maximum time (e.g. Nequi) with no final state → alert the cashier and the owner;
   the sale stays `pending_payment` and blocks the cash close until resolved (D-12).

## 7. To verify with the provider before implementing

1. Endpoint and payload for creating transactions for `NEQUI` and `BANCOLOMBIA_QR`; QR validity.
2. Transaction query by `reference`.
3. Unique event key to deduplicate webhooks.
4. Refunds/voids: `VOIDED` only applies to cards according to the documentation; how a Nequi or QR
   payment is refunded (API or manual process?).
5. Integrity signature required when creating transactions (if applicable) and key format per environment.
6. Settlement times, fees and certifications required of the merchant.

## 8. Refunds and returns

- Return of a sale paid in cash → cash refund from the open register
  (`cash_movements.refund_cash`).
- Paid through a gateway → refund by API if the provider supports it for that method; otherwise a **manual**
  refund recorded with supporting evidence and approved by the owner. The refund's state is its own
  (`refunds.status`) and is not mixed with the return's.

## 9. Reconciliation

Daily per company: payments approved at the gateway (provider query/reports, *to verify*)
vs. approved attempts vs. confirmed sales. Differences → a report with actions (refund,
confirm sale, investigate).

## 10. Credentials

Public key, private key and events secret **per company and environment** in
`pay.gateway_accounts`, encrypted with Data Protection; never returned by the API or written to logs.
Sandbox and production in separate records; startup in production rejects sandbox keys.
No card data is stored (if `CARD` is used, tokenization happens in the provider's widget/checkout).

## 11. Bold — card-terminal API (verified at developers.bold.co, 27/10/2026)

Sources: [API Integrations](https://developers.bold.co/api-integrations/integration),
[Sandbox](https://developers.bold.co/api-integrations/integration-sandbox), [Webhook](https://developers.bold.co/webhook),
[Keys](https://developers.bold.co/pagos-en-linea/llaves-de-integracion),
[Transaction query](https://developers.bold.co/pagos-en-linea/consulta-de-transacciones).
The earlier search (section 2) did not find the documentation; it does exist and is the basis of this integration.

| Topic | Verified |
|---|---|
| Base URL | `https://integrations.api.bold.co` (the same for test and production; the environment is defined by the key) |
| Authentication | `Authorization: x-api-key <identity_key>`; test and production keys at panel.bold.co → Integraciones → API datáfono |
| Enabled methods | `GET /payments/payment-methods` → `POS` (card on the terminal), `NEQUI`, `PAY_BY_QR_BOLD`, `PAY_BY_LINK` |
| Terminals | `GET /payments/binded-terminals` → `terminal_model`, `terminal_serial`, `status`, `name` |
| Create a charge | `POST /payments/app-checkout` with `amount` (`currency`, total, `taxes`, tip), `user_email` (seller), `payment_method`, `terminal_model`, `terminal_serial`, `reference`; 201 → `payload.integration_id` (for support only). Errors AP001–AP006 |
| Result | **Only by webhook**: `SALE_APPROVED`, `SALE_REJECTED`, `VOID_APPROVED`, `VOID_REJECTED`; `data.metadata.reference` carries our reference and `data.payment_id` Bold's id |
| Webhook signature | `x-bold-signature` header = hexadecimal HMAC-SHA256 of the **Base64 body**, with the secret key; in tests the key is empty |
| Webhook retries | Answer 200 within 2 s; up to 5 retries (15 min, 1 h, 4 h, 8 h, 24 h). Bold may notify the same transaction several times: deduplicate by the notification `id` and validate by `payment_id` |
| Sandbox | Result by amount: 1,000–2,000,000 approved; 111,111, 222,222, 333,333, 444,444 and 999,999 simulated rejections. The test terminals are the merchant's own |
| Status query | **There is none for app-checkout** (the `payment-voucher` one is only for the payment button) |
| Remote cancellation | There is none: the charge cannot be cancelled from the system |

**To verify in the merchant's sandbox:**
1. Names of the amount fields: the documentation table says `amount.total` and `amount.tip`, the
   example uses `total_amount` and `tip_amount`. The example's was implemented (`BoldClient`); if the sandbox
   answers AP005/AP006 it is changed in a single place.
2. Whether the webhooks of API Integrations test transactions reach the test webhook (the page
   has two sentences that contradict each other).
3. Uniqueness of `reference` (not documented; the attempt id is sent, which is unique).
4. Refund or void of Nequi/QR payments by API (not documented): manual refund (section 8).

**Design consequences (I-23):** with no status query there is no recovery worker that asks
Bold; an attempt with no response stays `pending`/`unknown` until the webhook arrives. If it does not arrive, a
supervisor with `payments.resolve` resolves it by hand: **approved** by recording the approval number from the
terminal receipt (it is marked "manually approved", audited and flagged in reconciliation),
**declined** or **cancelled**. Since there is no remote cancellation, before cancelling it must be checked on the
terminal that the charge was not made.
