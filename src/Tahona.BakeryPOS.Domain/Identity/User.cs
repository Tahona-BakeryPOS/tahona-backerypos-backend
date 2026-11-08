using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Identity;

/// <summary>
/// Persona que usa el sistema. Es global (no pertenece a una empresa): llega a cada empresa por una
/// <see cref="Membership"/> (D-08). Sin correo ni contraseña solo puede entrar a caja con PIN.
/// </summary>
public sealed class User : AuditableEntity
{
    public string? Email { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string? PasswordHash { get; private set; }
    public bool MustChangePassword { get; private set; }
    public bool IsPlatformAdmin { get; private set; }
    public int FailedLogins { get; private set; }
    public DateTime? LockedUntil { get; private set; }
    public bool IsActive { get; private set; } = true;

    private User() { }

    /// <param name="passwordHash">Hash de la contraseña de un solo uso; se exige cambiarla al entrar.</param>
    public static User Create(string fullName, string? email, string? passwordHash)
    {
        if (email is not null && passwordHash is null)
            throw new DomainException("VALIDACION", "Un usuario con correo necesita una contraseña inicial.");
        var user = new User
        {
            Email = NormalizeEmail(email),
            PasswordHash = passwordHash,
            MustChangePassword = passwordHash is not null,
        };
        user.Rename(fullName);
        return user;
    }

    public static User CreatePlatformAdmin(string fullName, string email, string passwordHash)
    {
        var user = Create(fullName, email, passwordHash);
        user.IsPlatformAdmin = true;
        user.MustChangePassword = false;
        return user;
    }

    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    public bool CanUseBackoffice => Email is not null && PasswordHash is not null;

    public void Rename(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("VALIDACION", "El nombre del usuario es obligatorio.");
        FullName = fullName.Trim();
    }

    public bool IsLockedOut(DateTime now) => LockedUntil > now;

    /// <summary>Cuenta un intento fallido; al quinto bloquea el acceso. Devuelve true si acaba de bloquear.</summary>
    public bool RegisterFailedLogin(DateTime now)
    {
        FailedLogins++;
        if (FailedLogins < LockoutPolicy.MaxFailedAttempts) return false;
        LockedUntil = now + LockoutPolicy.Duration;
        FailedLogins = 0;
        return true;
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLogins = 0;
        LockedUntil = null;
    }

    public void ChangePassword(string newPasswordHash)
    {
        PasswordHash = newPasswordHash;
        MustChangePassword = false;
        RegisterSuccessfulLogin();
    }
}

/// <summary>Bloqueo por intentos fallidos, igual para contraseña y PIN (HU-2.4).</summary>
public static class LockoutPolicy
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(5);
}
