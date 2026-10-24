using System.Net;
using System.Text.Json;
using BakeryPOS.Domain.Exceptions;
using FluentValidation;

namespace BakeryPOS.Api.Middleware;

public sealed class GlobalExceptionMiddleware
{
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
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, errorCode, message, details) = exception switch
        {
            ValidationException validationEx => (
                HttpStatusCode.BadRequest,
                "VALIDACION",
                "Error de validación.",
                validationEx.Errors.Select(e => new { campo = e.PropertyName, error = e.ErrorMessage }).ToArray() as object
            ),
            NotFoundException notFoundEx => (
                HttpStatusCode.NotFound,
                "NO_ENCONTRADO",
                notFoundEx.Message,
                (object?)null
            ),
            ConflictException conflictEx => (
                HttpStatusCode.Conflict,
                "CONFLICTO_VERSION",
                conflictEx.Message,
                (object?)null
            ),
            BusinessRuleException businessEx => (
                HttpStatusCode.UnprocessableEntity,
                businessEx.ErrorCode,
                businessEx.Message,
                (object?)null
            ),
            DomainException domainEx => (
                HttpStatusCode.BadRequest,
                "ERROR_DOMINIO",
                domainEx.Message,
                (object?)null
            ),
            UnauthorizedAccessException => (
                HttpStatusCode.Forbidden,
                "SIN_PERMISO",
                "No tiene permisos para realizar esta acción.",
                (object?)null
            ),
            _ => (
                HttpStatusCode.InternalServerError,
                "ERROR_INTERNO",
                "Ocurrió un error interno. Intente nuevamente.",
                (object?)null
            )
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Error no controlado: {Message}", exception.Message);
        }
        else
        {
            _logger.LogWarning(exception, "Error controlado [{ErrorCode}]: {Message}", errorCode, message);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            codigo = errorCode,
            mensaje = message,
            detalles = details,
            traceId = context.TraceIdentifier
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        await context.Response.WriteAsync(json);
    }
}
