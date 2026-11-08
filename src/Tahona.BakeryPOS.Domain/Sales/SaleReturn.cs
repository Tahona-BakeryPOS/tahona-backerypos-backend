using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Sales;

/// <summary>
/// Devolución total o parcial de una venta confirmada (HU-9.2). La autoriza en el momento quien tiene
/// <c>devoluciones.aprobar</c> con su PIN y se completa en la misma transacción: reembolso en efectivo de la
/// caja abierta, reingreso al inventario de las líneas marcadas y nota crédito si la venta tiene factura.
/// </summary>
public sealed class SaleReturn : AuditableEntity, ITenantEntity
{
    private readonly List<SaleReturnLine> _lines = [];

    public Guid CompanyId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid BranchId { get; private set; }
    /// <summary>Sesión de caja de donde sale el efectivo devuelto.</summary>
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
            throw new DomainException("VALIDACION", "La devolución exige motivo.");
        return new SaleReturn
        {
            CompanyId = sale.CompanyId, SaleId = sale.Id, BranchId = sale.BranchId, CashSessionId = cashSessionId,
            Reason = reason.Trim(), ApprovedBy = approvedBy, OccurredAt = at,
        };
    }

    /// <param name="alreadyReturned">Cantidad y valor ya devueltos de esa línea en devoluciones anteriores.</param>
    /// <param name="restock">El producto vuelve a la venta (genera movimiento de inventario).</param>
    public SaleReturnLine AddLine(SaleLine line, decimal quantity, (decimal Quantity, decimal Amount) alreadyReturned, bool restock)
    {
        if (quantity <= 0 || decimal.Round(quantity, 2) != quantity)
            throw new DomainException("VALIDACION", "La cantidad devuelta debe ser mayor que 0 con máximo 2 decimales.");
        if (_lines.Any(l => l.SaleLineId == line.Id))
            throw new DomainException("VALIDACION", "Hay líneas repetidas en la devolución.");
        var available = line.Quantity - alreadyReturned.Quantity;
        if (quantity > available)
            throw new DomainException("VALIDACION", $"{line.Description}: se devuelven máximo {available:0.##}.");
        // Valor proporcional al total de la línea; lo último que queda devuelve el resto exacto (sin descuadres de redondeo).
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
    /// <summary>Dinero devuelto por la línea (con impuesto).</summary>
    public decimal Amount { get; internal set; }
    public bool Restock { get; internal set; }

    internal SaleReturnLine() { }
}
