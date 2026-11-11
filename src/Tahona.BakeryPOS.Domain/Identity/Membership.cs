using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Identity;

/// <summary>
/// A user's membership of a company (D-08). The register PIN is per company and is stored as a
/// hash; its attempt lockout is also per company.
/// </summary>
public sealed class Membership : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public string? PinHash { get; private set; }
    public int FailedPinAttempts { get; private set; }
    public DateTime? PinLockedUntil { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Membership() { }

    public static Membership Create(Guid companyId, Guid userId, string? pinHash)
    {
        if (companyId == Guid.Empty || userId == Guid.Empty)
            throw new DomainException("VALIDACION", "La pertenencia necesita empresa y usuario.");
        return new Membership { CompanyId = companyId, UserId = userId, PinHash = pinHash };
    }

    public void SetPin(string pinHash)
    {
        PinHash = pinHash;
        FailedPinAttempts = 0;
        PinLockedUntil = null;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public bool IsPinLocked(DateTime now) => PinLockedUntil > now;

    /// <summary>Counts a wrong PIN; on the fifth it locks for 5 minutes. Returns true if it just locked.</summary>
    public bool RegisterFailedPin(DateTime now)
    {
        FailedPinAttempts++;
        if (FailedPinAttempts < LockoutPolicy.MaxFailedAttempts) return false;
        PinLockedUntil = now + LockoutPolicy.Duration;
        FailedPinAttempts = 0;
        return true;
    }

    public void RegisterSuccessfulPin()
    {
        FailedPinAttempts = 0;
        PinLockedUntil = null;
    }
}
