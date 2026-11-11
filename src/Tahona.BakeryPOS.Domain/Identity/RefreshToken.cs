namespace Tahona.BakeryPOS.Domain.Identity;

/// <summary>
/// The backoffice's rotating refresh token: only its hash is stored and each use issues a new one
/// (<see cref="ReplacedById"/>). Presenting one already rotated indicates theft and revokes the user's whole session.
/// It is not auditable row by row: it is session state, not business data.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid UserId { get; init; }

    /// <summary>The session's company; null if it has not chosen a company yet or is the platform administrator.</summary>
    public Guid? CompanyId { get; init; }

    public string TokenHash { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? RevokedAt { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTime now, Guid? replacedById = null)
    {
        RevokedAt ??= now;
        ReplacedById ??= replacedById;
    }
}
