using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Exceptions;
using Tahona.BakeryPOS.Domain.Inventory;
using Tahona.BakeryPOS.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Inventory;

/// <param name="Quantity">Con signo: positivo entra, negativo sale.</param>
/// <param name="SourceLineId">Línea del origen (p. ej. línea de venta); parte de la clave idempotente.</param>
public sealed record StockPostingLine(Guid ProductId, decimal Quantity, Guid SourceLineId);

/// <param name="SkipUntracked">Ventas: los productos sin control de existencias se ignoran. Ajustes: son un error.</param>
/// <param name="AuthorizedBy">Quien autoriza dejar saldo negativo en productos con política "bloquear" (D-06).</param>
public sealed record StockPosting(Guid LocationId, StockMovementKind Kind, string SourceType, Guid SourceId,
    IReadOnlyList<StockPostingLine> Lines, Guid? ReasonId = null, Guid? AuthorizedBy = null, string? Note = null,
    bool SkipUntracked = false);

/// <summary>
/// Única puerta para mover existencias (docs/inventory.md): crea los movimientos y actualiza los saldos
/// en la transacción del llamador (no guarda). Aplica la política D-06 y la idempotencia por origen: si
/// el mismo hecho ya se registró, devuelve sus movimientos sin volver a mover el saldo. La usan los
/// ajustes de esta fase y la confirmación de ventas (fase 8).
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

    /// <summary>Ubicación activa de la empresa y dentro del alcance por sucursal del token.</summary>
    public async Task<Result<StockLocation>> FindLocationAsync(Guid locationId, CancellationToken ct)
    {
        var location = await _db.StockLocations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == locationId, ct);
        if (location is null)
            return Result.Failure<StockLocation>(Error.NotFound("Ubicación", locationId));
        if (!_tenant.CanAccessBranch(location.BranchId))
            return Result.Failure<StockLocation>(Error.SinAsignacionVigente);
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

        // Idempotencia: las líneas ya registradas para este origen no se repiten.
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
        catch (DomainException e) when (e.ErrorCode == Error.ExistenciasInsuficientes.Code)
        {
            var name = products[pending[created.Count].ProductId].Name;
            return Result.Failure<IReadOnlyList<StockMovement>>(Error.ExistenciasInsuficientes with { Message = $"{name}: {e.Message}" });
        }

        return Result.Success<IReadOnlyList<StockMovement>>([.. existing, .. created]);
    }

    /// <summary>Saldo actual de un producto en una ubicación (0 si nunca se movió).</summary>
    public async Task<decimal> OnHandAsync(Guid locationId, Guid productId, CancellationToken ct) =>
        await _db.StockBalances.Where(b => b.LocationId == locationId && b.ProductId == productId)
            .Select(b => (decimal?)b.OnHand).FirstOrDefaultAsync(ct) ?? 0m;
}
