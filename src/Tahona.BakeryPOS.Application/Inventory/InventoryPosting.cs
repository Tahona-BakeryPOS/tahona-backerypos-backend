using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Inventory;
using Tahona.BakeryPOS.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Inventory;

/// <summary>
/// What the sale moves in stock: it deducts from the branch's default location with
/// <see cref="InventoryLedger"/> (idempotent per sale line; products that do not track stock are ignored).
/// </summary>
public sealed class InventoryPosting : IInventoryPosting
{
    private readonly IAppDbContext _db;
    private readonly IProductDirectory _products;
    private readonly InventoryLedger _ledger;

    public InventoryPosting(IAppDbContext db, IProductDirectory products, InventoryLedger ledger)
    {
        _db = db;
        _products = products;
        _ledger = ledger;
    }

    public async Task<IReadOnlyList<StockShortage>> ShortagesAsync(Guid branchId, IReadOnlyList<(Guid ProductId, decimal Quantity)> lines, CancellationToken ct)
    {
        var location = await DefaultLocationAsync(branchId, ct);
        var requested = lines.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var products = await _products.FindAsync(requested.Keys.ToList(), ct);
        var blocking = products.Values.Where(p => p.TrackStock && !p.AllowNegative).Select(p => p.Id).ToList();
        if (blocking.Count == 0)
            return [];
        var onHand = location is null ? new Dictionary<Guid, decimal>() : await _db.StockBalances.AsNoTracking()
            .Where(b => b.LocationId == location.Id && blocking.Contains(b.ProductId)).ToDictionaryAsync(b => b.ProductId, b => b.OnHand, ct);
        return blocking
            .Where(id => onHand.GetValueOrDefault(id) < requested[id])
            .Select(id => new StockShortage(id, products[id].Name, onHand.GetValueOrDefault(id), requested[id]))
            .ToList();
    }

    public async Task<Result> PostSaleAsync(Guid branchId, Guid saleId, IReadOnlyList<(Guid LineId, Guid ProductId, decimal Quantity)> lines,
        Guid? authorizedBy, CancellationToken ct)
    {
        if (lines.Count == 0)
            return Result.Success();
        var location = await DefaultLocationAsync(branchId, ct);
        if (location is null)
            return Result.Failure(Error.Validation("La sucursal no tiene ubicación de inventario por defecto."));
        var posted = await _ledger.PostAsync(new StockPosting(location.Id, StockMovementKind.Sale, StockSources.Sale, saleId,
            lines.Select(l => new StockPostingLine(l.ProductId, -l.Quantity, l.LineId)).ToList(), AuthorizedBy: authorizedBy, SkipUntracked: true), ct);
        return posted.IsSuccess ? Result.Success() : Result.Failure(posted.Error);
    }

    public async Task<Result> ReverseSaleAsync(Guid branchId, Guid saleId, IReadOnlyList<(Guid LineId, Guid ProductId, decimal Quantity)> lines,
        CancellationToken ct)
    {
        // What the sale deducted (products that do not track stock never moved).
        var sold = await _db.StockMovements.AsNoTracking()
            .Where(m => m.Kind == StockMovementKind.Sale && m.SourceType == StockSources.Sale && m.SourceId == saleId)
            .ToListAsync(ct);
        if (sold.Count == 0)
            return Result.Success();
        var posted = await _ledger.PostAsync(new StockPosting(sold[0].LocationId, StockMovementKind.VoidReversal, StockSources.Sale, saleId,
            sold.Select(m => new StockPostingLine(m.ProductId, -m.Quantity, m.SourceLineId)).ToList(), Note: "Anulación de venta",
            SkipUntracked: true), ct);
        return posted.IsSuccess ? Result.Success() : Result.Failure(posted.Error);
    }

    public async Task<Result> PostReturnAsync(Guid branchId, Guid returnId, IReadOnlyList<(Guid LineId, Guid ProductId, decimal Quantity)> lines,
        Guid authorizedBy, CancellationToken ct)
    {
        if (lines.Count == 0)
            return Result.Success();
        var location = await DefaultLocationAsync(branchId, ct);
        if (location is null)
            return Result.Failure(Error.Validation("La sucursal no tiene ubicación de inventario por defecto."));
        var posted = await _ledger.PostAsync(new StockPosting(location.Id, StockMovementKind.Return, StockSources.Return, returnId,
            lines.Select(l => new StockPostingLine(l.ProductId, l.Quantity, l.LineId)).ToList(), AuthorizedBy: authorizedBy,
            Note: "Devolución de venta", SkipUntracked: true), ct);
        return posted.IsSuccess ? Result.Success() : Result.Failure(posted.Error);
    }

    private Task<StockLocation?> DefaultLocationAsync(Guid branchId, CancellationToken ct) =>
        _db.StockLocations.AsNoTracking().FirstOrDefaultAsync(l => l.BranchId == branchId && l.IsDefault && l.IsActive, ct);
}
