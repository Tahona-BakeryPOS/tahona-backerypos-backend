using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Seeding;

/// <summary>
/// Development only: creates the platform administrator with the credentials from user-secrets
/// (<c>Seed:PlatformAdminEmail</c>, <c>Seed:PlatformAdminPassword</c>). Companies are created afterwards
/// through <c>POST /api/v1/plataforma/empresas</c>, the same as in production. It never logs the password.
/// </summary>
public static class DevelopmentSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevelopmentSeeder));

        var email = User.NormalizeEmail(config["Seed:PlatformAdminEmail"]);
        var password = config["Seed:PlatformAdminPassword"];
        if (email is null || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Sin administrador de plataforma de desarrollo: define Seed:PlatformAdminEmail y Seed:PlatformAdminPassword en user-secrets.");
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return;

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        db.Users.Add(User.CreatePlatformAdmin(config["Seed:PlatformAdminName"] ?? "Administrador de plataforma", email, hasher.Hash(password)));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Administrador de plataforma de desarrollo creado: {Email}", email);
    }
}
