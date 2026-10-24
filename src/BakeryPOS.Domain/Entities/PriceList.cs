using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: price_list / lista_precio
/// General, Mayorista, Empleados, etc.
/// </summary>
public class PriceList : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Price> Prices { get; set; } = [];
}
