using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Catalog;
using Tahona.BakeryPOS.SharedKernel.Pagination;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Catalog.Products;

/// <param name="BasePrice">Current base price (null = no price).</param>
/// <param name="BranchPrice">The queried branch's own price, if it has a current one.</param>
/// <param name="Price">The price in force: the branch's or, if none, the base one (US-3.2).</param>
/// <param name="PriceIncludesTax">Whether <paramref name="Price"/> includes the tax (D-07).</param>
/// <param name="IsAvailable">Availability at the queried branch (US-3.3); without a branch, true.</param>
/// <param name="NegativeStockPolicy">D-06: <c>allow</c> or <c>block</c> (only applies with <paramref name="TrackStock"/>).</param>
/// <param name="Version">ETag for If-Match on PUT.</param>
public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    Guid CategoryId,
    string CategoryName,
    Guid UnitId,
    string UnitCode,
    bool AllowsDecimals,
    Guid TaxId,
    string TaxName,
    decimal TaxRate,
    string? Barcode,
    bool TrackStock,
    string NegativeStockPolicy,
    bool IsActive,
    decimal? BasePrice,
    decimal? BranchPrice,
    decimal? Price,
    bool? PriceIncludesTax,
    bool IsAvailable,
    string Version);

/// <param name="BranchId">null = the company's base price.</param>
public sealed record ProductPriceDto(Guid Id, Guid? BranchId, decimal Amount, bool IncludesTax, DateTime ValidFrom, DateTime? ValidTo, bool IsCurrent);

/// <summary>
/// Products with their current price and availability at a branch (or only the base price without one).
/// <paramref name="Search"/> searches name, SKU and barcode.
/// </summary>
public sealed record GetProductsQuery(Guid? BranchId, string? Search, Guid? CategoryId, bool IncludeInactive, int Page = 1, int PageSize = 50)
    : IRequest<Result<PagedResult<ProductDto>>>;

public sealed record GetProductByIdQuery(Guid Id, Guid? BranchId) : IRequest<Result<ProductDto>>;

/// <summary>The product's price history: base and per branch, all validity periods.</summary>
public sealed record GetProductPricesQuery(Guid ProductId) : IRequest<Result<IReadOnlyList<ProductPriceDto>>>;

public sealed class GetProductsValidator : AbstractValidator<GetProductsQuery>
{
    public GetProductsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public sealed class ProductQueriesHandler :
    IRequestHandler<GetProductsQuery, Result<PagedResult<ProductDto>>>,
    IRequestHandler<GetProductByIdQuery, Result<ProductDto>>,
    IRequestHandler<GetProductPricesQuery, Result<IReadOnlyList<ProductPriceDto>>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IBranchDirectory _branches;
    private readonly TimeProvider _clock;

    public ProductQueriesHandler(IAppDbContext db, ITenantContext tenant, IBranchDirectory branches, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _branches = branches;
        _clock = clock;
    }

    public async Task<Result<PagedResult<ProductDto>>> Handle(GetProductsQuery request, CancellationToken ct)
    {
        var branch = await CheckBranchAsync(request.BranchId, ct);
        if (branch.IsFailure)
            return Result.Failure<PagedResult<ProductDto>>(branch.Error);

        var query = _db.Products.AsNoTracking();
        if (!request.IncludeInactive)
            query = query.Where(p => p.IsActive);
        if (request.CategoryId is { } categoryId)
            query = query.Where(p => p.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = $"%{request.Search.Trim().ToLower()}%";
            query = query.Where(p => EF.Functions.Like(p.Name.ToLower(), term) || EF.Functions.Like(p.Sku.ToLower(), term)
                || (p.Barcode != null && EF.Functions.Like(p.Barcode, term)));
        }

        var total = await query.CountAsync(ct);
        var page = await query.OrderBy(p => p.Name)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(ct);

        var items = await ToDtosAsync(page, request.BranchId, ct);
        return Result.Success(new PagedResult<ProductDto>(items, request.Page, request.PageSize, total));
    }

    public async Task<Result<ProductDto>> Handle(GetProductByIdQuery request, CancellationToken ct)
    {
        var branch = await CheckBranchAsync(request.BranchId, ct);
        if (branch.IsFailure)
            return Result.Failure<ProductDto>(branch.Error);

        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.Id, ct);
        if (product is null)
            return Result.Failure<ProductDto>(Error.NotFound("Producto", request.Id));
        return Result.Success((await ToDtosAsync([product], request.BranchId, ct))[0]);
    }

    public async Task<Result<IReadOnlyList<ProductPriceDto>>> Handle(GetProductPricesQuery request, CancellationToken ct)
    {
        if (!await _db.Products.AnyAsync(p => p.Id == request.ProductId, ct))
            return Result.Failure<IReadOnlyList<ProductPriceDto>>(Error.NotFound("Producto", request.ProductId));

        var now = _clock.GetUtcNow().UtcDateTime;
        var prices = await _db.ProductPrices.AsNoTracking()
            .Where(p => p.ProductId == request.ProductId)
            .OrderBy(p => p.BranchId).ThenByDescending(p => p.ValidFrom)
            .ToListAsync(ct);
        IReadOnlyList<ProductPriceDto> list = prices.Select(p => ToDto(p, now)).ToList();
        return Result.Success(list);
    }

    internal static ProductPriceDto ToDto(ProductPrice p, DateTime now) =>
        new(p.Id, p.BranchId, p.Amount, p.IncludesTax, p.ValidFrom, p.ValidTo, p.IsValidAt(now));

    /// <summary>A foreign or invented branch answers 404; one outside the token's scope, 403.</summary>
    private async Task<Result> CheckBranchAsync(Guid? branchId, CancellationToken ct)
    {
        if (branchId is not { } id)
            return Result.Success();
        if (await _branches.FindAsync(id, ct) is null)
            return Result.Failure(Error.NotFound("Sucursal", id));
        return _tenant.CanAccessBranch(id) ? Result.Success() : Result.Failure(Error.SinAsignacionVigente);
    }

    private async Task<IReadOnlyList<ProductDto>> ToDtosAsync(IReadOnlyList<Product> products, Guid? branchId, CancellationToken ct)
    {
        var ids = products.Select(p => p.Id).ToList();
        var now = _clock.GetUtcNow().UtcDateTime;

        var categoryIds = products.Select(p => p.CategoryId).Distinct().ToList();
        var taxIds = products.Select(p => p.TaxId).Distinct().ToList();

        var categories = await _db.Categories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var taxes = await _db.Taxes.AsNoTracking()
            .Where(t => taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var units = await _db.Units.AsNoTracking().ToDictionaryAsync(u => u.Id, ct);
        var prices = await _db.ProductPrices.AsNoTracking()
            .Where(p => ids.Contains(p.ProductId) && (p.BranchId == null || p.BranchId == branchId)
                && p.ValidFrom <= now && (p.ValidTo == null || p.ValidTo > now))
            .ToListAsync(ct);
        HashSet<Guid> unavailable = branchId is null
            ? []
            : await _db.BranchProducts.AsNoTracking()
                .Where(b => b.BranchId == branchId && ids.Contains(b.ProductId) && !b.IsAvailable)
                .Select(b => b.ProductId).ToHashSetAsync(ct);

        return products.Select(p =>
        {
            var own = prices.Where(x => x.ProductId == p.Id).ToList();
            var basePrice = own.FirstOrDefault(x => x.BranchId is null);
            var branchPrice = branchId is null ? null : own.FirstOrDefault(x => x.BranchId == branchId);
            var effective = ProductPrice.EffectiveAt(own, branchId, now);
            var tax = taxes[p.TaxId];
            var unit = units[p.UnitId];
            return new ProductDto(p.Id, p.Sku, p.Name, p.CategoryId, categories.GetValueOrDefault(p.CategoryId, ""),
                p.UnitId, unit.Code, unit.AllowsDecimals, p.TaxId, tax.Name, tax.Rate, p.Barcode, p.TrackStock,
                p.NegativeStockPolicy.ToString().ToLowerInvariant(), p.IsActive,
                basePrice?.Amount, branchPrice?.Amount, effective?.Amount, effective?.IncludesTax,
                !unavailable.Contains(p.Id), RowVersions.Encode(p.RowVersion));
        }).ToList();
    }
}
