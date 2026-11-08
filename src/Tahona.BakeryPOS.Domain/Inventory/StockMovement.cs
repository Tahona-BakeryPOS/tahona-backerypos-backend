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

/// <summary>Orígenes de un movimiento: junto con su id forman la clave idempotente.</summary>
public static class StockSources
{
    /// <summary>Existencia inicial: el origen es la ubicación, la línea es el producto (una sola vez por par).</summary>
    public const string Initial = "initial";
    public const string Adjustment = "adjustment";
    public const string PhysicalCount = "count";
    public const string Sale = "sale";
    public const string Return = "return";
}

/// <summary>
/// Movimiento de inventario: libro mayor de solo inserción, cantidad con signo. Nunca se edita ni se
/// borra; los errores se corrigen con otro movimiento. La clave (empresa, tipo, origen, id, línea) es
/// única: registrar dos veces el mismo hecho no mueve dos veces el saldo.
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
    /// <summary>Línea del origen; <see cref="Guid.Empty"/> si el origen no tiene líneas.</summary>
    public Guid SourceLineId { get; private set; }
    public Guid? ReasonId { get; private set; }
    /// <summary>Quién registró el movimiento.</summary>
    public Guid? UserId { get; private set; }
    /// <summary>Quién autorizó (ajustes, o vender sin existencias con política "bloquear").</summary>
    public Guid? AuthorizedBy { get; private set; }
    public string? Note { get; private set; }
    public DateTime OccurredAt { get; private set; }

    private StockMovement() { }

    public static StockMovement Create(Guid companyId, Guid locationId, Guid productId, decimal quantity, StockMovementKind kind,
        string sourceType, Guid sourceId, Guid sourceLineId, DateTime occurredAt, Guid? userId,
        Guid? reasonId = null, Guid? authorizedBy = null, string? note = null)
    {
        if (companyId == Guid.Empty || locationId == Guid.Empty || productId == Guid.Empty)
            throw new DomainException("VALIDACION", "El movimiento necesita empresa, ubicación y producto.");
        if (quantity == 0)
            throw new DomainException("VALIDACION", "La cantidad del movimiento no puede ser cero.");
        if (decimal.Round(quantity, 4) != quantity)
            throw new DomainException("VALIDACION", "La cantidad admite máximo 4 decimales.");
        if (IsInbound(kind) != quantity > 0)
            throw new DomainException("VALIDACION", $"Un movimiento {kind} {(IsInbound(kind) ? "suma" : "resta")} existencias.");
        if (string.IsNullOrWhiteSpace(sourceType) || sourceId == Guid.Empty)
            throw new DomainException("VALIDACION", "El movimiento debe indicar su origen.");

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
/// Saldo por (ubicación, producto): proyección de los movimientos, actualizada en la misma transacción
/// que cada uno. Nunca se edita directamente. Su versión de fila evita que dos cajas vendan a la vez la
/// última unidad sin verlo (la segunda falla por concurrencia y reintenta).
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
    /// Aplica el movimiento. Si el saldo quedaría negativo y el producto no lo permite (D-06), exige una
    /// autorización; sin ella falla con EXISTENCIAS_INSUFICIENTES.
    /// </summary>
    public void Apply(StockMovement movement, bool allowNegative)
    {
        if (movement.LocationId != LocationId || movement.ProductId != ProductId || movement.CompanyId != CompanyId)
            throw new DomainException("VALIDACION", "El movimiento no corresponde a este saldo.");
        var next = OnHand + movement.Quantity;
        if (movement.Quantity < 0 && next < 0 && !allowNegative && movement.AuthorizedBy is null)
            throw new DomainException("EXISTENCIAS_INSUFICIENTES",
                $"Existencias insuficientes: hay {OnHand:0.####} y se necesitan {-movement.Quantity:0.####}. Pide autorización a un superior.");
        OnHand = next;
        LastMovementId = movement.Id;
    }
}
