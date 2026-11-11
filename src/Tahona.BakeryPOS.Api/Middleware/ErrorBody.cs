namespace Tahona.BakeryPOS.Api.Middleware;

/// <summary>Error body of the contract: <c>{ code, message, details?, correlationId }</c>.</summary>
public sealed record ErrorBody(string Code, string Message, IDictionary<string, string[]>? Details, string CorrelationId)
{
    public static ErrorBody From(string code, string message, HttpContext http, IDictionary<string, string[]>? details = null) =>
        new(code, message, details, CorrelationIdMiddleware.Get(http));
}
