using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: payment / pago
/// </summary>
public class Payment : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid SaleId { get; set; }
    public Guid PaymentMethodId { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public string? BoldTransactionId { get; set; }

    // Navigation
    public Sale Sale { get; set; } = null!;
    public PaymentMethod PaymentMethod { get; set; } = null!;
}
