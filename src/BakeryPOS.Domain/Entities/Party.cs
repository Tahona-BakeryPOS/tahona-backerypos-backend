using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: party / tercero
/// Customers, suppliers, etc.
/// </summary>
public class Party : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid? IdentityDocumentTypeId { get; set; }
    public string? DocumentNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsCustomer { get; set; } = true;
    public bool IsSupplier { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public IdentityDocumentType? IdentityDocumentType { get; set; }
}
