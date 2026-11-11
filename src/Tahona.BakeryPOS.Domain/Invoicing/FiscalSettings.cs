using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Invoicing;

/// <summary>
/// The company's electronic invoicing configuration with Siigo. The API user and access key are
/// stored encrypted (infrastructure encrypts them). The ids are those of Siigo's catalogs.
/// </summary>
public sealed class FiscalSettings : AuditableEntity, ITenantEntity
{
    public const string DefaultConsumerFinal = "222222222222";
    public const int DefaultRetentionYears = 5;

    public Guid CompanyId { get; private set; }
    public string Provider { get; private set; } = FiscalDocument.Siigo;
    public string EncryptedUsername { get; private set; } = string.Empty;
    public string EncryptedAccessKey { get; private set; } = string.Empty;
    /// <summary>Electronic FV document type in Siigo (<c>/v1/document-types?type=FV</c>).</summary>
    public long? InvoiceDocumentTypeId { get; private set; }
    /// <summary>Electronic NC document type (credit notes, phase 9).</summary>
    public long? CreditNoteDocumentTypeId { get; private set; }
    /// <summary>Seller in Siigo (<c>/v1/users</c>).</summary>
    public long? SellerId { get; private set; }
    /// <summary>Inventory group with which products are created in Siigo.</summary>
    public long? ProductAccountGroupId { get; private set; }
    public string ConsumerFinalIdentification { get; private set; } = DefaultConsumerFinal;
    /// <summary>Years the documents are kept (D-15).</summary>
    public int RetentionYears { get; private set; } = DefaultRetentionYears;
    public bool IsActive { get; private set; }

    public bool IsComplete => EncryptedUsername.Length > 0 && EncryptedAccessKey.Length > 0 && InvoiceDocumentTypeId is not null
        && SellerId is not null && ProductAccountGroupId is not null;

    private FiscalSettings() { }

    public static FiscalSettings Create(Guid companyId) => new() { CompanyId = companyId };

    public void SetCredentials(string encryptedUsername, string encryptedAccessKey)
    {
        if (string.IsNullOrWhiteSpace(encryptedUsername) || string.IsNullOrWhiteSpace(encryptedAccessKey))
            throw new DomainException("VALIDACION", "El usuario y la llave de acceso de Siigo son obligatorios.");
        EncryptedUsername = encryptedUsername;
        EncryptedAccessKey = encryptedAccessKey;
    }

    public void Configure(long? invoiceDocumentTypeId, long? creditNoteDocumentTypeId, long? sellerId, long? productAccountGroupId,
        string consumerFinalIdentification, int retentionYears)
    {
        if (string.IsNullOrWhiteSpace(consumerFinalIdentification))
            throw new DomainException("VALIDACION", "La identificación del consumidor final es obligatoria.");
        if (retentionYears < 1)
            throw new DomainException("VALIDACION", "La conservación debe ser de al menos 1 año.");
        InvoiceDocumentTypeId = invoiceDocumentTypeId;
        CreditNoteDocumentTypeId = creditNoteDocumentTypeId;
        SellerId = sellerId;
        ProductAccountGroupId = productAccountGroupId;
        ConsumerFinalIdentification = consumerFinalIdentification.Trim();
        RetentionYears = retentionYears;
    }

    public void SetActive(bool active)
    {
        if (active && !IsComplete)
            throw new DomainException("VALIDACION", "Completa credenciales, tipo de comprobante, vendedor y grupo de inventario antes de activar.");
        IsActive = active;
    }
}

public enum FiscalMappingKind
{
    Tax,
    PaymentMethod,
    /// <summary>Product already created in Siigo with its code (SKU).</summary>
    Product,
}

/// <summary>Pairing of an own datum with its counterpart in Siigo (tax, payment method, product).</summary>
public sealed class FiscalMapping : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public FiscalMappingKind Kind { get; private set; }
    public Guid LocalId { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;

    private FiscalMapping() { }

    public static FiscalMapping Create(Guid companyId, FiscalMappingKind kind, Guid localId, string externalId)
    {
        var mapping = new FiscalMapping { CompanyId = companyId, Kind = kind, LocalId = localId };
        mapping.Set(externalId);
        return mapping;
    }

    public void Set(string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            throw new DomainException("VALIDACION", "El id en Siigo es obligatorio.");
        ExternalId = externalId.Trim();
    }
}
