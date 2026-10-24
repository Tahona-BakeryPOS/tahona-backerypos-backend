using BakeryPOS.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace BakeryPOS.Api.Extensions;

/// <summary>
/// Convierte Result/Result&lt;T&gt; del patrón Result en respuestas HTTP apropiadas.
/// </summary>
public static class ResultExtensions
{
    public static IActionResult ToActionResult(this Result result)
    {
        if (result.IsSuccess)
            return new OkResult();

        return ToErrorResult(result.Error);
    }

    public static IActionResult ToActionResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return new OkObjectResult(result.Value);

        return ToErrorResult(result.Error);
    }

    public static IActionResult ToCreatedResult<T>(this Result<T> result, string? location = null)
    {
        if (result.IsSuccess)
        {
            if (location is not null)
                return new CreatedResult(location, result.Value);
            return new ObjectResult(result.Value) { StatusCode = StatusCodes.Status201Created };
        }

        return ToErrorResult(result.Error);
    }

    private static IActionResult ToErrorResult(Error error)
    {
        var statusCode = error.Code switch
        {
            "NO_AUTENTICADO" => StatusCodes.Status401Unauthorized,
            "SIN_PERMISO" => StatusCodes.Status403Forbidden,
            "NO_ENCONTRADO" => StatusCodes.Status404NotFound,
            "CONFLICTO_VERSION" => StatusCodes.Status409Conflict,
            "VALIDACION" => StatusCodes.Status400BadRequest,
            "TURNO_YA_ABIERTO" or "TURNO_CERRADO" or "VENTA_DUPLICADA_DISTINTA"
                or "PAGOS_NO_CUBREN_TOTAL" or "LIMITE_EXCEDIDO" or "PAGO_EXCEDE_TOTAL"
                or "REFERENCIA_REQUERIDA" or "RETIRO_EXCEDE_SALDO" or "CODIGO_DUPLICADO"
                or "VENTA_RECHAZADA_SYNC" => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status500InternalServerError
        };

        return new ObjectResult(new
        {
            codigo = error.Code,
            mensaje = error.Message
        })
        {
            StatusCode = statusCode
        };
    }
}
