using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Inventory;
using Tahona.BakeryPOS.SharedKernel.Pagination;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Inventory;

/// <param name="AllowNegative">Política D-06 del producto.</param>
public sealed record StockRowDto(Guid LocationId, string LocationName, Guid ProductId, string Sku, string ProductName, string UnitCode,
    decimal OnHand, bool AllowNegative, bool HasMovements);

/// <param name="Expected">Σ movimientos.</param>
public sealed record ReconciliationRowDto(Guid LocationId, string LocationName, Guid ProductId, string ProductName, decimal OnHand,
    decimal Expected, decimal Difference);

public sealed record ReconciliationDto(DateTime CheckedAt, int BalancesChecked, IReadOnlyList<ReconciliationRowDto> Differences);

/// <summary>
/// Existencias de una sucursal (HU-4.2): cada producto que controla existencias por cada ubicación activa,
/// con 0 si aún no tiene movimientos. Paginado por producto.
/// </summary>
public sealed record GetStockQuery(Guid BranchId, Guid? LocationId, string? Search, int Page = 1, int PageSize = 50)
    : IRequest<Result<PagedResult<StockRowDto>>>;

/// <summary>Libro de movimientos de una sucursal, del más reciente al más antiguo.</summary>
public sealed record GetStockMovementsQuery(Guid BranchId, Guid? LocationId, Guid? ProductId, string? Kind, DateTime? From, DateTime? To,
    int Page = 1, int PageSize = 50) : IRequest<Result<PagedResult<StockMovementDto>>>;

public sealed record GetMovementsByIdQuery(IReadOnlyList<Guid> Ids) : IRequest<Result<IReadOnlyList<StockMovementDto>>>;

/// <summary>Reconciliación (HU-4.6): saldos que no cuadran con la suma de sus movimientos. Debe salir vacía.</summary>
public sealed record GetReconciliationQuery(Guid? BranchId) : IRequest<Result<ReconciliationDto>>;

public sealed class GetStockValidator : AbstractValidator<GetStockQuery>
{
    public GetStockValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty().WithMessage("Elige una sucursal.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class GetStockMovementsValidator : AbstractValidator<GetStockMovementsQuery>
{
    public GetStockMovementsValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty().WithMessage("Elige una sucursal.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Kind).Must(k => k is null || Kinds.TryParse(k, out _)).WithMessage("Tipo de movimiento desconocido.");
    }
}

/// <summary>Nombres de los tipos de movimiento en la API (snake_case, como en la base).</summary>
public static class Kinds
{
    public static string Name(StockMovementKind kind) =>
        string.Concat(kind.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    public static bool TryParse(string value, out StockMovementKind kind) =>
        Enum.TryParse(value.Replace("_", ""), true, out kind) && Enum.IsDefined(kind);
}

public sealed class InventoryQueriesHandler :
    IRequestHandler<GetStockQuery, Result<PagedResult<StockRowDto>>>,
    IRequestHandler<GetStockMovementsQuery, Result<PagedResult<StockMovementDto>>>,
    IRequestHandler<GetMovementsByIdQuery, Result<IReadOnlyList<StockMovementDto>>>,
    IRequestHandler<GetReconciliationQuery, Result<ReconciliationDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IBranchDirectory _branches;
    private readonly IProductDirectory _products;
    private readonly IUserDirectory _users;
    private readonly TimeProvider _clock;

    public InventoryQueriesHandler(IAppDbContext db, ITenantContext tenant, IBranchDirectory branches, IProductDirectory products,
        IUserDirectory users, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _branches = branches;
        _products = products;
        _users = users;
        _clock = clock;
    }

    public async Task<Result<PagedResult<StockRowDto>>> Handle(GetStockQuery request, CancellationToken ct)
    {
        var scope = await CheckBranchAsync(request.BranchId, ct);
        if (scope.IsFailure)
            return Result.Failure<PagedResult<StockRowDto>>(scope.Error);

        var locations = await _db.StockLocations.AsNoTracking()
            .Where(l => l.BranchId == request.BranchId && l.IsActive && (request.LocationId == null || l.Id == request.LocationId))
            .OrderByDescending(l => l.IsDefault).ThenBy(l => l.Name).ToListAsync(ct);
        var (products, total) = await _products.ListTrackedAsync(request.Search, request.Page, request.PageSize, ct);

        var locationIds = locations.Select(l => l.Id).ToList();
        var productIds = products.Select(p => p.Id).ToList();
        var balances = await _db.StockBalances.AsNoTracking()
            .Where(b => locationIds.Contains(b.LocationId) && productIds.Contains(b.ProductId))
            .ToDictionaryAsync(b => (b.LocationId, b.ProductId), b => b.OnHand, ct);

        var rows = products
            .SelectMany(p => locations.Select(l =>
            {
                var found = balances.TryGetValue((l.Id, p.Id), out var onHand);
                return new StockRowDto(l.Id, l.Name, p.Id, p.Sku, p.Name, p.UnitCode, onHand, p.AllowNegative, found);
            }))
            .ToList();
        return Result.Success(new PagedResult<StockRowDto>(rows, request.Page, request.PageSize, total));
    }

    public async Task<Result<PagedResult<StockMovementDto>>> Handle(GetStockMovementsQuery request, CancellationToken ct)
    {
        var scope = await CheckBranchAsync(request.BranchId, ct);
        if (scope.IsFailure)
            return Result.Failure<PagedResult<StockMovementDto>>(scope.Error);

        var locationIds = await _db.StockLocations.Where(l => l.BranchId == request.BranchId).Select(l => l.Id).ToListAsync(ct);
        var query = _db.StockMovements.AsNoTracking().Where(m => locationIds.Contains(m.LocationId));
        if (request.LocationId is { } locationId) query = query.Where(m => m.LocationId == locationId);
        if (request.ProductId is { } productId) query = query.Where(m => m.ProductId == productId);
        if (request.Kind is not null && Kinds.TryParse(request.Kind, out var kind)) query = query.Where(m => m.Kind == kind);
        if (request.From is { } from) query = query.Where(m => m.OccurredAt >= from.ToUniversalTime());
        if (request.To is { } to) query = query.Where(m => m.OccurredAt <= to.ToUniversalTime());

        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(m => m.OccurredAt).ThenByDescending(m => m.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return Result.Success(new PagedResult<StockMovementDto>(await ToDtosAsync(page, ct), request.Page, request.PageSize, total));
    }

    public async Task<Result<IReadOnlyList<StockMovementDto>>> Handle(GetMovementsByIdQuery request, CancellationToken ct)
    {
        var ids = request.Ids.ToList();
        var movements = await _db.StockMovements.AsNoTracking().Where(m => ids.Contains(m.Id)).OrderBy(m => m.Id).ToListAsync(ct);
        return Result.Success(await ToDtosAsync(movements, ct));
    }

    public async Task<Result<ReconciliationDto>> Handle(GetReconciliationQuery request, CancellationToken ct)
    {
        if (request.BranchId is { } branchId)
        {
            var scope = await CheckBranchAsync(branchId, ct);
            if (scope.IsFailure)
                return Result.Failure<ReconciliationDto>(scope.Error);
        }

        var locations = (await _db.StockLocations.AsNoTracking()
                .Where(l => request.BranchId == null || l.BranchId == request.BranchId).ToListAsync(ct))
            .Where(l => _tenant.CanAccessBranch(l.BranchId)).ToDictionary(l => l.Id);
        var locationIds = locations.Keys.ToList();

        var balances = await _db.StockBalances.AsNoTracking()
            .Where(b => locationIds.Contains(b.LocationId))
            .Select(b => new { b.LocationId, b.ProductId, b.OnHand }).ToListAsync(ct);
        var sums = await _db.StockMovements.AsNoTracking()
            .Where(m => locationIds.Contains(m.LocationId))
            .GroupBy(m => new { m.LocationId, m.ProductId })
            .Select(g => new { g.Key.LocationId, g.Key.ProductId, Total = g.Sum(m => m.Quantity) }).ToListAsync(ct);

        var expected = sums.ToDictionary(s => (s.LocationId, s.ProductId), s => s.Total);
        var actual = balances.ToDictionary(b => (b.LocationId, b.ProductId), b => b.OnHand);
        var keys = expected.Keys.Union(actual.Keys).ToList();
        var differing = keys.Where(k => expected.GetValueOrDefault(k) != actual.GetValueOrDefault(k)).ToList();

        var names = await _products.FindAsync(differing.Select(k => k.ProductId).ToList(), ct);
        var rows = differing
            .Select(k => new ReconciliationRowDto(k.LocationId, locations[k.LocationId].Name, k.ProductId,
                names.TryGetValue(k.ProductId, out var p) ? p.Name : "", actual.GetValueOrDefault(k), expected.GetValueOrDefault(k),
                actual.GetValueOrDefault(k) - expected.GetValueOrDefault(k)))
            .ToList();
        return Result.Success(new ReconciliationDto(_clock.GetUtcNow().UtcDateTime, keys.Count, rows));
    }

    private async Task<Result> CheckBranchAsync(Guid branchId, CancellationToken ct)
    {
        if (await _branches.FindAsync(branchId, ct) is null)
            return Result.Failure(Error.NotFound("Sucursal", branchId));
        return _tenant.CanAccessBranch(branchId) ? Result.Success() : Result.Failure(Error.SinAsignacionVigente);
    }

    private async Task<IReadOnlyList<StockMovementDto>> ToDtosAsync(IReadOnlyList<StockMovement> movements, CancellationToken ct)
    {
        var products = await _products.FindAsync(movements.Select(m => m.ProductId).ToList(), ct);
        var locationIds = movements.Select(m => m.LocationId).Distinct().ToList();
        var locations = await _db.StockLocations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        var reasonIds = movements.Where(m => m.ReasonId != null).Select(m => m.ReasonId!.Value).Distinct().ToList();
        var reasons = await _db.AdjustmentReasons.AsNoTracking().Where(r => reasonIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var users = await _users.NamesAsync(movements.SelectMany(m => new[] { m.UserId, m.AuthorizedBy }).OfType<Guid>().ToList(), ct);

        return movements.Select(m =>
        {
            var p = products.GetValueOrDefault(m.ProductId);
            return new StockMovementDto(m.Id, m.OccurredAt, m.LocationId, locations.GetValueOrDefault(m.LocationId, ""), m.ProductId,
                p?.Sku ?? "", p?.Name ?? "", p?.UnitCode ?? "", m.Quantity, Kinds.Name(m.Kind),
                m.ReasonId is { } r ? reasons.GetValueOrDefault(r) : null,
                m.UserId is { } u ? users.GetValueOrDefault(u) : null,
                m.AuthorizedBy is { } a ? users.GetValueOrDefault(a) : null, m.Note);
        }).ToList();
    }
}
