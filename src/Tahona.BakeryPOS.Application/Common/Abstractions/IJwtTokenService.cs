namespace Tahona.BakeryPOS.Application.Common.Abstractions;

/// <summary>Issues access tokens. The login decides which company, permissions and scope go in the token.</summary>
public interface IJwtTokenService
{
    IssuedToken CreateAccessToken(AccessTokenRequest request);
}

/// <param name="CompanyId">Active company; null before choosing a company or for the platform administrator.</param>
/// <param name="BranchIds">Branches it reaches; null = all of the company's.</param>
/// <param name="DeviceId">Device from which the PIN sign-in happened (cash-register session).</param>
/// <param name="CashRegisterId">The device's cash register (cash-register session).</param>
/// <param name="MustChangePassword">Restricted session: it can only change the password.</param>
public sealed record AccessTokenRequest(
    Guid UserId,
    string FullName,
    Guid? CompanyId,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<Guid>? BranchIds = null,
    Guid? DeviceId = null,
    Guid? CashRegisterId = null,
    bool MustChangePassword = false,
    TimeSpan? Lifetime = null);

public sealed record IssuedToken(string Token, DateTime ExpiresAt);

/// <summary>Hash of passwords and PINs (BCrypt). The plain value is never stored or logged.</summary>
public interface IPasswordHasher
{
    string Hash(string secret);

    bool Verify(string secret, string hash);

    /// <summary>Spends the same time as a real verification: avoids revealing by timing that the user does not exist.</summary>
    void VerifyDummy(string secret);
}
