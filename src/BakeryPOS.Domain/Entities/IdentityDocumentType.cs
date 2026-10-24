using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: identity_document_type / tipo_documento_identidad
/// CC, NIT, CE, TI, PA, etc.
/// </summary>
public class IdentityDocumentType : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
