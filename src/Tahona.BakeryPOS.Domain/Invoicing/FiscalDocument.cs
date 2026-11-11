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
    /// <summary>Created with the sale; waiting to be sent (outbox).</summary>
    Pending,
    Submitting,
    /// <summary>Accepted by the DIAN with a CUFE: only with a verifiable response from the provider.</summary>
    Accepted,
    /// <summary>Rejected (DIAN or provider validation): not retried automatically.</summary>
    Rejected,
    /// <summary>Transient failure before knowing anything (configuration, network): it is retried.</summary>
    TemporaryError,
    /// <summary>It is unknown whether the provider received it: it is queried before resending, with the same key.</summary>
    Unknown,
    Cancelled,
}

/// <summary>
/// Electronic fiscal document (invoice or credit note) of a sale. Its status is its own. It is never marked
/// accepted without a verifiable response from the provider; an unknown result is resent with the same
/// <see cref="IdempotencyKey"/>, which in Siigo returns the document already created (electronic-invoicing.md §9).
/// </summary>
public sealed class FiscalDocument : AuditableEntity, ITenantEntity
{
    public const string Siigo = "siigo";

    public Guid CompanyId { get; private set; }
    public Guid SaleId { get; private set; }
    public FiscalDocumentKind Kind { get; private set; }
    public string Provider { get; private set; } = Siigo;
    public FiscalDocumentStatus Status { get; private set; } = FiscalDocumentStatus.Pending;
    /// <summary>Provider idempotency key (the document id in base 36, 25 characters).</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;
    /// <summary>What is invoiced (lines, payments, customer), fixed when the document is created.</summary>
    public string Payload { get; private set; } = string.Empty;
    public decimal Total { get; private set; }
    public string CustomerIdentification { get; private set; } = string.Empty;
    /// <summary>Id of the document at the provider (Siigo GUID).</summary>
    public string? ExternalId { get; private set; }
    public string? Prefix { get; private set; }
    public long? Number { get; private set; }
    public string? Cufe { get; private set; }
    public string? Errors { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    /// <summary>Until when it is kept (D-15).</summary>
    public DateTime? RetainUntil { get; private set; }
    /// <summary>Credit note: the invoice it corrects.</summary>
    public Guid? InvoiceDocumentId { get; private set; }
    /// <summary>Credit note: DIAN reason (1 = partial return, 2 = invoice void).</summary>
    public int? CreditReason { get; private set; }
    /// <summary>Credit note: void (sale id) or return (return id). One note per source.</summary>
    public Guid? SourceId { get; private set; }

    public bool IsFinal => Status is FiscalDocumentStatus.Accepted or FiscalDocumentStatus.Cancelled;

    private FiscalDocument() { }

    public static FiscalDocument Create(Guid companyId, Guid saleId, FiscalDocumentKind kind, string payload, decimal total, string customerIdentification)
    {
        if (companyId == Guid.Empty || saleId == Guid.Empty)
            throw new DomainException("VALIDATION", "El documento fiscal necesita empresa y venta.");
        if (total <= 0)
            throw new DomainException("VALIDATION", "El total del documento debe ser mayor que 0.");
        if (string.IsNullOrWhiteSpace(customerIdentification))
            throw new DomainException("VALIDATION", "El documento necesita la identificación del adquirente (o la del consumidor final).");
        var doc = new FiscalDocument
        {
            CompanyId = companyId, SaleId = saleId, Kind = kind, Payload = payload, Total = total,
            CustomerIdentification = customerIdentification.Trim(),
        };
        doc.IdempotencyKey = Base36(doc.Id);
        return doc;
    }

    /// <summary>Credit note against an invoice of the same sale.</summary>
    public static FiscalDocument CreateCreditNote(FiscalDocument invoice, Guid sourceId, int reason, string payload, decimal total)
    {
        if (invoice.Kind != FiscalDocumentKind.Invoice)
            throw new DomainException("VALIDATION", "La nota crédito se emite sobre una factura.");
        if (reason is not (1 or 2))
            throw new DomainException("VALIDATION", "Motivo de nota crédito: 1 (devolución) o 2 (anulación).");
        var doc = Create(invoice.CompanyId, invoice.SaleId, FiscalDocumentKind.CreditNote, payload, total, invoice.CustomerIdentification);
        doc.InvoiceDocumentId = invoice.Id;
        doc.CreditReason = reason;
        doc.SourceId = sourceId;
        return doc;
    }

    /// <summary>
    /// Without fiscal effect: a rejected invoice of a voided sale, or a credit note of an invoice that was never
    /// valid. An accepted document is not cancelled (that requires a credit note).
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == FiscalDocumentStatus.Accepted)
            throw new DomainException("INVALID_STATE", "Un documento aceptado por la DIAN no se cancela: requiere nota crédito.");
        Status = FiscalDocumentStatus.Cancelled;
        Errors = Truncate(reason);
    }

    /// <summary>Starts a submission attempt. Only from non-final and non-rejected states.</summary>
    public void StartSubmission()
    {
        if (IsFinal || Status == FiscalDocumentStatus.Rejected)
            throw new DomainException("INVALID_STATE", $"El documento está {Status}: no se envía.");
        Status = FiscalDocumentStatus.Submitting;
        Attempts++;
    }

    /// <summary>The provider returned the created document (with or without a DIAN result yet).</summary>
    public void RecordCreated(string externalId, string? prefix, long? number)
    {
        ExternalId = externalId;
        Prefix = prefix;
        Number = number;
    }

    public void Accept(string cufe, DateTime at, int retentionYears)
    {
        if (string.IsNullOrWhiteSpace(cufe))
            throw new DomainException("VALIDATION", "No se acepta un documento sin CUFE/CUDE.");
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

    /// <summary>Manual retry of a rejected one after fixing the cause (configuration or data).</summary>
    public void Retry()
    {
        if (Status is not (FiscalDocumentStatus.Rejected or FiscalDocumentStatus.TemporaryError or FiscalDocumentStatus.Unknown))
            throw new DomainException("INVALID_STATE", $"Solo se reintenta un documento rechazado, con error o desconocido (está {Status}).");
        Status = FiscalDocumentStatus.Pending;
    }

    private static string Truncate(string text) => text.Length > 2000 ? text[..2000] : text;

    /// <summary>GUID in base 36: 25 alphanumeric characters (Siigo accepts up to 30 in Idempotency-Key).</summary>
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

/// <summary>Record of each submission attempt (requirement: do not resend an unknown one without a recorded query).</summary>
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
