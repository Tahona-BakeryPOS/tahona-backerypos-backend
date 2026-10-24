using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: device / dispositivo
/// Physical device (tablet, phone, PC) linked to a cash register.
/// </summary>
public class Device : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid CashRegisterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DeviceIdentifier { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public CashRegister CashRegister { get; set; } = null!;
}
