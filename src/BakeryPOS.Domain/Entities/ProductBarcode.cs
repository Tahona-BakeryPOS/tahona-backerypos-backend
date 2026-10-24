using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: product_barcode / producto_codigo_barras
/// Multiple barcodes per product.
/// </summary>
public class ProductBarcode : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid ProductId { get; set; }
    public string Barcode { get; set; } = string.Empty;

    // Navigation
    public Product Product { get; set; } = null!;
}
