using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Inventory;

/// <summary>
/// A place where there is stock within a branch ("Mostrador", "Vitrina", "Bodega"). Each branch
/// has a default one, which is where sales deduct. No transfers in the MVP.
/// </summary>
public sealed class StockLocation : AuditableEntity, ITenantEntity
{
    public const string DefaultCode = "COUNTER";
    public const string DefaultName = "Mostrador";

    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; } = true;

    private StockLocation() { }

    public static StockLocation Create(Guid companyId, Guid branchId, string code, string name, bool isDefault)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty)
            throw new DomainException("VALIDACION", "La ubicación debe pertenecer a una sucursal.");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            throw new DomainException("VALIDACION", "Código y nombre de la ubicación son obligatorios.");
        return new StockLocation
        {
            CompanyId = companyId, BranchId = branchId, Code = code.Trim().ToUpperInvariant(), Name = name.Trim(), IsDefault = isDefault,
        };
    }

    public static StockLocation CreateDefault(Guid companyId, Guid branchId) =>
        Create(companyId, branchId, DefaultCode, DefaultName, isDefault: true);
}

public enum AdjustmentDirection
{
    In,
    Out,
    /// <summary>Entry or exit according to the sign (corrections, physical count).</summary>
    Both,
}

/// <summary>Reason for an inventory adjustment (waste, damage, production, count…). Mandatory on every adjustment.</summary>
public sealed class AdjustmentReason : AuditableEntity, ITenantEntity
{
    /// <summary>Reason the physical count uses for its adjustments.</summary>
    public const string PhysicalCountCode = "PHYSICAL_COUNT";

    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public AdjustmentDirection Direction { get; private set; }
    public bool IsActive { get; private set; } = true;

    private AdjustmentReason() { }

    public static AdjustmentReason Create(Guid companyId, string code, string name, AdjustmentDirection direction)
    {
        if (companyId == Guid.Empty)
            throw new DomainException("VALIDACION", "El motivo debe pertenecer a una empresa.");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            throw new DomainException("VALIDACION", "Código y nombre del motivo son obligatorios.");
        return new AdjustmentReason { CompanyId = companyId, Code = code.Trim().ToUpperInvariant(), Name = name.Trim(), Direction = direction };
    }

    public bool Allows(decimal quantity) => Direction switch
    {
        AdjustmentDirection.In => quantity > 0,
        AdjustmentDirection.Out => quantity < 0,
        _ => quantity != 0,
    };
}

/// <summary>Reasons each company is born with. A reasonable assumption to confirm with the owner (I-21).</summary>
public static class DefaultAdjustmentReasons
{
    public static readonly IReadOnlyList<(string Code, string Name, AdjustmentDirection Direction)> All =
    [
        ("PRODUCTION", "Producción del día", AdjustmentDirection.In),
        ("PURCHASE", "Compra o recepción", AdjustmentDirection.In),
        ("WASTE", "Merma (no vendido)", AdjustmentDirection.Out),
        ("DAMAGE", "Daño o vencimiento", AdjustmentDirection.Out),
        ("INTERNAL_USE", "Consumo interno", AdjustmentDirection.Out),
        (AdjustmentReason.PhysicalCountCode, "Conteo físico", AdjustmentDirection.Both),
        ("CORRECTION", "Corrección", AdjustmentDirection.Both),
    ];
}
