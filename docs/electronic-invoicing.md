# Electronic invoicing integration

Status: **D-04 = Siigo, D-17 = electronic invoicing optional per company (replaces D-05), D-15 = 5 years**. What was verified
about Siigo's API and its consequences are in section 9. Pending: a test with an accepted document in the
test environment (acceptance criterion of phase 7) and confirmation from the accountant (D-05, D-15).

## 1. Regulatory framework (summary, to confirm with the accountant)

| Topic | What was found | Source | Confidence |
|---|---|---|---|
| Electronic POS equivalent document | The "cash register receipt with a P.O.S. system" becomes **electronic**; the obligated party replaces it with the electronic equivalent document or with an electronic sales invoice | [DIAN – Electronic equivalent document](https://micrositios.dian.gov.co/sistema-de-facturacion-electronica/documento-equivalente-electronico) | High |
| Legal basis | DIAN Resolution 000165 of 2023 (arts. 15, 19, 20); calendar modified by Resolution 000008 of 2024 | DIAN; [Actualícese](https://actualicese.com/dian-modifica-el-calendario-de-implementacion-de-documentos-equivalentes-electronicos/) | High / medium |
| Current compilation | DIAN Single Resolution 000227 of 2025 | [Siempre al Día](https://siemprealdia.co/colombia/calendario-del-sistema-de-facturacion-electronica/) | **Medium — verify the official text** |
| Mandatory dates | Staggered in 2024 by taxpayer type; new obligated parties from their enablement | Secondary sources | Medium |
| 5 UVT cap | Contradictory sources: the original text mentions it; Sovos and DIAN concept 1563 of 2024 indicate the electronic POS is issued regardless of the value | [Sovos](https://sovos.com/co/iva/documento-equivalente-electronico-pos/), [Accounter](https://accounter.co/normatividad/pos-electronico-soporte-de-costos-y-deducciones-en-renta-concepto-1563-dian-de-2024) | **Low — the accountant's decision** |
| Identifying the buyer | The document may identify the buyer (name, NIT); with it, it gives the right to deductible VAT and tax deduction | DIAN | High |
| Transmission, prior validation, CUDE, technical annex | Not specified on the page consulted | — | **To verify** |

**Implication (if the company is obliged to invoice):** the bakery must issue, for each sale, an electronic POS equivalent document or
an electronic invoice, through a technology provider or the DIAN's service. Which
document applies in each case (final consumer vs. customer with a NIT, amount) is defined by D-05 with the
accountant.

> **Optional per company (D-17).** Invoicing is turned on or off in Configuración → Facturación electrónica. When on,
> each sale generates its invoice (§4). When off, the sale keeps `fiscal_status = not_required` and no
> document is created; everything else (Bold payments, inventory, cash, returns) is unchanged. When it is turned off, the documents that already
> exist (invoices in progress and credit notes of already-invoiced sales) keep being sent with the stored configuration;
> a sale that was not invoiced does not generate a credit note. If a company is obliged by the DIAN, keeping invoicing off
> is the company's responsibility; the system does not validate it.

## 2. Selecting the technology provider

No verifiable public official API documentation was found in this review for the
candidates. Criteria to decide:

| Criterion | Weight | How it is verified |
|---|---|---|
| Supports the **electronic POS equivalent document** and electronic invoicing, plus notes | Mandatory | Documentation + demo |
| Documented REST API with a test environment (DIAN enablement) | Mandatory | Sandbox account |
| Status query by own id or consecutive (for unknown results) | Mandatory | Documentation |
| Idempotency or duplicate rejection by prefix+number | Mandatory | Sandbox test |
| Returns CUFE/CUDE, signed XML, PDF/graphic representation, QR | Mandatory | Documentation |
| Status-change webhooks (desirable) | Desirable | Documentation |
| Price per document and SaaS scalability (multi-NIT under one account) | High | Quote |
| Support and SLA | Medium | Contract |

Candidates to contact (in no order of preference): Factus (Halltec), Alegra, Siigo, Dataico, The
Factory HKA, Carvajal, Sovos, and the DIAN's free option (not suitable for API integration).
The recommendation will be presented after validating at least two in a sandbox.

## 3. Provider-independent design

Port `IFiscalDocumentProvider`:
```
SubmitAsync(FiscalDocumentPayload)      → Accepted | Rejected(reasons) | Pending(providerId) | Unknown
GetStatusAsync(providerId | prefix+number) → same result
GetArtifactsAsync(providerId)            → XML, PDF, QR, CUFE/CUDE
SubmitNoteAsync(...)                     → credit / adjustment note
```
The domain builds the document (issuer, buyer, lines, taxes, totals, numbering) and the
adapter translates it to the provider's format.

## 4. Document life cycle

```
confirmed sale → fiscal_document 'pending' (number assigned from the current range, in the same TX)
worker → 'submitting' → SubmitAsync
   ├─ Accepted  → 'accepted' + CUFE/CUDE + files stored
   ├─ Rejected  → 'rejected' + reasons (not retried automatically)
   ├─ Pending   → follow-up with GetStatusAsync
   ├─ transient error (5xx, timeout before sending) → 'temporary_error', retry with backoff
   └─ Unknown (timeout after sending) → 'unknown' → GetStatusAsync mandatory before resending
```
Rules:
- A document is **never** marked `accepted` without a verifiable provider response (CUFE/CUDE and DIAN status).
- An `unknown` document is **never** resent without a status query recorded in
  `fiscal_submission_attempts`. The resend uses the **same prefix and number**.
- Range numbers are assigned with a row lock and are not reused; a rejected document
  keeps its number and the correction follows the provider's procedure (*to verify*).
- A range about to expire or run out → early alert to the owner (configurable threshold).

## 5. Tax configuration

Per company (and per branch when numbering is per point of sale): NIT and check digit, regime and
responsibilities, economic activity, address and municipality (DANE), resolutions and numbering
ranges, environment (test/production), encrypted provider credentials. Test and production
in separate records; switching to production requires explicit confirmation and is audited.

## 6. Retention

Signed XML, PDF/graphic representation, CUFE/CUDE, and the provider's and DIAN's response are stored
in object storage (paths in `xml_storage_key`, `pdf_storage_key`) and kept for
the legal period confirmed by the accountant (D-15).

## 7. Notes and corrections

- Return or void of a sale with an accepted document → credit note / adjustment note against the
  equivalent document, depending on the type issued.
- Sale voided before the document is issued → the document is not sent (it stays `cancelled`) only if
  the regulation allows it (D-11).

## 8. To verify

1. Current official text (Single Resolution 000227 of 2025) and the calendar applicable to the target customers.
2. UVT cap for the electronic POS and when an electronic invoice is required.
3. Current technical annex of the POS equivalent document and whether it requires prior validation.
4. Enablement process and test set for each customer NIT (impact on SaaS onboarding).
5. API, sandbox, status query and idempotency of the chosen provider.

## 9. Siigo (verified on 28/10/2026)

Official sources: [Introduction](https://developers.siigo.com/docs/siigoapi/), [Create invoice](https://developers.siigo.com/docs/siigoapi/invoice/1-create-invoice),
[Document types](https://developers.siigo.com/docs/siigoapi/catalog/6-ge-types-of-receipt/), [Idempotency](https://developers.siigo.com/docs/siigoapi/idempotencia/),
[Partner Id](https://developers.siigo.com/docs/siigoapi/partner-id/), [User blocking](https://developers.siigo.com/docs/siigoapi/bloqueo-de-usuarios/),
[Electronic invoicing](https://developers.siigo.com/docs/siigoapi/facturacion-electronica/), the official API Blueprint
([siigoapi.docs.apiary.io](https://siigoapi.docs.apiary.io/)) and the official SDK [SiigoSAS/siigo_sdk_javascript](https://github.com/SiigoSAS/siigo_sdk_javascript).

**Why D-05 changed (before D-17):** Siigo states on its POS-system page that it decided **not to develop the electronic POS
equivalent document** and to focus on the electronic invoice; no other provider reviewed (Factus, Alegra,
Dataico, The Factory HKA) publicly documents an API for the electronic POS. The DIAN allows replacing the
equivalent document with an electronic invoice, so every sale is invoiced electronically: with the customer's
data if they give it and, if not, as a final consumer. **Confirm with the accountant.**

| Topic | Verified |
|---|---|
| Base URL | `https://api.siigo.com` (resources under `/v1/...`) |
| Token | `POST /auth` with `username` and `access_key` → `access_token` (Bearer) valid for 24 h. Credentials per company, generated in Siigo Nube |
| Headers | `Authorization: Bearer <token>` and `Partner-Id` (the application's name, 3–100 alphanumeric characters, the same for all companies) |
| Create invoice | `POST /v1/invoices`: `document.id` (electronic FV type), `date` (not earlier than today), `customer.identification` (must exist in Siigo), `seller` (Siigo user), `items[]` (`code` of a product that exists in Siigo, `quantity` ≤ 2 decimals, `price` or `taxed_price` with VAT included, `taxes[].id`), `payments[]` (`id` from `/payment-types`, `value`; the sum must equal the total), `stamp.send = true` sends to the DIAN |
| Response | `id` (GUID), `prefix`, `number`, `name`, `total`, `stamp.status` (`Draft`, `Accepted`, `Rejected`), `stamp.cufe`, `stamp.errors` |
| DIAN errors | `GET /v1/invoices/{id}/stamp/errors`; resend: `POST /v1/invoices/{id}/stamp`; a rejected one is corrected and resent |
| Idempotency | `Idempotency-Key` on invoice and credit-note POSTs (alphanumeric, max 30 per the page; the Blueprint says 32): repeating the key returns the invoice already created |
| Credit notes | `POST /v1/credit-notes` with `invoice` (the invoice's id), `reason` (DIAN reason), `items`, `payments`, `stamp` |
| Representation | `GET /v1/invoices/{id}/pdf`. **There is no XML endpoint** in the reference: the XML stays in Siigo Nube |
| Catalogs | `/v1/document-types?type=FV`, `/v1/users`, `/v1/taxes`, `/v1/payment-types`, `/v1/customers`, `/v1/products` |
| Blocking | API user blocked if more than 80 % of requests are errors over 7 days |
| Tests | No separate test environment is documented; the environment (enablement or production) is the company's in Siigo Nube. The Blueprint uses a sample account `sandbox@siigoapi.com` |

**Design consequences (I-24):**
1. Numbering and resolutions are kept by Siigo (electronic FV document type); the system stores the prefix and
   number that Siigo returns. "Numberings" shows Siigo's document types, not own ranges.
2. Each company needs its own Siigo Nube account with API access; its keys are stored encrypted like Bold's.
3. Products must exist in Siigo with the same code (SKU): they are created in Siigo the first time they are invoiced
   (`POST /v1/products`, with the configured inventory group). Taxes and payment methods are paired with Siigo's in
   the configuration. The final consumer must exist in Siigo (configurable identification, `222222222222` by
   default).
4. Sending is done by the outbox (outside the transaction) with `Idempotency-Key` = the document id in base 36; if the
   result is unknown, repeating the POST with the same key works as a status query (there is no other one by own
   reference).
5. No XML by API: the PDF is stored on demand along with the CUFE; the XML is consulted in Siigo Nube. Retention 5 years (D-15).

**To verify with a Siigo account in the test environment:** exact format of the product and customer body, values of
`stamp.status` besides `Accepted`, whether `Draft` appears when the DIAN is slow, and the real `Idempotency-Key` limit (30 or 32).

## 10. Credit notes with Siigo (phase 9, verified at developers.siigo.com and the official SDK)

| Topic | Detail |
|---|---|
| Create | `POST /v1/credit-notes`: `document.id` (NC document type), `date` (not earlier than today), `invoice` (the invoice's GUID in Siigo), `reason` (DIAN reason: 1 partial return, 2 invoice void, 3 discount, 4 price adjustment…), `items[]` (`code`, `quantity` ≤ 2 decimals, `price`), `payments[]` (`id`, `value`), `stamp`, `mail` |
| Query / PDF | `GET /v1/credit-notes/{id}` and `GET /v1/credit-notes/{id}/pdf` (official SDK `SiigoSAS/siigo_sdk_javascript`) |
| Response | `id`, `number`, `name`, `total`, `stamp` (`status`, `cufe`, `cude`, `errors`) |
| To verify | The guide uses `price` (without VAT) and only the spec example shows `taxed_price`: `taxed_price` is sent as in the invoice. There is no documented DIAN-errors or resend endpoint for notes: they are queried with `GET`. The documentation is not consistent on the range of `reason` |

Rules (D-11, I-26): void → a note with reason 2 for the whole invoice; return → reason 1 for what was returned, with the
refunded methods. The note waits in the outbox until the invoice is accepted; if the invoice was rejected, the note
is left void (`cancelled`) and, on a void, so is the invoice.
