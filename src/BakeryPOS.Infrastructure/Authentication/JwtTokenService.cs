using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace BakeryPOS.Infrastructure.Authentication;

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expirationMinutes;

    public JwtTokenService(IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        _secret = jwtSection["Secret"] ?? throw new InvalidOperationException("JWT Secret not configured.");
        _issuer = jwtSection["Issuer"] ?? "BakeryPOS";
        _audience = jwtSection["Audience"] ?? "BakeryPOS.Client";
        _expirationMinutes = int.TryParse(jwtSection["ExpirationMinutes"], out var mins) ? mins : 480; // 8 horas por defecto (turno completo)
    }

    public string GenerateToken(AppUser user, Guid organizationId, Guid branchId, IEnumerable<string> permissions)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("org_id", organizationId.ToString()),
            new("branch_id", branchId.ToString()),
            new("full_name", user.FullName),
        };

        // Agregar cada permiso como claim individual
        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
}
