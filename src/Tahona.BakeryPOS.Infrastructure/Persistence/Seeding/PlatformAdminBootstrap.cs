using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Seeding;

/// <summary>
/// Crea el primer administrador de plataforma de un ambiente nuevo (staging o producción) con
/// <c>dotnet Tahona.BakeryPOS.Api.dll bootstrap-admin</c> (docs/runbooks.md). Solo si aún no existe ninguno: después, los
/// administradores se gestionan desde la aplicación. Nunca registra la contraseña.
/// </summary>
public static class PlatformAdminBootstrap
{
    public const string Command = "bootstrap-admin";

    /// <returns>Mensaje para la consola y si se creó.</returns>
    public static async Task<(bool Created, string Message)> CreateAsync(IServiceProvider services, string? name, string? email, string? password,
        CancellationToken ct = default)
    {
        var normalized = User.NormalizeEmail(email);
        if (normalized is null || string.IsNullOrWhiteSpace(password) || password.Length < 12)
            return (false, "Define BOOTSTRAP_ADMIN_EMAIL y BOOTSTRAP_ADMIN_PASSWORD (mínimo 12 caracteres).");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Users.AnyAsync(u => u.IsPlatformAdmin, ct))
            return (false, "Ya existe un administrador de plataforma: no se crea otro.");
        if (await db.Users.AnyAsync(u => u.Email == normalized, ct))
            return (false, "Ese correo ya tiene usuario.");

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        db.Users.Add(User.CreatePlatformAdmin(string.IsNullOrWhiteSpace(name) ? "Administrador de plataforma" : name.Trim(), normalized, hasher.Hash(password)));
        await db.SaveChangesAsync(ct);
        return (true, $"Administrador de plataforma creado: {normalized}. Quita BOOTSTRAP_ADMIN_PASSWORD del ambiente.");
    }
}
