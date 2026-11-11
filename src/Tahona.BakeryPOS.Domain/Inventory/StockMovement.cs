using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Inventory;

public enum StockMovementKind
{
    Initial,
    Sale,
    Return,
    VoidReversal,
    AdjustmentIn,
    AdjustmentOut,
}

/// <summary>Origins of a movement: together with its id they form the idempotent key.</summary>
public static class StockSources
{
    /// <summary>Initial stock: the origin is the location, the line is the product (only once per pair).</summary>
    public const string Initial = "initial";
    public const string Adjustment = "adjustment";
    public const string PhysicalCount = "count";
    public const string Sale = "sale";
    public const string Return = "return";
}

/// <summary>
/// Inventory movement: an insert-only ledger, signed quantity. It is never edited or
/// deleted; mistakes are corrected with another movement. The key (company, kind, source, id, line) is
/// unique: recording the same fact twice does not move the balance twice.
/// </summary>
public sealed class StockMovement : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public StockMovementKind Kind { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    /// <summary>Line of the source; <see cref="Guid.Empty"/> if the source has no lines.</summary>
    public Guid SourceLineId { get; private set; }
    public Guid? ReasonId { get; private set; }
    /// <summary>Who recorded the movement.</summary>
    public Guid? UserId { get; private set; }
    /// <summary>Who authorized (adjustments, or selling without stock with a "block" policy).</summary>
    public Guid? AuthorizedBy { get; private set; }
    public string? Note { get; private set; }
    public DateTime OccurredAt { get; private set; }

    private StockMovement() { }

    public static StockMovement Create(Guid companyId, Guid locationId, Guid productId, decimal quantity, StockMovementKind kind,
        string sourceType, Guid sourceId, Guid sourceLineId, DateTime occurredAt, Guid? userId,
        Guid? reasonId = null, Guid? authorizedBy = null, string? note = null)
    {
        if (companyId == Guid.Empty || locationId == Guid.Empty || productId == Guid.Empty)
            throw new DomainException("VALIDATION", "El movimiento necesita empresa, ubicación y producto.");
        if (quantity == 0)
            throw new DomainException("VALIDATION", "La cantidad del movimiento no puede ser cero.");
        if (decimal.Round(quantity, 4) != quantity)
            throw new DomainException("VALIDATION", "La cantidad admite máximo 4 decimales.");
        if (IsInbound(kind) != quantity > 0)
            throw new DomainException("VALIDATION", $"Un movimiento {kind} {(IsInbound(kind) ? "suma" : "resta")} existencias.");
        if (string.IsNullOrWhiteSpace(sourceType) || sourceId == Guid.Empty)
            throw new DomainException("VALIDATION", "El movimiento debe indicar su origen.");

        return new StockMovement
        {
            CompanyId = companyId, LocationId = locationId, ProductId = productId, Quantity = quantity, Kind = kind,
            SourceType = sourceType, SourceId = sourceId, SourceLineId = sourceLineId, OccurredAt = occurredAt,
            UserId = userId, ReasonId = reasonId, AuthorizedBy = authorizedBy,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
    }

    public static bool IsInbound(StockMovementKind kind) => kind is not (StockMovementKind.Sale or StockMovementKind.AdjustmentOut);
}

/// <summary>
/// Balance per (location, product): a projection of the movements, updated in the same transaction
/// as each one. It is never edited directly. Its row version prevents two registers from selling the
/// last unit at the same time without seeing it (the second fails on concurrency and retries).
/// </summary>
public sealed class StockBalance : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal OnHand { get; private set; }
    public Guid? LastMovementId { get; private set; }

    private StockBalance() { }

    public static StockBalance Empty(Guid companyId, Guid locationId, Guid productId) =>
        new() { CompanyId = companyId, LocationId = locationId, ProductId = productId };

    /// <summary>
    /// Applies the movement. If the balance would go negative and the product does not allow it (D-06), it requires an
    /// authorization; without it, it fails with INSUFFICIENT_STOCK.
    /// </summary>
    public void Apply(StockMovement movement, bool allowNegative)
    {
        if (movement.LocationId != LocationId || movement.ProductId != ProductId || movement.CompanyId != CompanyId)
            throw new DomainException("VALIDATION", "El movimiento no corresponde a este saldo.");
        var next = OnHand + movement.Quantity;
        if (movement.Quantity < 0 && next < 0 && !allowNegative && movement.AuthorizedBy is null)
            throw new DomainException("INSUFFICIENT_STOCK",
                $"Existencias insuficientes: hay {OnHand:0.####} y se necesitan {-movement.Quantity:0.####}. Pide autorización a un superior.");
        OnHand = next;
        LastMovementId = movement.Id;
    }
}
