using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Identity;

/// <summary>
/// Role of a membership at a branch (or at all if <see cref="BranchId"/> is null), with validity.
/// Removing an assignment does not delete it: it closes its validity.
/// </summary>
public sealed class RoleAssignment : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid MembershipId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid? BranchId { get; private set; }
    public DateTime ValidFrom { get; private set; }
    public DateTime? ValidTo { get; private set; }

    private RoleAssignment() { }

    public static RoleAssignment Create(Guid companyId, Guid membershipId, Guid roleId, Guid? branchId, DateTime validFrom, DateTime? validTo)
    {
        if (validTo is not null && validTo <= validFrom)
            throw new DomainException("VALIDACION", "La vigencia debe terminar después de empezar.");
        return new RoleAssignment
        {
            CompanyId = companyId,
            MembershipId = membershipId,
            RoleId = roleId,
            BranchId = branchId,
            ValidFrom = validFrom,
            ValidTo = validTo,
        };
    }

    public bool IsValidAt(DateTime now) => ValidFrom <= now && (ValidTo is null || ValidTo > now);

    public bool Covers(Guid branchId) => BranchId is null || BranchId == branchId;

    /// <summary>Ends the validity now (if it had not already ended).</summary>
    public void End(DateTime now)
    {
        if (ValidTo is null || ValidTo > now)
            ValidTo = now < ValidFrom ? ValidFrom : now;
    }
}
