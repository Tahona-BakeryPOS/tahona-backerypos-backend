namespace Tahona.BakeryPOS.Application.Invoicing.Siigo;

/// <summary>A company's credentials in clear text (only in memory, never in logs).</summary>
public sealed record SiigoCredentials(Guid CompanyId, string Username, string AccessKey);

public sealed record SiigoCatalogItem(string Id, string Name, string? Code, string? Detail, bool Active);

/// <param name="TaxedPrice">Unit price with VAT included (D-07); Siigo uses it instead of <c>price</c>.</param>
/// <param name="Discount">Value of the line's discount (to verify in the test environment whether Siigo applies it before or after VAT).</param>
public sealed record SiigoInvoiceItem(string Code, string Description, decimal Quantity, decimal TaxedPrice, IReadOnlyList<long> TaxIds, decimal Discount = 0);

public sealed record SiigoPayment(long Id, decimal Value);

public sealed record SiigoInvoice(long DocumentTypeId, DateOnly Date, string CustomerIdentification, long SellerId,
    IReadOnlyList<SiigoInvoiceItem> Items, IReadOnlyList<SiigoPayment> Payments, string Observations);

/// <param name="InvoiceId">The invoice's GUID in Siigo.</param>
/// <param name="Reason">DIAN reason: 1 partial return, 2 invoice void.</param>
public sealed record SiigoCreditNote(long DocumentTypeId, DateOnly Date, string InvoiceId, int Reason, IReadOnlyList<SiigoInvoiceItem> Items,
    IReadOnlyList<SiigoPayment> Payments, string Observations);

public enum SiigoOutcome
{
    /// <summary>The provider answered with the document (see <see cref="SiigoInvoiceResult.StampStatus"/>).</summary>
    Ok,
    /// <summary>Validation 4xx: the document was not created.</summary>
    Invalid,
    /// <summary>Timeout, network or 5xx: it is unknown whether it was created.</summary>
    Unknown,
}

/// <param name="StampStatus">DIAN result: <c>Accepted</c>, <c>Rejected</c>, <c>Draft</c> (not sent) or another.</param>
public sealed record SiigoInvoiceResult(SiigoOutcome Outcome, int? HttpStatus, string? Id, string? Prefix, long? Number, string? StampStatus,
    string? Cufe, string? Errors);

/// <summary>
/// Siigo API client (electronic-invoicing.md §9). It is always called outside a SQL
/// transaction. The token (24 h) is kept in memory per company.
/// </summary>
public interface ISiigoClient
{
    Task<IReadOnlyList<SiigoCatalogItem>> DocumentTypesAsync(SiigoCredentials credentials, string type, CancellationToken ct);

    Task<IReadOnlyList<SiigoCatalogItem>> UsersAsync(SiigoCredentials credentials, CancellationToken ct);

    Task<IReadOnlyList<SiigoCatalogItem>> TaxesAsync(SiigoCredentials credentials, CancellationToken ct);

    Task<IReadOnlyList<SiigoCatalogItem>> PaymentTypesAsync(SiigoCredentials credentials, CancellationToken ct);

    /// <summary>Creates the product; true if it was created or already existed with that code.</summary>
    Task<(bool Ok, string? Error)> EnsureProductAsync(SiigoCredentials credentials, string code, string name, long accountGroupId,
        IReadOnlyList<long> taxIds, CancellationToken ct);

    /// <summary><c>POST /v1/invoices</c> with <c>stamp.send = true</c> and <c>Idempotency-Key</c>.</summary>
    Task<SiigoInvoiceResult> CreateInvoiceAsync(SiigoCredentials credentials, SiigoInvoice invoice, string idempotencyKey, CancellationToken ct);

    Task<SiigoInvoiceResult> GetInvoiceAsync(SiigoCredentials credentials, string id, CancellationToken ct);

    /// <summary><c>POST /v1/invoices/{id}/stamp</c>: sends to the DIAN an invoice that was left unsent.</summary>
    Task<SiigoInvoiceResult> StampInvoiceAsync(SiigoCredentials credentials, string id, CancellationToken ct);

    /// <summary><c>GET /v1/invoices/{id}/stamp/errors</c>.</summary>
    Task<string?> StampErrorsAsync(SiigoCredentials credentials, string id, CancellationToken ct);

    /// <summary><c>GET /v1/invoices/{id}/pdf</c>; null if it is not available.</summary>
    Task<byte[]?> InvoicePdfAsync(SiigoCredentials credentials, string id, CancellationToken ct);

    /// <summary><c>POST /v1/credit-notes</c> with <c>stamp.send = true</c> and <c>Idempotency-Key</c>.</summary>
    Task<SiigoInvoiceResult> CreateCreditNoteAsync(SiigoCredentials credentials, SiigoCreditNote creditNote, string idempotencyKey, CancellationToken ct);

    /// <summary><c>GET /v1/credit-notes/{id}</c> (SDK oficial).</summary>
    Task<SiigoInvoiceResult> GetCreditNoteAsync(SiigoCredentials credentials, string id, CancellationToken ct);

    /// <summary><c>GET /v1/credit-notes/{id}/pdf</c> (official SDK); null if it is not available.</summary>
    Task<byte[]?> CreditNotePdfAsync(SiigoCredentials credentials, string id, CancellationToken ct);
}
