using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Catalog;

/// <summary>What other modules can query from products. Respects the company filter.</summary>
public sealed class ProductDirectory : IProductDirectory
{
    private readonly IAppDbContext _db;

    public ProductDirectory(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyDictionary<Guid, StockProductInfo>> FindAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        var rows = await Project(_db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))).ToListAsync(ct);
        return rows.ToDictionary(p => p.Id);
    }

    public async Task<IReadOnlyDictionary<Guid, SaleProductInfo>> PricedAsync(IReadOnlyCollection<Guid> productIds, Guid? branchId, DateTime at, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        var rows = await (from p in _db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
                          join u in _db.Units on p.UnitId equals u.Id
                          join t in _db.Taxes on p.TaxId equals t.Id
                          select new { p.Id, p.Sku, p.Name, UnitCode = u.Code, p.TaxId, t.Rate, AllowsDecimals = u.Kind != UnitKind.Count, p.TrackStock, p.IsActive })
            .ToListAsync(ct);
        var prices = await _db.ProductPrices.AsNoTracking()
            .Where(x => ids.Contains(x.ProductId) && (x.BranchId == null || x.BranchId == branchId) && x.ValidFrom <= at && (x.ValidTo == null || x.ValidTo > at))
            .ToListAsync(ct);
        var unavailable = branchId is null
            ? new HashSet<Guid>()
            : await _db.BranchProducts.AsNoTracking().Where(b => b.BranchId == branchId && ids.Contains(b.ProductId) && !b.IsAvailable)
                .Select(b => b.ProductId).ToHashSetAsync(ct);
        return rows.ToDictionary(r => r.Id, r =>
        {
            var price = ProductPrice.EffectiveAt(prices.Where(x => x.ProductId == r.Id), branchId, at);
            return new SaleProductInfo(r.Id, r.Sku, r.Name, r.UnitCode, r.TaxId, r.Rate, price?.Amount, price?.IncludesTax ?? true, r.AllowsDecimals,
                r.TrackStock, r.IsActive, !unavailable.Contains(r.Id));
        });
    }

    public async Task<(IReadOnlyList<StockProductInfo> Items, int Total)> ListTrackedAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.Products.AsNoTracking().Where(p => p.IsActive && p.TrackStock);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim().ToLower()}%";
            query = query.Where(p => EF.Functions.Like(p.Name.ToLower(), term) || EF.Functions.Like(p.Sku.ToLower(), term));
        }
        var total = await query.CountAsync(ct);
        var items = await Project(query.OrderBy(p => p.Name).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return (items, total);
    }

    private IQueryable<StockProductInfo> Project(IQueryable<Product> products) =>
        from p in products
        join u in _db.Units on p.UnitId equals u.Id
        select new StockProductInfo(p.Id, p.Sku, p.Name, u.Code, u.Kind != UnitKind.Count, p.TrackStock,
            p.NegativeStockPolicy == NegativeStockPolicy.Allow, p.IsActive);
}
