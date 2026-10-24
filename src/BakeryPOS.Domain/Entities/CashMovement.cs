using BakeryPOS.Domain.Common;
using BakeryPOS.Domain.Enums;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: cash_movement / movimiento_caja
/// </summary>
public class CashMovement : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid CashShiftId { get; set; }
    public Guid? OperationReasonId { get; set; }
    public CashMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public CashShift CashShift { get; set; } = null!;
    public OperationReason? OperationReason { get; set; }
}
