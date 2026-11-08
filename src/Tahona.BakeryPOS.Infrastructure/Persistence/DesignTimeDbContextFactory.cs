using Microsoft.EntityFrameworkCore.Design;

namespace Tahona.BakeryPOS.Infrastructure.Persistence;

/// <summary>
/// Para <c>dotnet ef migrations add</c>: genera el modelo sin arrancar la API ni conectarse a la base.
/// Para <c>database update</c> usa la variable <c>ConnectionStrings__DefaultConnection</c>.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Sin contraseña: solo sirve para generar migraciones (no se conecta).
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=bakerypos_design;Username=postgres";
        return new AppDbContext(AppDatabase.Options(connection));
    }
}
