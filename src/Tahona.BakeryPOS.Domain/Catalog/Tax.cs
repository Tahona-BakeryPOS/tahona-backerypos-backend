using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Catalog;

public enum TaxKind
{
    /// <summary>VAT (DIAN code 01).</summary>
    Iva,
    /// <summary>National consumption tax (DIAN code 04).</summary>
    Inc,
    /// <summary>No tax: excluded good (DIAN code ZZ, "not applicable").</summary>
    None,
}

/// <summary>
/// The company's tax. The rate is not edited: changing it would alter the price of all products
/// that use it; for another rate a new tax is created and the previous one is deactivated. Sales store
/// a copy of the rate applied.
/// </summary>
public sealed class Tax : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public TaxKind Kind { get; private set; }
    /// <summary>Porcentaje: 19 = 19 %.</summary>
    public decimal Rate { get; private set; }
    public bool IsActive { get; private set; } = true;

    public string DianCode => DianCodeOf(Kind);

    private Tax() { }

    public static Tax Create(Guid companyId, string code, string name, TaxKind kind, decimal rate)
    {
        if (companyId == Guid.Empty)
            throw new DomainException("VALIDACION", "El impuesto debe pertenecer a una empresa.");
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("VALIDACION", "El código del impuesto es obligatorio.");
        if (rate < 0 || rate > 100)
            throw new DomainException("VALIDACION", "La tarifa debe estar entre 0 y 100.");
        if (kind == TaxKind.None && rate != 0)
            throw new DomainException("VALIDACION", "Un impuesto 'sin impuesto' tiene tarifa 0.");
        var tax = new Tax { CompanyId = companyId, Code = code.Trim().ToUpperInvariant(), Kind = kind, Rate = rate };
        tax.Update(name, isActive: true);
        return tax;
    }

    public void Update(string name, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VALIDACION", "El nombre del impuesto es obligatorio.");
        Name = name.Trim();
        IsActive = isActive;
    }

    public static string DianCodeOf(TaxKind kind) => kind switch
    {
        TaxKind.Iva => "01",
        TaxKind.Inc => "04",
        _ => "ZZ",
    };
}

/// <summary>
/// Taxes created with each company (US-1.1). A reasonable assumption for a bakery:
/// confirm the rates with the accountant (I-20).
/// </summary>
public static class DefaultTaxes
{
    public static readonly IReadOnlyList<(string Code, string Name, TaxKind Kind, decimal Rate)> All =
    [
        ("IVA19", "IVA 19 %", TaxKind.Iva, 19m),
        ("IVA5", "IVA 5 %", TaxKind.Iva, 5m),
        ("IVA0", "IVA 0 % (exento)", TaxKind.Iva, 0m),
        ("EXCLUDED", "Excluido de IVA", TaxKind.None, 0m),
        ("INC8", "Impuesto al consumo 8 %", TaxKind.Inc, 8m),
    ];
}
