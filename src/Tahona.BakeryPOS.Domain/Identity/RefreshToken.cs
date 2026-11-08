namespace Tahona.BakeryPOS.Domain.Identity;

/// <summary>
/// Refresh token rotatorio del backoffice: se guarda solo su hash y cada uso emite uno nuevo
/// (<see cref="ReplacedById"/>). Presentar uno ya rotado indica robo y revoca toda la sesión del usuario.
/// No es auditable fila a fila: es estado de sesión, no dato de negocio.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid UserId { get; init; }

    /// <summary>Empresa de la sesión; null si aún no eligió empresa o es el administrador de plataforma.</summary>
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
