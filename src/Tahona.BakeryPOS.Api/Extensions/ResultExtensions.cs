using Tahona.BakeryPOS.Api.Middleware;
using Tahona.BakeryPOS.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Extensions;

/// <summary>
/// Converts Result/Result&lt;T&gt; into HTTP responses with the standard error body
/// <c>{ code, message, correlationId }</c>.
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
        "UNAUTHENTICATED" => StatusCodes.Status401Unauthorized,
        "PERMISSION_DENIED" or "COMPANY_REQUIRED" or "PASSWORD_CHANGE_REQUIRED" or "DEVICE_NOT_PAIRED" => StatusCodes.Status403Forbidden,
        "NOT_FOUND" => StatusCodes.Status404NotFound,
        "VALIDATION" or "IDEMPOTENCY_KEY_REQUIRED" => StatusCodes.Status400BadRequest,
        "IDEMPOTENCY_CONFLICT" or "IDEMPOTENCY_IN_PROGRESS" or "DUPLICATE_CODE" or "DUPLICATE_NIT"
            or "EMAIL_IN_USE" or "DUPLICATE_ASSIGNMENT" or "INITIAL_STOCK_ALREADY_RECORDED"
            or "CASH_SESSION_ALREADY_OPEN" or "CASH_SESSION_CLOSED" or "INVALID_STATE" or "PAYMENT_IN_PROGRESS" => StatusCodes.Status409Conflict,
        "PROVIDER_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
        "VERSION_CONFLICT" => StatusCodes.Status412PreconditionFailed,
        "USER_LOCKED" => StatusCodes.Status423Locked,
        "VERSION_REQUIRED" => StatusCodes.Status428PreconditionRequired,
        "RATE_LIMIT_EXCEEDED" => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status422UnprocessableEntity,
    };

    public static IActionResult ToErrorResult(Error error, HttpContext http) =>
        new ObjectResult(ErrorBody.From(error.Code, error.Message, http)) { StatusCode = StatusCodeOf(error.Code) };
}
