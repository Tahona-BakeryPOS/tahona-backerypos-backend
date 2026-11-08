using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Tahona.BakeryPOS.Infrastructure.Persistence;

/// <summary>
/// Configuración de <see cref="AppDbContext"/> sobre PostgreSQL (único motor, igual que la v1).
/// La cadena sale de <c>ConnectionStrings:DefaultConnection</c>.
/// </summary>
public static class AppDatabase
{
    public const string ConnectionStringName = "DefaultConnection";

    public static DbContextOptionsBuilder UseAppDatabase(this DbContextOptionsBuilder options, string connection, bool retryOnFailure = false) =>
        options.UseNpgsql(connection, pg =>
        {
            pg.MigrationsHistoryTable("__ef_migrations_history", "int");
            if (retryOnFailure) pg.EnableRetryOnFailure(3);
        });

    public static DbContextOptions<AppDbContext> Options(string connection) =>
        (DbContextOptions<AppDbContext>)new DbContextOptionsBuilder<AppDbContext>().UseAppDatabase(connection).Options;

    /// <summary>Violación de índice único (23505).</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
