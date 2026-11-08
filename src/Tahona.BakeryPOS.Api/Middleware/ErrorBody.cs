namespace Tahona.BakeryPOS.Api.Middleware;

/// <summary>Cuerpo de error del contrato: <c>{ codigo, mensaje, detalles?, correlationId }</c>.</summary>
public sealed record ErrorBody(string Codigo, string Mensaje, IDictionary<string, string[]>? Detalles, string CorrelationId)
{
    public static ErrorBody From(string code, string message, HttpContext http, IDictionary<string, string[]>? details = null) =>
        new(code, message, details, CorrelationIdMiddleware.Get(http));
}
