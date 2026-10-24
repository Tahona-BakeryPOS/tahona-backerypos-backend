using BakeryPOS.Domain.Common;
using BakeryPOS.Domain.Enums;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: operation_reason / motivo_operacion
/// Reasons for cash movements (opening, withdrawals, deposits, voids).
/// </summary>
public class OperationReason : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public CashMovementType MovementType { get; set; }
    public bool IsActive { get; set; } = true;
}
