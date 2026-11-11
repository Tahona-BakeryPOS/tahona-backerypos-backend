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
    document.Info.Description = "Bakery POS SaaS — MVP v1.1. Errors: { code, message, details?, correlationId }.";
    return Task.CompletedTask;
}));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("postgres");
builder.Services.AddCors(options => options.AddPolicy("WebClients", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(CorrelationIdMiddleware.Header, "ETag")));
builder.Services.AddAuthRateLimiting(builder.Configuration);

// Behind Caddy (deploy/): the https scheme and the client's real IP (login attempt limit) arrive in
// X-Forwarded-*. They are only trusted if ReverseProxy:Enabled: the API does not publish its port outside the Docker network.
var behindProxy = builder.Configuration.GetValue<bool>("ReverseProxy:Enabled");
if (behindProxy)
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

// Staging and production: the Data Protection keys (they encrypt the Bold and Siigo credentials) must survive a
// redeploy; without them the stored credentials cannot be read.
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing")
    && string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:KeysPath"]))
    throw new InvalidOperationException("DataProtection:KeysPath is required outside development (variable DataProtection__KeysPath, persistent volume).");

var app = builder.Build();

// First platform administrator of a new environment (docs/runbooks.md): it creates and exits, it does not start the API.
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
app.UseCors("WebClients");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapHealthChecks("/health");
app.MapControllers();

// Migrations are only applied automatically in development; in other environments they run as a pipeline step (bundle).
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await DevelopmentSeeder.SeedAsync(app.Services);
}

await app.RunAsync();
return 0;

/// <summary>Visible entry point for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
