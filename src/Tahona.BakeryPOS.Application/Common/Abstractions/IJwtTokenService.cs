namespace Tahona.BakeryPOS.Application.Common.Abstractions;

/// <summary>Emite tokens de acceso. El login decide qué empresa, permisos y alcance van en el token.</summary>
public interface IJwtTokenService
{
    IssuedToken CreateAccessToken(AccessTokenRequest request);
}

/// <param name="CompanyId">Empresa activa; null antes de elegir empresa o para el administrador de plataforma.</param>
/// <param name="BranchIds">Sucursales a las que alcanza; null = todas las de la empresa.</param>
/// <param name="DeviceId">Equipo desde el que se entró con PIN (sesión de caja).</param>
/// <param name="CashRegisterId">Caja del equipo (sesión de caja).</param>
/// <param name="MustChangePassword">Sesión restringida: solo puede cambiar la contraseña.</param>
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

/// <summary>Hash de contraseñas y PIN (BCrypt). Nunca se guarda ni se registra el valor plano.</summary>
public interface IPasswordHasher
{
    string Hash(string secret);

    bool Verify(string secret, string hash);

    /// <summary>Gasta el mismo tiempo que una verificación real: evita delatar por tiempo que el usuario no existe.</summary>
    void VerifyDummy(string secret);
}
