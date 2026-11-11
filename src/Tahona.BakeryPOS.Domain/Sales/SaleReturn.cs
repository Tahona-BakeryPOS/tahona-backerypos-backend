using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Sales;

/// <summary>
/// Full or partial return of a confirmed sale (US-9.2). It is authorized on the spot by whoever holds
/// <c>returns.approve</c> with their PIN and completed in the same transaction: cash refund from the
/// open register, restocking of the marked lines and a credit note if the sale has an invoice.
/// </summary>
public sealed class SaleReturn : AuditableEntity, ITenantEntity
{
    private readonly List<SaleReturnLine> _lines = [];

    public Guid CompanyId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid BranchId { get; private set; }
    /// <summary>Cash session the returned cash comes out of.</summary>
    public Guid CashSessionId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid ApprovedBy { get; private set; }
    public decimal Total { get; private set; }
    public Guid? FiscalDocumentId { get; private set; }
    public DateTime OccurredAt { get; private set; }

    public IReadOnlyList<SaleReturnLine> Lines => _lines.AsReadOnly();

    private SaleReturn() { }

    public static SaleReturn Create(Sale sale, Guid cashSessionId, string reason, Guid approvedBy, DateTime at)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("VALIDATION", "La devolución exige motivo.");
        return new SaleReturn
        {
            CompanyId = sale.CompanyId, SaleId = sale.Id, BranchId = sale.BranchId, CashSessionId = cashSessionId,
            Reason = reason.Trim(), ApprovedBy = approvedBy, OccurredAt = at,
        };
    }

    /// <param name="alreadyReturned">Quantity and value already returned from that line in earlier returns.</param>
    /// <param name="restock">The product goes back on sale (it generates an inventory movement).</param>
    public SaleReturnLine AddLine(SaleLine line, decimal quantity, (decimal Quantity, decimal Amount) alreadyReturned, bool restock)
    {
        if (quantity <= 0 || decimal.Round(quantity, 2) != quantity)
            throw new DomainException("VALIDATION", "La cantidad devuelta debe ser mayor que 0 con máximo 2 decimales.");
        if (_lines.Any(l => l.SaleLineId == line.Id))
            throw new DomainException("VALIDATION", "Hay líneas repetidas en la devolución.");
        var available = line.Quantity - alreadyReturned.Quantity;
        if (quantity > available)
            throw new DomainException("VALIDATION", $"{line.Description}: se devuelven máximo {available:0.##}.");
        // Value proportional to the line total; the last one returns the exact remainder (no rounding mismatches).
        var amount = quantity == available
            ? line.LineTotal - alreadyReturned.Amount
            : decimal.Round(line.LineTotal * quantity / line.Quantity, 2);
        var returnLine = new SaleReturnLine
        {
            CompanyId = CompanyId, SaleReturnId = Id, SaleLineId = line.Id, ProductId = line.ProductId, Quantity = quantity,
            Amount = amount, Restock = restock,
        };
        _lines.Add(returnLine);
        Total = _lines.Sum(l => l.Amount);
        return returnLine;
    }

    public void SetFiscalDocument(Guid? id) => FiscalDocumentId = id;
}

public sealed class SaleReturnLine : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; internal set; }
    public Guid SaleReturnId { get; internal set; }
    public Guid SaleLineId { get; internal set; }
    public Guid ProductId { get; internal set; }
    public decimal Quantity { get; internal set; }
    /// <summary>Money returned for the line (with tax).</summary>
    public decimal Amount { get; internal set; }
    public bool Restock { get; internal set; }

    internal SaleReturnLine() { }
}
