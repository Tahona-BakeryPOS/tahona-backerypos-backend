namespace Tahona.BakeryPOS.Application.Invoicing.Siigo;

/// <summary>Credenciales en claro de una empresa (solo en memoria, nunca en logs).</summary>
public sealed record SiigoCredentials(Guid CompanyId, string Username, string AccessKey);

public sealed record SiigoCatalogItem(string Id, string Name, string? Code, string? Detail, bool Active);

/// <param name="TaxedPrice">Precio unitario con IVA incluido (D-07); Siigo lo usa en lugar de <c>price</c>.</param>
/// <param name="Discount">Valor del descuento de la línea (por verificar en habilitación si Siigo lo aplica antes o después del IVA).</param>
public sealed record SiigoInvoiceItem(string Code, string Description, decimal Quantity, decimal TaxedPrice, IReadOnlyList<long> TaxIds, decimal Discount = 0);

public sealed record SiigoPayment(long Id, decimal Value);

public sealed record SiigoInvoice(long DocumentTypeId, DateOnly Date, string CustomerIdentification, long SellerId,
    IReadOnlyList<SiigoInvoiceItem> Items, IReadOnlyList<SiigoPayment> Payments, string Observations);

/// <param name="InvoiceId">GUID de la factura en Siigo.</param>
/// <param name="Reason">Motivo DIAN: 1 devolución parcial, 2 anulación de la factura.</param>
public sealed record SiigoCreditNote(long DocumentTypeId, DateOnly Date, string InvoiceId, int Reason, IReadOnlyList<SiigoInvoiceItem> Items,
    IReadOnlyList<SiigoPayment> Payments, string Observations);

public enum SiigoOutcome
{
    /// <summary>El proveedor respondió con el documento (ver <see cref="SiigoInvoiceResult.StampStatus"/>).</summary>
    Ok,
    /// <summary>4xx de validación: el documento no se creó.</summary>
    Invalid,
    /// <summary>Timeout, red o 5xx: no se sabe si se creó.</summary>
    Unknown,
}

/// <param name="StampStatus">Resultado DIAN: <c>Accepted</c>, <c>Rejected</c>, <c>Draft</c> (sin enviar) u otro.</param>
public sealed record SiigoInvoiceResult(SiigoOutcome Outcome, int? HttpStatus, string? Id, string? Prefix, long? Number, string? StampStatus,
    string? Cufe, string? Errors);

/// <summary>
/// Cliente de la API de Siigo (electronic-invoicing.md §9). Se llama siempre fuera de una transacción
/// SQL. El token (24 h) lo guarda en memoria por empresa.
/// </summary>
public interface ISiigoClient
{
    Task<IReadOnlyList<SiigoCatalogItem>> DocumentTypesAsync(SiigoCredentials credentials, string type, CancellationToken ct);

    Task<IReadOnlyList<SiigoCatalogItem>> UsersAsync(SiigoCredentials credentials, CancellationToken ct);

    Task<IReadOnlyList<SiigoCatalogItem>> TaxesAsync(SiigoCredentials credentials, CancellationToken ct);

    Task<IReadOnlyList<SiigoCatalogItem>> PaymentTypesAsync(SiigoCredentials credentials, CancellationToken ct);

    /// <summary>Crea el producto; true si quedó creado o ya existía con ese código.</summary>
    Task<(bool Ok, string? Error)> EnsureProductAsync(SiigoCredentials credentials, string code, string name, long accountGroupId,
        IReadOnlyList<long> taxIds, CancellationToken ct);

    /// <summary><c>POST /v1/invoices</c> con <c>stamp.send = true</c> e <c>Idempotency-Key</c>.</summary>
    Task<SiigoInvoiceResult> CreateInvoiceAsync(SiigoCredentials credentials, SiigoInvoice invoice, string idempotencyKey, CancellationToken ct);

    Task<SiigoInvoiceResult> GetInvoiceAsync(SiigoCredentials credentials, string id, CancellationToken ct);

    /// <summary><c>POST /v1/invoices/{id}/stamp</c>: envía a la DIAN una factura que quedó sin enviar.</summary>
    Task<SiigoInvoiceResult> StampInvoiceAsync(SiigoCredentials credentials, string id, CancellationToken ct);

    /// <summary><c>GET /v1/invoices/{id}/stamp/errors</c>.</summary>
    Task<string?> StampErrorsAsync(SiigoCredentials credentials, string id, CancellationToken ct);

    /// <summary><c>GET /v1/invoices/{id}/pdf</c>; null si no está disponible.</summary>
    Task<byte[]?> InvoicePdfAsync(SiigoCredentials credentials, string id, CancellationToken ct);

    /// <summary><c>POST /v1/credit-notes</c> con <c>stamp.send = true</c> e <c>Idempotency-Key</c>.</summary>
    Task<SiigoInvoiceResult> CreateCreditNoteAsync(SiigoCredentials credentials, SiigoCreditNote creditNote, string idempotencyKey, CancellationToken ct);

    /// <summary><c>GET /v1/credit-notes/{id}</c> (SDK oficial).</summary>
    Task<SiigoInvoiceResult> GetCreditNoteAsync(SiigoCredentials credentials, string id, CancellationToken ct);

    /// <summary><c>GET /v1/credit-notes/{id}/pdf</c> (SDK oficial); null si no está disponible.</summary>
    Task<byte[]?> CreditNotePdfAsync(SiigoCredentials credentials, string id, CancellationToken ct);
}
