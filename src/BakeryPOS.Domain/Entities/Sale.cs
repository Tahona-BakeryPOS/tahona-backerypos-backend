using BakeryPOS.Domain.Common;
using BakeryPOS.Domain.Enums;
using BakeryPOS.Domain.Events;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: sale / venta
/// UUID v7 generated on device for offline-first idempotency.
/// </summary>
public class Sale : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid CashShiftId { get; set; }
    public Guid BranchId { get; set; }
    public Guid CashRegisterId { get; set; }
    public Guid SoldByUserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? DocumentNumber { get; set; }
    public DateTime SaleDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal Total { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Completed;
    public SyncOrigin Origin { get; set; } = SyncOrigin.Server;
    public string? Notes { get; set; }

    // Navigation
    public CashShift CashShift { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public AppUser SoldByUser { get; set; } = null!;
    public Party? Customer { get; set; }
    public ICollection<SaleLine> Lines { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];

    public void Calculate()
    {
        Subtotal = Lines.Sum(l => l.Subtotal);
        TaxTotal = Lines.Sum(l => l.TaxAmount);
        DiscountTotal = Lines.Sum(l => l.DiscountAmount);
        Total = Subtotal + TaxTotal - DiscountTotal;
    }

    public bool ValidatePayments()
    {
        var totalPaid = Payments.Sum(p => p.Amount);
        return totalPaid >= Total;
    }

    public void Complete()
    {
        Calculate();

        if (!ValidatePayments())
            throw new InvalidOperationException("Los pagos no cubren el total de la venta.");

        Status = SaleStatus.Completed;
        SaleDate = DateTime.UtcNow;

        AddDomainEvent(new SaleCompletedEvent(Id, OrganizationId, BranchId, Total));
    }

    public void Void(string reason)
    {
        Status = SaleStatus.Voided;
        Notes = reason;

        AddDomainEvent(new SaleVoidedEvent(Id, OrganizationId, reason));
    }
}
