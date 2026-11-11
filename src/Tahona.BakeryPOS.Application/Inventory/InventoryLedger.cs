using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Exceptions;
using Tahona.BakeryPOS.Domain.Inventory;
using Tahona.BakeryPOS.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Inventory;

/// <param name="Quantity">Signed: positive goes in, negative goes out.</param>
/// <param name="SourceLineId">Line of the source (e.g. a sale line); part of the idempotent key.</param>
public sealed record StockPostingLine(Guid ProductId, decimal Quantity, Guid SourceLineId);

/// <param name="SkipUntracked">Sales: products that do not track stock are ignored. Adjustments: it is an error.</param>
/// <param name="AuthorizedBy">Who authorizes leaving a negative balance on products with a "block" policy (D-06).</param>
public sealed record StockPosting(Guid LocationId, StockMovementKind Kind, string SourceType, Guid SourceId,
    IReadOnlyList<StockPostingLine> Lines, Guid? ReasonId = null, Guid? AuthorizedBy = null, string? Note = null,
    bool SkipUntracked = false);

/// <summary>
/// The only door for moving stock (docs/inventory.md): it creates the movements and updates the balances
/// in the caller's transaction (it does not save). It applies the D-06 policy and idempotency by source: if
/// the same fact was already recorded, it returns its movements without moving the balance again. It is used by
/// the adjustments and by sale confirmation (phase 8).
/// </summary>
public sealed class InventoryLedger
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IProductDirectory _products;
    private readonly TimeProvider _clock;

    public InventoryLedger(IAppDbContext db, ITenantContext tenant, IProductDirectory products, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _products = products;
        _clock = clock;
    }

    /// <summary>Active location of the company and within the token's branch scope.</summary>
    public async Task<Result<StockLocation>> FindLocationAsync(Guid locationId, CancellationToken ct)
    {
        var location = await _db.StockLocations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == locationId, ct);
        if (location is null)
            return Result.Failure<StockLocation>(Error.NotFound("Ubicación", locationId));
        if (!_tenant.CanAccessBranch(location.BranchId))
            return Result.Failure<StockLocation>(Error.NoCurrentAssignment);
        if (!location.IsActive)
            return Result.Failure<StockLocation>(Error.Validation("La ubicación está desactivada."));
        return Result.Success(location);
    }

    public async Task<Result<IReadOnlyList<StockMovement>>> PostAsync(StockPosting posting, CancellationToken ct)
    {
        if (posting.Lines.Count == 0)
            return Result.Failure<IReadOnlyList<StockMovement>>(Error.Validation("No hay líneas para registrar."));
        if (posting.Lines.GroupBy(l => (l.ProductId, l.SourceLineId)).Any(g => g.Count() > 1))
            return Result.Failure<IReadOnlyList<StockMovement>>(Error.Validation("Hay líneas repetidas."));

        var products = await _products.FindAsync(posting.Lines.Select(l => l.ProductId).ToList(), ct);
        var lines = new List<StockPostingLine>();
        foreach (var line in posting.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
                return Result.Failure<IReadOnlyList<StockMovement>>(Error.NotFound("Producto", line.ProductId));
            if (!product.TrackStock)
            {
                if (posting.SkipUntracked) continue;
                return Result.Failure<IReadOnlyList<StockMovement>>(Error.Validation($"{product.Name} no controla existencias: actívalo en el catálogo."));
            }
            if (!product.AllowsDecimals && decimal.Truncate(line.Quantity) != line.Quantity)
                return Result.Failure<IReadOnlyList<StockMovement>>(Error.Validation($"{product.Name} se cuenta por unidades: usa cantidades enteras."));
            lines.Add(line);
        }

        // Idempotency: lines already recorded for this source are not repeated.
        var lineIds = lines.Select(l => l.SourceLineId).ToList();
        var existing = await _db.StockMovements.AsNoTracking()
            .Where(m => m.Kind == posting.Kind && m.SourceType == posting.SourceType && m.SourceId == posting.SourceId
                && lineIds.Contains(m.SourceLineId))
            .ToListAsync(ct);
        var pending = lines.Where(l => existing.All(m => m.SourceLineId != l.SourceLineId)).ToList();

        var productIds = pending.Select(l => l.ProductId).Distinct().ToList();
        var balances = await _db.StockBalances
            .Where(b => b.LocationId == posting.LocationId && productIds.Contains(b.ProductId))
            .ToDictionaryAsync(b => b.ProductId, ct);

        var companyId = _tenant.RequiredCompanyId;
        var now = _clock.GetUtcNow().UtcDateTime;
        var created = new List<StockMovement>();
        try
        {
            foreach (var line in pending)
            {
                var movement = StockMovement.Create(companyId, posting.LocationId, line.ProductId, line.Quantity, posting.Kind,
                    posting.SourceType, posting.SourceId, line.SourceLineId, now, _tenant.UserId, posting.ReasonId,
                    posting.AuthorizedBy, posting.Note);
                if (!balances.TryGetValue(line.ProductId, out var balance))
                {
                    balance = StockBalance.Empty(companyId, posting.LocationId, line.ProductId);
                    balances[line.ProductId] = balance;
                    _db.StockBalances.Add(balance);
                }
                balance.Apply(movement, products[line.ProductId].AllowNegative);
                _db.StockMovements.Add(movement);
                created.Add(movement);
            }
        }
        catch (DomainException e) when (e.ErrorCode == Error.InsufficientStock.Code)
        {
            var name = products[pending[created.Count].ProductId].Name;
            return Result.Failure<IReadOnlyList<StockMovement>>(Error.InsufficientStock with { Message = $"{name}: {e.Message}" });
        }

        return Result.Success<IReadOnlyList<StockMovement>>([.. existing, .. created]);
    }

    /// <summary>Current balance of a product at a location (0 if it never moved).</summary>
    public async Task<decimal> OnHandAsync(Guid locationId, Guid productId, CancellationToken ct) =>
        await _db.StockBalances.Where(b => b.LocationId == locationId && b.ProductId == productId)
            .Select(b => (decimal?)b.OnHand).FirstOrDefaultAsync(ct) ?? 0m;
}
