using System.Text;
using Tahona.BakeryPOS.Application.Auditing;
using Tahona.BakeryPOS.Application.Payments.Gateway;
using Tahona.BakeryPOS.Infrastructure.Invoicing;
using Tahona.BakeryPOS.Infrastructure.Payments;
using Tahona.BakeryPOS.Application.Invoicing.Siigo;
using Microsoft.AspNetCore.DataProtection;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Identity.Auth;
using Tahona.BakeryPOS.Infrastructure.Integration;
using Tahona.BakeryPOS.Infrastructure.Persistence;
using Tahona.BakeryPOS.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Tahona.BakeryPOS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString(AppDatabase.ConnectionStringName)
            ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection (PostgreSQL).");

        // Una fábrica por petición (scoped) para que cada contexto reciba el ITenantContext de la petición;
        // el contexto de la petición se comparte entre IAppDbContext e IOutbox (misma transacción).
        services.AddDbContextFactory<AppDbContext>(options => options.UseAppDatabase(connection, retryOnFailure: true),
            ServiceLifetime.Scoped);
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IOutbox>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddHttpContextAccessor();
        services.AddScoped<HttpTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpTenantContext>());
        services.AddScoped<IBackgroundTenant>(sp => sp.GetRequiredService<HttpTenantContext>());
        services.AddScoped<IInboundEventStore, InboundEventStore>();
        services.AddScoped<IdempotencyStore>();
        services.AddScoped<IAuditLogReader, AuditLogReader>();

        // Pagos y facturación: credenciales cifradas con Data Protection. Fuera de desarrollo sus llaves viven en
        // DataProtection:KeysPath (volumen persistente con copia de seguridad, runbook): si se pierden, hay que volver a
        // escribir las llaves de Bold y Siigo. Cliente de Bold sin reintentos automáticos (un cobro repetido llega dos veces).
        var dataProtection = services.AddDataProtection().SetApplicationName("BakeryPOS");
        if (configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        var bold = configuration.GetSection(BoldOptions.Section).Get<BoldOptions>() ?? new BoldOptions();
        services.AddHttpClient<IBoldGateway, BoldGateway>(http =>
        {
            http.BaseAddress = new Uri(bold.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(bold.TimeoutSeconds);
        });

        // Facturación electrónica: cliente de Siigo (token por empresa en memoria, sin reintentos propios).
        var siigo = configuration.GetSection(SiigoOptions.Section).Get<SiigoOptions>() ?? new SiigoOptions();
        services.AddSingleton(siigo);
        services.AddHttpClient<ISiigoClient, SiigoClient>(http =>
        {
            http.BaseAddress = new Uri(siigo.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(siigo.TimeoutSeconds);
        });

        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.Section));
        services.AddSingleton<OutboxProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxProcessor>());

        AddJwt(services, configuration);
        return services;
    }

    private static void AddJwt(IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwt.Secret) || jwt.Secret.Length < JwtOptions.MinSecretLength)
            throw new InvalidOperationException(
                $"Jwt:Secret es obligatorio y debe tener al menos {JwtOptions.MinSecretLength} caracteres (user-secrets o variable Jwt__Secret).");

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton(configuration.GetSection(JwtOptions.Section).Get<SessionOptions>() ?? new SessionOptions());

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "name",
            };
        });
        services.AddAuthorization();
    }
}
