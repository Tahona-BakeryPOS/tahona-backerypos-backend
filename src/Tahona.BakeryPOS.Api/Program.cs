using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Middleware;
using Tahona.BakeryPOS.Application;
using Tahona.BakeryPOS.Infrastructure;
using Tahona.BakeryPOS.Infrastructure.Persistence;
using Tahona.BakeryPOS.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ── Capas ──
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<ICorrelationIdAccessor, HttpCorrelationIdAccessor>();

// ── API ──
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});
builder.Services.AddOpenApi("v1", options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "BakeryPOS API";
    document.Info.Version = "v1";
    document.Info.Description = "POS SaaS para panaderías — MVP v1.1. Errores: { codigo, mensaje, detalles?, correlationId }.";
    return Task.CompletedTask;
}));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("postgres");
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(CorrelationIdMiddleware.Header, "ETag")));
builder.Services.AddAuthRateLimiting(builder.Configuration);

// Detrás de Caddy (deploy/): el esquema https y la IP real del cliente (límite de intentos de login) llegan en
// X-Forwarded-*. Solo se confía en ellos si ReverseProxy:Enabled: la API no publica su puerto fuera de la red de Docker.
var behindProxy = builder.Configuration.GetValue<bool>("ReverseProxy:Enabled");
if (behindProxy)
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

// Staging y producción: las llaves de Data Protection (cifran las credenciales de Bold y Siigo) deben sobrevivir a un
// redespliegue; sin ellas las credenciales guardadas no se pueden leer.
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing")
    && string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:KeysPath"]))
    throw new InvalidOperationException("DataProtection:KeysPath es obligatorio fuera de desarrollo (variable DataProtection__KeysPath, volumen persistente).");

var app = builder.Build();

// Primer administrador de plataforma de un ambiente nuevo (docs/runbooks.md): crea y termina, no levanta la API.
if (args.Contains(PlatformAdminBootstrap.Command))
{
    var (created, message) = await PlatformAdminBootstrap.CreateAsync(app.Services, Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_NAME"),
        Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_EMAIL"), Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD"));
    Console.WriteLine(message);
    return created ? 0 : 1;
}

if (behindProxy)
    app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapHealthChecks("/health");
app.MapControllers();

// Las migraciones se aplican solas solo en desarrollo; en otros ambientes van como paso del pipeline (bundle).
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await DevelopmentSeeder.SeedAsync(app.Services);
}

await app.RunAsync();
return 0;

/// <summary>Punto de entrada visible para WebApplicationFactory en las pruebas de integración.</summary>
public partial class Program;
