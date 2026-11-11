using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Identity.Auth;

/// <summary>Session durations (the "Jwt" section of the configuration).</summary>
public sealed class SessionOptions
{
    public int RefreshTokenDays { get; set; } = 14;
    public int CashierTokenMinutes { get; set; } = 720;
}

/// <summary>Response of login, company selection, refresh and password change.</summary>
/// <param name="RefreshToken">Backoffice only; the cash-register session is renewed with the PIN.</param>
/// <param name="CompanyId">The token's company; null if one still has to be chosen (see <paramref name="Companies"/>).</param>
public sealed record AuthSessionDto(
    string AccessToken,
    DateTime ExpiresAt,
    string? RefreshToken,
    Guid UserId,
    string FullName,
    Guid? CompanyId,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword,
    bool IsPlatformAdmin,
    IReadOnlyList<CompanySummary> Companies);

/// <summary>Permissions and scope of a membership according to its current assignments.</summary>
/// <param name="BranchIds">null = all branches.</param>
public sealed record Grant(IReadOnlyList<string> Permissions, IReadOnlyList<Guid>? BranchIds, bool HasAnyAssignment);

/// <summary>
/// Builds sessions: computes permissions from the current roles and issues access + refresh token. It does not save:
/// the handler that uses it calls SaveChanges (the session's changes go in the same transaction).
/// </summary>
public sealed class SessionIssuer
{
    private readonly IAppDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly ICompanyDirectory _companies;
    private readonly SessionOptions _options;
    private readonly TimeProvider _clock;

    public SessionIssuer(IAppDbContext db, IJwtTokenService jwt, ICompanyDirectory companies, SessionOptions options, TimeProvider clock)
    {
        _db = db;
        _jwt = jwt;
        _companies = companies;
        _options = options;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Active companies where the user has an active membership.</summary>
    public async Task<IReadOnlyList<CompanySummary>> CompaniesOfAsync(Guid userId, CancellationToken ct)
    {
        var ids = await _db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(m => m.UserId == userId && m.IsActive)
            .Select(m => m.CompanyId)
            .ToListAsync(ct);
        return await _companies.GetActiveAsync(ids, ct);
    }

    /// <summary>The user's active membership in an active company, or null.</summary>
    public async Task<Membership?> ActiveMembershipAsync(Guid userId, Guid companyId, CancellationToken ct)
    {
        var companies = await _companies.GetActiveAsync([companyId], ct);
        if (companies.Count == 0) return null;
        return await _db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
            .FirstOrDefaultAsync(m => m.UserId == userId && m.CompanyId == companyId && m.IsActive, ct);
    }

    /// <summary>Permissions of the current assignments; with <paramref name="branchId"/>, only those that cover that branch.</summary>
    public async Task<Grant> GrantOfAsync(Guid membershipId, Guid? branchId, CancellationToken ct)
    {
        var now = Now;
        var assignments = (await _db.RoleAssignments.IgnoreQueryFilters([QueryFilters.Tenant])
                .Where(a => a.MembershipId == membershipId)
                .ToListAsync(ct))
            .Where(a => a.IsValidAt(now) && (branchId is null || a.Covers(branchId.Value)))
            .ToList();
        if (assignments.Count == 0)
            return new Grant([], [], false);

        var roleIds = assignments.Select(a => a.RoleId).Distinct().ToList();
        var permissions = await _db.Roles.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(r => roleIds.Contains(r.Id))
            .SelectMany(r => r.Permissions)
            .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .Where(code => code != Permissions.PlatformAdmin)
            .Distinct()
            .ToListAsync(ct);

        IReadOnlyList<Guid>? branches = assignments.Any(a => a.BranchId is null)
            ? null
            : assignments.Select(a => a.BranchId!.Value).Distinct().ToList();
        return new Grant(permissions.Order(StringComparer.Ordinal).ToList(), branches, true);
    }

    /// <summary>
    /// Backoffice session. Without a company: a session to choose a company (or a platform one if they are an administrator).
    /// If <paramref name="replacing"/> is given, that refresh token is left rotated.
    /// </summary>
    public async Task<AuthSessionDto> IssueBackofficeAsync(User user, Membership? membership, CancellationToken ct, RefreshToken? replacing = null)
    {
        var now = Now;
        var companies = await CompaniesOfAsync(user.Id, ct);

        IReadOnlyList<string> permissions = [];
        IReadOnlyList<Guid>? branches = null;
        if (user.MustChangePassword)
        {
            membership = null; // restricted session: it can only change the password
        }
        else if (membership is not null)
        {
            var grant = await GrantOfAsync(membership.Id, null, ct);
            permissions = grant.Permissions;
            branches = grant.BranchIds;
        }
        else if (user.IsPlatformAdmin)
        {
            permissions = [Permissions.PlatformAdmin];
        }

        var access = _jwt.CreateAccessToken(new AccessTokenRequest(
            user.Id, user.FullName, membership?.CompanyId, permissions, branches, MustChangePassword: user.MustChangePassword));

        var refreshValue = SecretTokens.NewToken();
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            CompanyId = membership?.CompanyId,
            TokenHash = SecretTokens.Hash(refreshValue),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays),
        };
        _db.RefreshTokens.Add(refresh);
        replacing?.Revoke(now, refresh.Id);

        return new AuthSessionDto(access.Token, access.ExpiresAt, refreshValue, user.Id, user.FullName, membership?.CompanyId,
            permissions, user.MustChangePassword, user.IsPlatformAdmin, companies);
    }

    /// <summary>Cash-register session: permissions of the register's branch, tied to the device; no refresh token.</summary>
    public AuthSessionDto IssueCashier(User user, Membership membership, Grant grant, Guid branchId, Guid deviceId, Guid cashRegisterId)
    {
        var access = _jwt.CreateAccessToken(new AccessTokenRequest(
            user.Id, user.FullName, membership.CompanyId, grant.Permissions, [branchId], deviceId, cashRegisterId,
            Lifetime: TimeSpan.FromMinutes(_options.CashierTokenMinutes)));
        return new AuthSessionDto(access.Token, access.ExpiresAt, null, user.Id, user.FullName, membership.CompanyId,
            grant.Permissions, false, false, []);
    }

    /// <summary>Closes all of the user's backoffice sessions (or only those of one company).</summary>
    public async Task RevokeAllAsync(Guid userId, Guid? companyId, CancellationToken ct)
    {
        var now = Now;
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && (companyId == null || t.CompanyId == companyId))
            .ToListAsync(ct);
        foreach (var token in tokens)
            token.Revoke(now);
    }
}
