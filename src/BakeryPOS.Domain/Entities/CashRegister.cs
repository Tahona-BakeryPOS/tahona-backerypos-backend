using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: cash_register / caja
/// </summary>
public class CashRegister : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Navigation
    public Branch Branch { get; set; } = null!;
    public ICollection<Device> Devices { get; set; } = [];
    public ICollection<CashShift> CashShifts { get; set; } = [];
}
