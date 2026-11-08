using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Identity.Auth;

/// <summary>Duraciones de sesión (sección "Jwt" de la configuración).</summary>
public sealed class SessionOptions
{
    public int RefreshTokenDays { get; set; } = 14;
    public int CashierTokenMinutes { get; set; } = 720;
}

/// <summary>Respuesta de login, selección de empresa, refresh y cambio de contraseña.</summary>
/// <param name="RefreshToken">Solo backoffice; la sesión de caja se renueva con el PIN.</param>
/// <param name="CompanyId">Empresa del token; null si aún debe elegir una (ver <paramref name="Companies"/>).</param>
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

/// <summary>Permisos y alcance de una pertenencia según sus asignaciones vigentes.</summary>
/// <param name="BranchIds">null = todas las sucursales.</param>
public sealed record Grant(IReadOnlyList<string> Permissions, IReadOnlyList<Guid>? BranchIds, bool HasAnyAssignment);

/// <summary>
/// Arma sesiones: calcula permisos desde los roles vigentes y emite access + refresh token. No guarda:
/// el handler que lo usa llama a SaveChanges (los cambios de la sesión van en la misma transacción).
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

    /// <summary>Empresas activas donde el usuario tiene una pertenencia activa.</summary>
    public async Task<IReadOnlyList<CompanySummary>> CompaniesOfAsync(Guid userId, CancellationToken ct)
    {
        var ids = await _db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(m => m.UserId == userId && m.IsActive)
            .Select(m => m.CompanyId)
            .ToListAsync(ct);
        return await _companies.GetActiveAsync(ids, ct);
    }

    /// <summary>Pertenencia activa del usuario en una empresa activa, o null.</summary>
    public async Task<Membership?> ActiveMembershipAsync(Guid userId, Guid companyId, CancellationToken ct)
    {
        var companies = await _companies.GetActiveAsync([companyId], ct);
        if (companies.Count == 0) return null;
        return await _db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
            .FirstOrDefaultAsync(m => m.UserId == userId && m.CompanyId == companyId && m.IsActive, ct);
    }

    /// <summary>Permisos de las asignaciones vigentes; con <paramref name="branchId"/>, solo las que cubren esa sucursal.</summary>
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
            .Where(code => code != Permissions.PlataformaAdmin)
            .Distinct()
            .ToListAsync(ct);

        IReadOnlyList<Guid>? branches = assignments.Any(a => a.BranchId is null)
            ? null
            : assignments.Select(a => a.BranchId!.Value).Distinct().ToList();
        return new Grant(permissions.Order(StringComparer.Ordinal).ToList(), branches, true);
    }

    /// <summary>
    /// Sesión de backoffice. Sin empresa: sesión para elegir empresa (o de plataforma si es administrador).
    /// Si <paramref name="replacing"/> viene, ese refresh token queda rotado.
    /// </summary>
    public async Task<AuthSessionDto> IssueBackofficeAsync(User user, Membership? membership, CancellationToken ct, RefreshToken? replacing = null)
    {
        var now = Now;
        var companies = await CompaniesOfAsync(user.Id, ct);

        IReadOnlyList<string> permissions = [];
        IReadOnlyList<Guid>? branches = null;
        if (user.MustChangePassword)
        {
            membership = null; // sesión restringida: solo puede cambiar la contraseña
        }
        else if (membership is not null)
        {
            var grant = await GrantOfAsync(membership.Id, null, ct);
            permissions = grant.Permissions;
            branches = grant.BranchIds;
        }
        else if (user.IsPlatformAdmin)
        {
            permissions = [Permissions.PlataformaAdmin];
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

    /// <summary>Sesión de caja: permisos de la sucursal de la caja, ligada al equipo; sin refresh token.</summary>
    public AuthSessionDto IssueCashier(User user, Membership membership, Grant grant, Guid branchId, Guid deviceId, Guid cashRegisterId)
    {
        var access = _jwt.CreateAccessToken(new AccessTokenRequest(
            user.Id, user.FullName, membership.CompanyId, grant.Permissions, [branchId], deviceId, cashRegisterId,
            Lifetime: TimeSpan.FromMinutes(_options.CashierTokenMinutes)));
        return new AuthSessionDto(access.Token, access.ExpiresAt, null, user.Id, user.FullName, membership.CompanyId,
            grant.Permissions, false, false, []);
    }

    /// <summary>Cierra todas las sesiones de backoffice del usuario (o solo las de una empresa).</summary>
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
