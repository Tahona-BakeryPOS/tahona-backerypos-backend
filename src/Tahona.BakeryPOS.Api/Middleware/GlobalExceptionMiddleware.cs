using System.Text.Json;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Application.Payments.Gateway;
using Tahona.BakeryPOS.Domain.Exceptions;
using Tahona.BakeryPOS.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Api.Middleware;

/// <summary>
/// Última barrera: convierte excepciones en el cuerpo de error estándar. Los errores esperados de
/// negocio viajan como Result; aquí llegan validaciones, reglas de dominio, concurrencia y fallas.
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
            ValidationException v => (StatusCodes.Status400BadRequest, ErrorBody.From("VALIDACION", "Los datos proporcionados no son válidos.", context,
                v.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))),
            DomainException d => (ResultExtensions.StatusCodeOf(d.ErrorCode), ErrorBody.From(d.ErrorCode, d.Message, context)),
            DbUpdateConcurrencyException => (StatusCodes.Status412PreconditionFailed,
                ErrorBody.From("CONFLICTO_VERSION", "El registro fue modificado por otro usuario. Recárgalo e intenta de nuevo.", context)),
            // Un intento de escribir datos de otra empresa es un error de programación o un ataque: no se detalla.
            GatewayUnavailableException g => (StatusCodes.Status503ServiceUnavailable,
                ErrorBody.From("PROVEEDOR_NO_DISPONIBLE", g.Message, context)),
            TenantIsolationException => (StatusCodes.Status403Forbidden,
                ErrorBody.From("SIN_PERMISO", "No tiene permisos para realizar esta acción.", context)),
            _ => (StatusCodes.Status500InternalServerError,
                ErrorBody.From("ERROR_INTERNO", "Ocurrió un error interno. Intenta de nuevo.", context)),
        };

        if (status >= 500 || exception is TenantIsolationException)
            _logger.LogError(exception, "Error {Code} en {Path}", body.Codigo, context.Request.Path);
        else
            _logger.LogWarning("Error controlado {Code} en {Path}: {Message}", body.Codigo, context.Request.Path, exception.Message);

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }
}
