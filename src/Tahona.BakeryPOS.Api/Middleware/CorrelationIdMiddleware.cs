using Serilog.Context;

namespace Tahona.BakeryPOS.Api.Middleware;

/// <summary>
/// Id de correlación por petición: se toma de <c>X-Correlation-Id</c> si el cliente lo envía
/// (válido y corto) o se genera; viaja en la respuesta, en los logs y en los errores.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string Header = "X-Correlation-Id";
    private const string ItemKey = "CorrelationId";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[Header].ToString();
        var id = incoming.Length is > 0 and <= 64 && incoming.All(c => char.IsLetterOrDigit(c) || c == '-')
            ? incoming
            : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = id;
        context.Response.Headers[Header] = id;
        using (LogContext.PushProperty("CorrelationId", id))
        {
            await _next(context);
        }
    }

    public static string Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var v) && v is string s ? s : context.TraceIdentifier;
}
