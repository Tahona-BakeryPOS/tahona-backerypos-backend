using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Tahona.BakeryPOS.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public const int MinSecretLength = 32;

    /// <summary>Nunca en archivos versionados: user-secrets en dev, variable Jwt__Secret en el resto.</summary>
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "BakeryPOS";
    public string Audience { get; set; } = "BakeryPOS.Client";
    public int AccessTokenMinutes { get; set; } = 60;
}

/// <summary>Tokens de acceso firmados con HS256. Lleva una sola empresa por token.</summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _clock;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public IssuedToken CreateAccessToken(AccessTokenRequest request)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", request.FullName),
        };
        if (request.CompanyId is { } companyId)
        {
            claims.Add(new Claim(HttpTenantContext.CompanyClaim, companyId.ToString()));
            // Alcance por sucursal: "*" = todas; si no, una claim por sucursal. Sin empresa no hay alcance.
            if (request.BranchIds is null)
                claims.Add(new Claim(HttpTenantContext.BranchClaim, HttpTenantContext.AllBranches));
            else
                claims.AddRange(request.BranchIds.Distinct().Select(b => new Claim(HttpTenantContext.BranchClaim, b.ToString())));
        }
        if (request.DeviceId is { } deviceId)
            claims.Add(new Claim(HttpTenantContext.DeviceClaim, deviceId.ToString()));
        if (request.CashRegisterId is { } registerId)
            claims.Add(new Claim(HttpTenantContext.CashRegisterClaim, registerId.ToString()));
        if (request.MustChangePassword)
            claims.Add(new Claim(HttpTenantContext.MustChangePasswordClaim, "true"));
        claims.AddRange(request.Permissions.Distinct().Select(p => new Claim(HttpTenantContext.PermissionClaim, p)));

        var now = _clock.GetUtcNow().UtcDateTime;
        var expires = now.Add(request.Lifetime ?? TimeSpan.FromMinutes(_options.AccessTokenMinutes));
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, notBefore: now, expires: expires, signingCredentials: credentials);
        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
