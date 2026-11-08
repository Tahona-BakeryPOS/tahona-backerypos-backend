using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Companies;

/// <summary>
/// Sucursal (sede) de una empresa. Código único dentro de la empresa e inmutable; una sucursal no se
/// borra, se desactiva (HU-1.3).
/// </summary>
public sealed class Branch : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public string? CityCode { get; private set; }
    public string? Phone { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Branch() { }

    public static Branch Create(Guid companyId, string code, string name, string? address, string? cityCode = null, string? phone = null)
    {
        if (companyId == Guid.Empty)
            throw new DomainException("VALIDACION", "La sucursal debe pertenecer a una empresa.");
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("VALIDACION", "El código de la sucursal es obligatorio.");
        var branch = new Branch { CompanyId = companyId, Code = code.Trim().ToUpperInvariant() };
        branch.Update(name, address, cityCode, phone);
        return branch;
    }

    public void Update(string name, string? address, string? cityCode, string? phone)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VALIDACION", "El nombre de la sucursal es obligatorio.");
        Name = name.Trim();
        Address = Clean(address);
        CityCode = Clean(cityCode);
        Phone = Clean(phone);
    }

    public void Deactivate() => IsActive = false;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
