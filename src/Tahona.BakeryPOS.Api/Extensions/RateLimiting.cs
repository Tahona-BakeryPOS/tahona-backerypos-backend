using System.Text.Json;
using System.Threading.RateLimiting;
using Tahona.BakeryPOS.Api.Middleware;

namespace Tahona.BakeryPOS.Api.Extensions;

/// <summary>
/// Per-IP limit for the anonymous endpoints that receive secrets (login, refresh, pairing, PIN):
/// it slows brute force before it reaches the per-user lockout. Answers 429 LIMITE_EXCEDIDO.
/// </summary>
public static class RateLimiting
{
    public const string Auth = "auth";

    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permitPerMinute = configuration.GetValue("RateLimiting:AuthPermitPerMinute", 10);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                http.Response.ContentType = "application/json";
                var body = ErrorBody.From("LIMITE_EXCEDIDO", "Demasiados intentos. Espera un minuto e intenta de nuevo.", http);
                await http.Response.WriteAsync(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), ct);
            };
            options.AddPolicy(Auth, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitPerMinute, Window = TimeSpan.FromMinutes(1) }));
        });
        return services;
    }
}
