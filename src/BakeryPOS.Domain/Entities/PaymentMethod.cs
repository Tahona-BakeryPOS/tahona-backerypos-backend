using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: payment_method / medio_pago
/// Efectivo, Tarjeta (Bold), Nequi, Daviplata, etc.
/// </summary>
public class PaymentMethod : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool RequiresReference { get; set; }
    public bool IsActive { get; set; } = true;
}
