using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Companies;

/// <summary>
/// Empresa cliente del SaaS (tenant). Es la raíz del aislamiento: todo dato de negocio cuelga de una.
/// Solo el administrador de plataforma la da de alta (D-09); el propietario edita contacto y zona horaria.
/// Los datos fiscales (NIT, régimen, responsabilidades) se fijan en el alta.
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
            throw new DomainException("VALIDACION", "La razón social es obligatoria.");

        var responsibilities = taxResponsibilities.Select(r => r.Trim().ToUpperInvariant()).Distinct().ToList();
        if (!TaxCodes.Regimes.Contains(taxRegime))
            throw new DomainException("VALIDACION", $"Régimen fiscal no reconocido: {taxRegime}.");
        if (responsibilities.Count == 0 || responsibilities.Any(r => !TaxCodes.Responsibilities.Contains(r)))
            throw new DomainException("VALIDACION", "Las responsabilidades fiscales son obligatorias y deben ser códigos DIAN válidos.");

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

    /// <summary>Datos que el propietario puede cambiar (HU-1.2). La zona horaria define el "día" de reportes y cierres.</summary>
    public void UpdateContact(string? tradeName, string email, string? phone, string? address, string? cityCode, string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("VALIDACION", "El correo de la empresa es obligatorio.");
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
/// Códigos fiscales del anexo técnico de facturación electrónica de la DIAN (por verificar con el
/// contador antes de la fase 7, D-05). Régimen: 48 responsable de IVA, 49 no responsable.
/// Responsabilidades: O-13 gran contribuyente, O-15 autorretenedor, O-23 agente de retención de IVA,
/// O-47 régimen simple de tributación, R-99-PN no aplica (otros).
/// </summary>
public static class TaxCodes
{
    public static readonly IReadOnlySet<string> Regimes = new HashSet<string> { "48", "49" };

    public static readonly IReadOnlySet<string> Responsibilities =
        new HashSet<string> { "O-13", "O-15", "O-23", "O-47", "R-99-PN" };
}
