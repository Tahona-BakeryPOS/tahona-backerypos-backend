using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: price / precio
/// Product price within a price list.
/// </summary>
public class Price : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid ProductId { get; set; }
    public Guid PriceListId { get; set; }
    public decimal UnitPrice { get; set; }

    // Navigation
    public Product Product { get; set; } = null!;
    public PriceList PriceList { get; set; } = null!;
}
