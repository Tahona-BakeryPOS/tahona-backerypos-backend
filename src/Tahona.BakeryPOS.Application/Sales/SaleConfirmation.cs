using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Sales;
using Tahona.BakeryPOS.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Tahona.BakeryPOS.Application.Sales;

/// <summary>
/// Step [4] of architecture.md §4: when the approved payments cover the total, in a single transaction the
/// sale is confirmed with its number, stock is deducted, cash goes into the drawer and the electronic
/// invoice is created (with its outbox message). Idempotent: a confirmed sale is not repeated.
/// </summary>
public sealed class SaleConfirmation
{
    private readonly IAppDbContext _db;
    private readonly ISalePayments _payments;
    private readonly IInventoryPosting _inventory;
    private readonly ICashDrawer _drawer;
    private readonly IFiscalDocumentIssuer _fiscal;
    private readonly TimeProvider _clock;
    private readonly ILogger<SaleConfirmation> _logger;

    public SaleConfirmation(IAppDbContext db, ISalePayments payments, IInventoryPosting inventory, ICashDrawer drawer,
        IFiscalDocumentIssuer fiscal, TimeProvider clock, ILogger<SaleConfirmation> logger)
    {
        _db = db;
        _payments = payments;
        _inventory = inventory;
        _drawer = drawer;
        _fiscal = fiscal;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Confirms if it is already paid; if payments are missing, it only updates the payment status. It does not save if there are no changes.</summary>
    public async Task<Result> TryConfirmAsync(Sale sale, CancellationToken ct)
    {
        if (!sale.IsOpen)
            return Result.Success();
        var payments = await _payments.ForSaleAsync(sale.Id, ct);
        var approved = payments.Where(p => p.Status == "approved").ToList();
        var approvedTotal = approved.Sum(p => p.Amount);
        if (approvedTotal < sale.Total || payments.Any(p => p.Status == "pending"))
        {
            sale.MarkPendingPayment(approvedTotal);
            await _db.SaveChangesAsync(ct);
            return Result.Success();
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var counter = await _db.SaleCounters.FirstOrDefaultAsync(ct);
        if (counter is null)
            _db.SaleCounters.Add(counter = SaleCounter.Create(sale.CompanyId));
        if (!sale.Confirm(approvedTotal, counter.Next(), now))
            return Result.Success();

        // Inventory: the D-06 policy was checked before charging. If a race left a "block" product
        // without stock and the payment is already approved, it is deducted anyway in the cashier's name and recorded.
        var lines = sale.Lines.Select(l => (l.Id, l.ProductId, l.Quantity)).ToList();
        var posted = await _inventory.PostSaleAsync(sale.BranchId, sale.Id, lines, sale.StockAuthorizedBy, ct);
        if (posted.IsFailure && posted.Error.Code == Error.ExistenciasInsuficientes.Code)
        {
            _logger.LogWarning("Venta {SaleId} pagada sin existencias suficientes: se descuenta igual ({Detail})", sale.Id, posted.Error.Message);
            posted = await _inventory.PostSaleAsync(sale.BranchId, sale.Id, lines, sale.StockAuthorizedBy ?? sale.CashierId, ct);
        }
        if (posted.IsFailure)
            return posted;
        sale.MarkInventoryApplied();

        var cash = approved.Where(p => p.MethodKind == "cash").ToList();
        await _drawer.RecordSaleCashAsync(sale.CashSessionId, sale.Id, cash.Sum(p => p.AmountTendered ?? p.Amount), cash.Sum(p => p.ChangeAmount ?? 0), ct);

        // D-17: electronic invoicing is optional per company. Without active invoicing the sale stays "not required" (I-25).
        if (await _fiscal.IsActiveAsync(ct))
        {
            var docId = await _fiscal.EnqueueSaleInvoiceAsync(new SaleFiscalSnapshot(sale.Id, now, sale.CustomerIdentification,
                sale.Lines.Select(l => new SaleFiscalLine(l.ProductId, l.Sku, l.Description, l.Quantity, l.UnitPriceWithTax, l.TaxId,
                    l.PriceIncludesTax ? l.DiscountAmount : decimal.Round(l.DiscountAmount * (1 + l.TaxRate / 100m), 2))).ToList(),
                approved.Select(p => new SaleFiscalPayment(p.PaymentMethodId, p.Amount)).ToList(), sale.Total), ct);
            sale.SetFiscal(SaleFiscalStatus.Pending, docId);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
