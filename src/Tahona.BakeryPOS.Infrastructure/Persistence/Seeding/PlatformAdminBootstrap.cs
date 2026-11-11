using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Seeding;

/// <summary>
/// Creates the first platform administrator of a new environment (staging or production) with
/// <c>dotnet Tahona.BakeryPOS.Api.dll bootstrap-admin</c> (docs/runbooks.md). Only if none exists yet: afterwards,
/// administrators are managed from the application. It never logs the password.
/// </summary>
public static class PlatformAdminBootstrap
{
    public const string Command = "bootstrap-admin";

    /// <returns>Message for the console and whether it was created.</returns>
    public static async Task<(bool Created, string Message)> CreateAsync(IServiceProvider services, string? name, string? email, string? password,
        CancellationToken ct = default)
    {
        var normalized = User.NormalizeEmail(email);
        if (normalized is null || string.IsNullOrWhiteSpace(password) || password.Length < 12)
            return (false, "Set BOOTSTRAP_ADMIN_EMAIL and BOOTSTRAP_ADMIN_PASSWORD (minimum 12 characters).");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Users.AnyAsync(u => u.IsPlatformAdmin, ct))
            return (false, "Ya existe un administrador de plataforma: no se crea otro.");
        if (await db.Users.AnyAsync(u => u.Email == normalized, ct))
            return (false, "Ese correo ya tiene usuario.");

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        db.Users.Add(User.CreatePlatformAdmin(string.IsNullOrWhiteSpace(name) ? "Administrador de plataforma" : name.Trim(), normalized, hasher.Hash(password)));
        await db.SaveChangesAsync(ct);
        return (true, $"Platform administrator created: {normalized}. Remove BOOTSTRAP_ADMIN_PASSWORD from the environment.");
    }
}
