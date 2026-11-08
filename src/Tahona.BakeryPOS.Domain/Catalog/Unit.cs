namespace Tahona.BakeryPOS.Domain.Catalog;

public enum UnitKind
{
    Count,
    Mass,
    Volume,
}

/// <summary>
/// Unidad de medida (catálogo global, sembrado por migración). Las de masa y volumen admiten
/// cantidades decimales (HU-3.1). <see cref="DianCode"/> es el código UN/ECE Rec. 20 que usa la
/// factura electrónica (verificar contra el anexo técnico en la fase 7).
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

/// <summary>Unidades sembradas. Agregar una aquí exige una migración.</summary>
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
