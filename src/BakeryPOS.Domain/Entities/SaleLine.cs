using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: sale_line / venta_linea
/// </summary>
public class SaleLine : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid SaleId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal LineTotal { get; set; }
    public int LineNumber { get; set; }

    // Navigation
    public Sale Sale { get; set; } = null!;
    public Product Product { get; set; } = null!;

    public void Calculate()
    {
        Subtotal = Math.Round(Quantity * UnitPrice, 2);
        DiscountAmount = Math.Round(Subtotal * (DiscountPercent / 100m), 2);
        var baseAfterDiscount = Subtotal - DiscountAmount;
        TaxAmount = Math.Round(baseAfterDiscount * (TaxRate / 100m), 2);
        LineTotal = baseAfterDiscount + TaxAmount;
    }
}
