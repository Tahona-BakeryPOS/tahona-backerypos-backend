using Tahona.BakeryPOS.Infrastructure.Persistence;

namespace Tahona.BakeryPOS.Api.Middleware;

/// <summary>Expone el id de correlación de la petición a la capa de persistencia (auditoría).</summary>
public sealed class HttpCorrelationIdAccessor : ICorrelationIdAccessor
{
    private readonly IHttpContextAccessor _http;

    public HttpCorrelationIdAccessor(IHttpContextAccessor http)
    {
        _http = http;
    }

    public string? CorrelationId => _http.HttpContext is { } ctx ? CorrelationIdMiddleware.Get(ctx) : null;
}
