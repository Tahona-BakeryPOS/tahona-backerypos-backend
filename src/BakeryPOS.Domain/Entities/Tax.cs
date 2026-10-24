using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: tax / impuesto
/// IVA 19%, IVA 5%, Excluido, etc.
/// </summary>
public class Tax : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public bool IsActive { get; set; } = true;
}
