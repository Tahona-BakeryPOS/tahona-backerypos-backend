using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: product / producto
/// </summary>
public class Product : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid CategoryId { get; set; }
    public Guid? TaxId { get; set; }
    public Guid? UnitOfMeasureId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool TrackInventory { get; set; } = false;

    // Navigation
    public ProductCategory Category { get; set; } = null!;
    public Tax? Tax { get; set; }
    public UnitOfMeasure? UnitOfMeasure { get; set; }
    public ICollection<ProductBarcode> Barcodes { get; set; } = [];
    public ICollection<Price> Prices { get; set; } = [];
}
