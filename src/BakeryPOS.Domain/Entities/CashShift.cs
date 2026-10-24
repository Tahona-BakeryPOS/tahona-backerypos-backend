using BakeryPOS.Domain.Common;
using BakeryPOS.Domain.Enums;
using BakeryPOS.Domain.Events;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: cash_shift / turno_caja
/// </summary>
public class CashShift : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid CashRegisterId { get; set; }
    public Guid OpenedByUserId { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningAmount { get; set; }
    public decimal? ClosingAmount { get; set; }
    public decimal? ExpectedAmount { get; set; }
    public decimal? Difference { get; set; }
    public CashShiftStatus Status { get; set; } = CashShiftStatus.Open;
    public string? Notes { get; set; }

    // Navigation
    public CashRegister CashRegister { get; set; } = null!;
    public AppUser OpenedByUser { get; set; } = null!;
    public AppUser? ClosedByUser { get; set; }
    public ICollection<CashMovement> CashMovements { get; set; } = [];
    public ICollection<Sale> Sales { get; set; } = [];

    public void Open(decimal openingAmount, Guid userId)
    {
        OpeningAmount = openingAmount;
        OpenedByUserId = userId;
        OpenedAt = DateTime.UtcNow;
        Status = CashShiftStatus.Open;

        AddDomainEvent(new CashShiftOpenedEvent(Id, CashRegisterId, userId));
    }

    /// <summary>
    /// Cierra el turno. <paramref name="cashSalesTotal"/> es la suma de pagos en efectivo
    /// de las ventas completadas en este turno (CU-07: efectivo esperado = apertura + ventas en
    /// efectivo + ingresos − retiros); se calcula fuera del dominio (Dapper) y se inyecta aquí.
    /// </summary>
    public void Close(decimal closingAmount, Guid userId, decimal cashSalesTotal, string? notes = null)
    {
        if (Status == CashShiftStatus.Closed)
            throw new InvalidOperationException("El turno ya fue cerrado.");

        ClosingAmount = closingAmount;
        ClosedByUserId = userId;
        ClosedAt = DateTime.UtcNow;
        Notes = notes;
        Status = CashShiftStatus.Closed;

        var totalIncome = CashMovements
            .Where(m => m.Type == CashMovementType.Income)
            .Sum(m => m.Amount);
        var totalExpense = CashMovements
            .Where(m => m.Type == CashMovementType.Expense)
            .Sum(m => m.Amount);

        ExpectedAmount = OpeningAmount + cashSalesTotal + totalIncome - totalExpense;
        Difference = closingAmount - ExpectedAmount;

        AddDomainEvent(new CashShiftClosedEvent(Id, CashRegisterId, userId));
    }
}
