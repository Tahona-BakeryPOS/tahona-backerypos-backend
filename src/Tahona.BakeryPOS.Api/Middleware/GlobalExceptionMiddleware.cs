using System.Text.Json;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Application.Payments.Gateway;
using Tahona.BakeryPOS.Domain.Exceptions;
using Tahona.BakeryPOS.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Api.Middleware;

/// <summary>
/// Last barrier: converts exceptions into the standard error body. Expected business errors
/// travel as Result; validations, domain rules, concurrency and failures arrive here.
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            await WriteAsync(context, ex);
        }
    }

    private async Task WriteAsync(HttpContext context, Exception exception)
    {
        var (status, body) = exception switch
        {
            ValidationException v => (StatusCodes.Status400BadRequest, ErrorBody.From("VALIDATION", "Los datos proporcionados no son válidos.", context,
                v.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))),
            DomainException d => (ResultExtensions.StatusCodeOf(d.ErrorCode), ErrorBody.From(d.ErrorCode, d.Message, context)),
            DbUpdateConcurrencyException => (StatusCodes.Status412PreconditionFailed,
                ErrorBody.From("VERSION_CONFLICT", "El registro fue modificado por otro usuario. Recárgalo e intenta de nuevo.", context)),
            // An attempt to write another company's data is a programming error or an attack: it is not detailed.
            GatewayUnavailableException g => (StatusCodes.Status503ServiceUnavailable,
                ErrorBody.From("PROVIDER_UNAVAILABLE", g.Message, context)),
            TenantIsolationException => (StatusCodes.Status403Forbidden,
                ErrorBody.From("PERMISSION_DENIED", "No tiene permisos para realizar esta acción.", context)),
            _ => (StatusCodes.Status500InternalServerError,
                ErrorBody.From("INTERNAL_ERROR", "Ocurrió un error interno. Intenta de nuevo.", context)),
        };

        if (status >= 500 || exception is TenantIsolationException)
            _logger.LogError(exception, "Error {Code} on {Path}", body.Code, context.Request.Path);
        else
            _logger.LogWarning("Handled error {Code} on {Path}: {Message}", body.Code, context.Request.Path, exception.Message);

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }
}
