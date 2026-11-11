using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tahona.BakeryPOS.Api.Middleware;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Infrastructure.Integration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Tahona.BakeryPOS.Api.Filters;

/// <summary>
/// Requires <c>Idempotency-Key</c> and guarantees that retrying the same request returns the same
/// response without repeating the operation. Same key with another body → 409 IDEMPOTENCIA_CONFLICTO;
/// same key still in progress → 409 IDEMPOTENCIA_EN_CURSO. 2xx and 4xx responses are stored; if the
/// operation fails with 5xx or an exception, the key is released so the client can retry.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter
{
    public const string Header = "Idempotency-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[Header].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
        {
            context.Result = Error(http, StatusCodes.Status400BadRequest, "IDEMPOTENCIA_REQUERIDA",
                "Esta operación exige el encabezado Idempotency-Key (máx. 64 caracteres).");
            return;
        }

        var store = http.RequestServices.GetRequiredService<IdempotencyStore>();
        var companyId = http.RequestServices.GetRequiredService<ITenantContext>().CompanyId ?? Guid.Empty;
        var endpoint = $"{http.Request.Method} {context.ActionDescriptor.AttributeRouteInfo?.Template ?? http.Request.Path}";
        var hash = Hash(context.ActionArguments, http.Request.Path);

        var decision = await store.BeginAsync(companyId, key, endpoint, hash, http.RequestAborted);
        switch (decision)
        {
            case IdempotencyDecision.Replay replay:
                http.Response.Headers["Idempotent-Replayed"] = "true";
                context.Result = new ContentResult { StatusCode = replay.StatusCode, Content = replay.Body, ContentType = "application/json" };
                return;
            case IdempotencyDecision.Mismatch:
                context.Result = Error(http, StatusCodes.Status409Conflict, "IDEMPOTENCIA_CONFLICTO",
                    "La clave de idempotencia ya se usó con un contenido distinto.");
                return;
            case IdempotencyDecision.InProgress:
                context.Result = Error(http, StatusCodes.Status409Conflict, "IDEMPOTENCIA_EN_CURSO",
                    "Una petición con la misma clave de idempotencia aún se está procesando.");
                return;
        }

        ActionExecutedContext executed;
        try
        {
            executed = await next();
        }
        catch
        {
            await store.ReleaseAsync(companyId, key, endpoint, CancellationToken.None);
            throw;
        }

        var (status, body) = Describe(executed);
        if (executed.Exception is not null && !executed.ExceptionHandled || status >= 500)
            await store.ReleaseAsync(companyId, key, endpoint, CancellationToken.None);
        else
            await store.CompleteAsync(companyId, key, endpoint, status, body, CancellationToken.None);
    }

    private static (int Status, string? Body) Describe(ActionExecutedContext executed) => executed.Result switch
    {
        ObjectResult o => (o.StatusCode ?? StatusCodes.Status200OK, JsonSerializer.Serialize(o.Value, JsonSerializerOptions.Web)),
        StatusCodeResult s => (s.StatusCode, null),
        _ => (StatusCodes.Status500InternalServerError, null),
    };

    /// <summary>Content fingerprint: route + action arguments (the already deserialized body).</summary>
    private static string Hash(IDictionary<string, object?> arguments, PathString path)
    {
        var args = arguments
            .Where(a => a.Value is not CancellationToken)
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .ToDictionary();
        var canonical = JsonSerializer.Serialize(new { path = path.Value, args }, JsonSerializerOptions.Web);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static ObjectResult Error(HttpContext http, int status, string code, string message) =>
        new(ErrorBody.From(code, message, http)) { StatusCode = status };
}
