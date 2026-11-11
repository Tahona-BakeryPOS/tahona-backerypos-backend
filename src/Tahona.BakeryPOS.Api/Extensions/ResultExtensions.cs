using Tahona.BakeryPOS.Api.Middleware;
using Tahona.BakeryPOS.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Extensions;

/// <summary>
/// Converts Result/Result&lt;T&gt; into HTTP responses with the standard error body
/// <c>{ codigo, mensaje, correlationId }</c>.
/// </summary>
public static class ResultExtensions
{
    public static IActionResult ToActionResult(this Result result, HttpContext http) =>
        result.IsSuccess ? new NoContentResult() : ToErrorResult(result.Error, http);

    public static IActionResult ToActionResult<T>(this Result<T> result, HttpContext http) =>
        result.IsSuccess ? new OkObjectResult(result.Value) : ToErrorResult(result.Error, http);

    public static IActionResult ToCreatedResult<T>(this Result<T> result, HttpContext http, string? location = null)
    {
        if (result.IsFailure)
            return ToErrorResult(result.Error, http);
        return location is null
            ? new ObjectResult(result.Value) { StatusCode = StatusCodes.Status201Created }
            : new CreatedResult(location, result.Value);
    }

    public static int StatusCodeOf(string code) => code switch
    {
        "NO_AUTENTICADO" => StatusCodes.Status401Unauthorized,
        "SIN_PERMISO" or "EMPRESA_REQUERIDA" or "CAMBIO_CONTRASENA_REQUERIDO" or "DISPOSITIVO_NO_VINCULADO" => StatusCodes.Status403Forbidden,
        "NO_ENCONTRADO" => StatusCodes.Status404NotFound,
        "VALIDACION" or "IDEMPOTENCIA_REQUERIDA" => StatusCodes.Status400BadRequest,
        "IDEMPOTENCIA_CONFLICTO" or "IDEMPOTENCIA_EN_CURSO" or "CODIGO_DUPLICADO" or "NIT_DUPLICADO"
            or "CORREO_EN_USO" or "ASIGNACION_DUPLICADA" or "INVENTARIO_INICIAL_REGISTRADO"
            or "CAJA_YA_ABIERTA" or "CAJA_CERRADA" or "ESTADO_INVALIDO" or "PAGO_EN_CURSO" => StatusCodes.Status409Conflict,
        "PROVEEDOR_NO_DISPONIBLE" => StatusCodes.Status503ServiceUnavailable,
        "CONFLICTO_VERSION" => StatusCodes.Status412PreconditionFailed,
        "USUARIO_BLOQUEADO" => StatusCodes.Status423Locked,
        "VERSION_REQUERIDA" => StatusCodes.Status428PreconditionRequired,
        "LIMITE_EXCEDIDO" => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status422UnprocessableEntity,
    };

    public static IActionResult ToErrorResult(Error error, HttpContext http) =>
        new ObjectResult(ErrorBody.From(error.Code, error.Message, http)) { StatusCode = StatusCodeOf(error.Code) };
}
