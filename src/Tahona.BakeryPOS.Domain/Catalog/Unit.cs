namespace Tahona.BakeryPOS.Domain.Catalog;

public enum UnitKind
{
    Count,
    Mass,
    Volume,
}

/// <summary>
/// Unit of measure (global catalog, seeded by migration). Mass and volume ones accept
/// decimal quantities (US-3.1). <see cref="DianCode"/> is the UN/ECE Rec. 20 code used by the
/// electronic invoice (verify against the technical annex in phase 7).
/// </summary>
public sealed class Unit
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public UnitKind Kind { get; init; }
    public string DianCode { get; init; } = string.Empty;

    public bool AllowsDecimals => Kind != UnitKind.Count;
}

/// <summary>Seeded units. Adding one here requires a migration.</summary>
public static class Units
{
    public const string Unidad = "UND";

    public static readonly IReadOnlyList<(string Code, string Name, UnitKind Kind, string DianCode)> All =
    [
        (Unidad, "Unidad", UnitKind.Count, "94"),
        ("KG", "Kilogramo", UnitKind.Mass, "KGM"),
        ("G", "Gramo", UnitKind.Mass, "GRM"),
        ("LB", "Libra", UnitKind.Mass, "LBR"),
        ("L", "Litro", UnitKind.Volume, "LTR"),
        ("ML", "Mililitro", UnitKind.Volume, "MLT"),
    ];
}
