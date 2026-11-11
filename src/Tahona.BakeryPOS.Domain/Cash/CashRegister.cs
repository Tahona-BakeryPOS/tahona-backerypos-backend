using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Cash;

/// <summary>
/// A branch's physical cash register. In phase 2 it only exists as a record to pair devices;
/// cash sessions and movements arrive in phase 5.
/// </summary>
public sealed class CashRegister : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    private CashRegister() { }

    public static CashRegister Create(Guid companyId, Guid branchId, string code, string name)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty)
            throw new DomainException("VALIDACION", "La caja debe pertenecer a una empresa y una sucursal.");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            throw new DomainException("VALIDACION", "El código y el nombre de la caja son obligatorios.");
        return new CashRegister { CompanyId = companyId, BranchId = branchId, Code = code.Trim().ToUpperInvariant(), Name = name.Trim() };
    }

    public void Deactivate() => IsActive = false;
}
