using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Inventory;

/// <summary>
/// Lugar donde hay existencias dentro de una sucursal ("Mostrador", "Vitrina", "Bodega"). Cada sucursal
/// tiene una por defecto, que es donde descuentan las ventas. Sin transferencias en el MVP.
/// </summary>
public sealed class StockLocation : AuditableEntity, ITenantEntity
{
    public const string DefaultCode = "MOSTRADOR";
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
    /// <summary>Entrada o salida según el signo (correcciones, conteo físico).</summary>
    Both,
}

/// <summary>Motivo de un ajuste de inventario (merma, daño, producción, conteo…). Obligatorio en todo ajuste.</summary>
public sealed class AdjustmentReason : AuditableEntity, ITenantEntity
{
    /// <summary>Motivo que usa el conteo físico para sus ajustes.</summary>
    public const string PhysicalCountCode = "CONTEO";

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

/// <summary>Motivos con que nace cada empresa. Supuesto razonable por confirmar con el dueño (I-21).</summary>
public static class DefaultAdjustmentReasons
{
    public static readonly IReadOnlyList<(string Code, string Name, AdjustmentDirection Direction)> All =
    [
        ("PRODUCCION", "Producción del día", AdjustmentDirection.In),
        ("COMPRA", "Compra o recepción", AdjustmentDirection.In),
        ("MERMA", "Merma (no vendido)", AdjustmentDirection.Out),
        ("DANO", "Daño o vencimiento", AdjustmentDirection.Out),
        ("CONSUMO", "Consumo interno", AdjustmentDirection.Out),
        (AdjustmentReason.PhysicalCountCode, "Conteo físico", AdjustmentDirection.Both),
        ("CORRECCION", "Corrección", AdjustmentDirection.Both),
    ];
}
