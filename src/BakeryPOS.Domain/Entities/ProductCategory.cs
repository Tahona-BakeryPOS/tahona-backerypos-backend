using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: product_category / categoria_producto
/// Panes, Pasteles, Bebidas, etc.
/// </summary>
public class ProductCategory : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Product> Products { get; set; } = [];
}
