using Microsoft.EntityFrameworkCore.Design;

namespace Tahona.BakeryPOS.Infrastructure.Persistence;

/// <summary>
/// For <c>dotnet ef migrations add</c>: it generates the model without starting the API or connecting to the database.
/// For <c>database update</c> it uses the variable <c>ConnectionStrings__DefaultConnection</c>.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // No password: it is only for generating migrations (it does not connect).
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=bakerypos_design;Username=postgres";
        return new AppDbContext(AppDatabase.Options(connection));
    }
}
