using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Companies;

/// <summary>
/// SaaS customer company (tenant). It is the root of isolation: all business data hangs from one.
/// Only the platform administrator onboards it (D-09); the owner edits contact and time zone.
/// The tax data (NIT, regime, responsibilities) is set at onboarding.
/// </summary>
public sealed class Company : AuditableEntity
{
    public string LegalName { get; private set; } = string.Empty;
    public string? TradeName { get; private set; }
    public string NitNumber { get; private set; } = string.Empty;
    public int NitCheckDigit { get; private set; }
    public string TaxRegime { get; private set; } = string.Empty;
    public List<string> TaxResponsibilities { get; private set; } = [];
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public string? CityCode { get; private set; }
    public string TimeZone { get; private set; } = DefaultTimeZone;
    public string Currency { get; private set; } = "COP";
    public CompanyStatus Status { get; private set; } = CompanyStatus.Active;

    public const string DefaultTimeZone = "America/Bogota";

    private Company() { }

    public static Company Create(
        string legalName,
        Nit nit,
        string taxRegime,
        IEnumerable<string> taxResponsibilities,
        string email,
        string? tradeName = null,
        string? phone = null,
        string? address = null,
        string? cityCode = null,
        string? timeZone = null)
    {
        if (string.IsNullOrWhiteSpace(legalName))
            throw new DomainException("VALIDATION", "La razón social es obligatoria.");

        var responsibilities = taxResponsibilities.Select(r => r.Trim().ToUpperInvariant()).Distinct().ToList();
        if (!TaxCodes.Regimes.Contains(taxRegime))
            throw new DomainException("VALIDATION", $"Régimen fiscal no reconocido: {taxRegime}.");
        if (responsibilities.Count == 0 || responsibilities.Any(r => !TaxCodes.Responsibilities.Contains(r)))
            throw new DomainException("VALIDATION", "Las responsabilidades fiscales son obligatorias y deben ser códigos DIAN válidos.");

        var company = new Company
        {
            LegalName = legalName.Trim(),
            NitNumber = nit.Number,
            NitCheckDigit = nit.CheckDigit,
            TaxRegime = taxRegime,
            TaxResponsibilities = responsibilities,
        };
        company.UpdateContact(tradeName, email, phone, address, cityCode, timeZone);
        return company;
    }

    /// <summary>Data the owner can change (US-1.2). The time zone defines the "day" of reports and closings.</summary>
    public void UpdateContact(string? tradeName, string email, string? phone, string? address, string? cityCode, string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("VALIDATION", "El correo de la empresa es obligatorio.");
        TradeName = Clean(tradeName);
        Email = email.Trim().ToLowerInvariant();
        Phone = Clean(phone);
        Address = Clean(address);
        CityCode = Clean(cityCode);
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? DefaultTimeZone : timeZone.Trim();
    }

    public string DisplayName => TradeName ?? LegalName;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum CompanyStatus
{
    Active,
    Suspended,
}

/// <summary>
/// Tax codes from the DIAN electronic-invoicing technical annex (to verify with the
/// accountant before phase 7, D-05). Regime: 48 VAT-responsible, 49 not responsible.
/// Responsibilities: O-13 large taxpayer, O-15 self-withholder, O-23 VAT withholding agent,
/// O-47 simple tax regime, R-99-PN not applicable (others).
/// </summary>
public static class TaxCodes
{
    public static readonly IReadOnlySet<string> Regimes = new HashSet<string> { "48", "49" };

    public static readonly IReadOnlySet<string> Responsibilities =
        new HashSet<string> { "O-13", "O-15", "O-23", "O-47", "R-99-PN" };
}
