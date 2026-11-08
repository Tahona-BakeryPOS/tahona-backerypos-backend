using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Invoicing;

public enum FiscalDocumentKind
{
    Invoice,
    CreditNote,
}

public enum FiscalDocumentStatus
{
    /// <summary>Creado con la venta; espera su envío (outbox).</summary>
    Pending,
    Submitting,
    /// <summary>Aceptado por la DIAN con CUFE: solo con respuesta verificable del proveedor.</summary>
    Accepted,
    /// <summary>Rechazado (DIAN o validación del proveedor): no se reintenta solo.</summary>
    Rejected,
    /// <summary>Falla transitoria antes de saber nada (configuración, red): se reintenta.</summary>
    TemporaryError,
    /// <summary>No se sabe si el proveedor lo recibió: se consulta antes de reenviar, con la misma clave.</summary>
    Unknown,
    Cancelled,
}

/// <summary>
/// Documento fiscal electrónico (factura o nota crédito) de una venta. Su estado es propio. Nunca se marca
/// aceptado sin respuesta verificable del proveedor; un resultado desconocido se reenvía con la misma
/// <see cref="IdempotencyKey"/>, que en Siigo devuelve el documento ya creado (electronic-invoicing.md §9).
/// </summary>
public sealed class FiscalDocument : AuditableEntity, ITenantEntity
{
    public const string Siigo = "siigo";

    public Guid CompanyId { get; private set; }
    public Guid SaleId { get; private set; }
    public FiscalDocumentKind Kind { get; private set; }
    public string Provider { get; private set; } = Siigo;
    public FiscalDocumentStatus Status { get; private set; } = FiscalDocumentStatus.Pending;
    /// <summary>Clave de idempotencia del proveedor (id del documento en base 36, 25 caracteres).</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;
    /// <summary>Lo que se factura (líneas, pagos, cliente), fijado al crear el documento.</summary>
    public string Payload { get; private set; } = string.Empty;
    public decimal Total { get; private set; }
    public string CustomerIdentification { get; private set; } = string.Empty;
    /// <summary>Id del documento en el proveedor (GUID de Siigo).</summary>
    public string? ExternalId { get; private set; }
    public string? Prefix { get; private set; }
    public long? Number { get; private set; }
    public string? Cufe { get; private set; }
    public string? Errors { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    /// <summary>Hasta cuándo se conserva (D-15).</summary>
    public DateTime? RetainUntil { get; private set; }
    /// <summary>Nota crédito: factura que corrige.</summary>
    public Guid? InvoiceDocumentId { get; private set; }
    /// <summary>Nota crédito: motivo DIAN (1 = devolución parcial, 2 = anulación de la factura).</summary>
    public int? CreditReason { get; private set; }
    /// <summary>Nota crédito: anulación (id de la venta) o devolución (id de la devolución). Una nota por origen.</summary>
    public Guid? SourceId { get; private set; }

    public bool IsFinal => Status is FiscalDocumentStatus.Accepted or FiscalDocumentStatus.Cancelled;

    private FiscalDocument() { }

    public static FiscalDocument Create(Guid companyId, Guid saleId, FiscalDocumentKind kind, string payload, decimal total, string customerIdentification)
    {
        if (companyId == Guid.Empty || saleId == Guid.Empty)
            throw new DomainException("VALIDACION", "El documento fiscal necesita empresa y venta.");
        if (total <= 0)
            throw new DomainException("VALIDACION", "El total del documento debe ser mayor que 0.");
        if (string.IsNullOrWhiteSpace(customerIdentification))
            throw new DomainException("VALIDACION", "El documento necesita la identificación del adquirente (o la del consumidor final).");
        var doc = new FiscalDocument
        {
            CompanyId = companyId, SaleId = saleId, Kind = kind, Payload = payload, Total = total,
            CustomerIdentification = customerIdentification.Trim(),
        };
        doc.IdempotencyKey = Base36(doc.Id);
        return doc;
    }

    /// <summary>Nota crédito sobre una factura de la misma venta.</summary>
    public static FiscalDocument CreateCreditNote(FiscalDocument invoice, Guid sourceId, int reason, string payload, decimal total)
    {
        if (invoice.Kind != FiscalDocumentKind.Invoice)
            throw new DomainException("VALIDACION", "La nota crédito se emite sobre una factura.");
        if (reason is not (1 or 2))
            throw new DomainException("VALIDACION", "Motivo de nota crédito: 1 (devolución) o 2 (anulación).");
        var doc = Create(invoice.CompanyId, invoice.SaleId, FiscalDocumentKind.CreditNote, payload, total, invoice.CustomerIdentification);
        doc.InvoiceDocumentId = invoice.Id;
        doc.CreditReason = reason;
        doc.SourceId = sourceId;
        return doc;
    }

    /// <summary>
    /// Sin efecto fiscal: factura rechazada de una venta anulada, o nota crédito de una factura que nunca fue
    /// válida. Un documento aceptado no se cancela (eso exige nota crédito).
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == FiscalDocumentStatus.Accepted)
            throw new DomainException("ESTADO_INVALIDO", "Un documento aceptado por la DIAN no se cancela: requiere nota crédito.");
        Status = FiscalDocumentStatus.Cancelled;
        Errors = Truncate(reason);
    }

    /// <summary>Comienza un intento de envío. Solo desde estados no finales y no rechazados.</summary>
    public void StartSubmission()
    {
        if (IsFinal || Status == FiscalDocumentStatus.Rejected)
            throw new DomainException("ESTADO_INVALIDO", $"El documento está {Status}: no se envía.");
        Status = FiscalDocumentStatus.Submitting;
        Attempts++;
    }

    /// <summary>El proveedor devolvió el documento creado (aún sin resultado DIAN o con él).</summary>
    public void RecordCreated(string externalId, string? prefix, long? number)
    {
        ExternalId = externalId;
        Prefix = prefix;
        Number = number;
    }

    public void Accept(string cufe, DateTime at, int retentionYears)
    {
        if (string.IsNullOrWhiteSpace(cufe))
            throw new DomainException("VALIDACION", "No se acepta un documento sin CUFE/CUDE.");
        Status = FiscalDocumentStatus.Accepted;
        Cufe = cufe;
        Errors = null;
        AcceptedAt = at;
        RetainUntil = at.AddYears(retentionYears);
    }

    public void Reject(string errors)
    {
        Status = FiscalDocumentStatus.Rejected;
        Errors = Truncate(errors);
    }

    public void MarkUnknown(string detail)
    {
        Status = FiscalDocumentStatus.Unknown;
        Errors = Truncate(detail);
    }

    public void MarkTemporaryError(string detail)
    {
        Status = FiscalDocumentStatus.TemporaryError;
        Errors = Truncate(detail);
    }

    /// <summary>Reintento manual de un rechazado tras corregir la causa (configuración o datos).</summary>
    public void Retry()
    {
        if (Status is not (FiscalDocumentStatus.Rejected or FiscalDocumentStatus.TemporaryError or FiscalDocumentStatus.Unknown))
            throw new DomainException("ESTADO_INVALIDO", $"Solo se reintenta un documento rechazado, con error o desconocido (está {Status}).");
        Status = FiscalDocumentStatus.Pending;
    }

    private static string Truncate(string text) => text.Length > 2000 ? text[..2000] : text;

    /// <summary>GUID en base 36: 25 caracteres alfanuméricos (Siigo admite hasta 30 en Idempotency-Key).</summary>
    public static string Base36(Guid id)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var value = new System.Numerics.BigInteger([.. id.ToByteArray(), 0]);
        var chars = new char[25];
        for (var i = 24; i >= 0; i--)
        {
            chars[i] = digits[(int)(value % 36)];
            value /= 36;
        }
        return new string(chars);
    }
}

/// <summary>Registro de cada intento de envío (requisito: no reenviar un desconocido sin consulta registrada).</summary>
public sealed class FiscalSubmissionAttempt : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid FiscalDocumentId { get; private set; }
    public int Number { get; private set; }
    /// <summary><c>create</c>, <c>status</c> o <c>stamp</c>.</summary>
    public string Operation { get; private set; } = string.Empty;
    /// <summary><c>accepted</c>, <c>rejected</c>, <c>draft</c>, <c>error</c> o <c>unknown</c>.</summary>
    public string Outcome { get; private set; } = string.Empty;
    public int? HttpStatus { get; private set; }
    public string? Detail { get; private set; }
    public DateTime OccurredAt { get; private set; }

    private FiscalSubmissionAttempt() { }

    public static FiscalSubmissionAttempt Record(FiscalDocument doc, string operation, string outcome, int? httpStatus, string? detail, DateTime at) => new()
    {
        CompanyId = doc.CompanyId, FiscalDocumentId = doc.Id, Number = doc.Attempts, Operation = operation, Outcome = outcome,
        HttpStatus = httpStatus, Detail = detail is { Length: > 1000 } ? detail[..1000] : detail, OccurredAt = at,
    };
}
