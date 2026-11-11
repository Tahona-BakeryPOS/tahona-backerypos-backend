using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Identity;

/// <summary>
/// A person who uses the system. Global (it does not belong to a company): it reaches each company through a
/// <see cref="Membership"/> (D-08). Without an email or password it can only sign in to a cash register with a PIN.
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

    /// <param name="passwordHash">Hash of the one-time password; changing it is required on first sign-in.</param>
    public static User Create(string fullName, string? email, string? passwordHash)
    {
        if (email is not null && passwordHash is null)
            throw new DomainException("VALIDATION", "Un usuario con correo necesita una contraseña inicial.");
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
            throw new DomainException("VALIDATION", "El nombre del usuario es obligatorio.");
        FullName = fullName.Trim();
    }

    public bool IsLockedOut(DateTime now) => LockedUntil > now;

    /// <summary>Counts a failed attempt; on the fifth it locks access. Returns true if it just locked.</summary>
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

/// <summary>Lockout by failed attempts, the same for password and PIN (US-2.4).</summary>
public static class LockoutPolicy
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(5);
}
