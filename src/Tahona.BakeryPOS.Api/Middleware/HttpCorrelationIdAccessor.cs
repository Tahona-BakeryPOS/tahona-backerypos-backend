using Tahona.BakeryPOS.Infrastructure.Persistence;

namespace Tahona.BakeryPOS.Api.Middleware;

/// <summary>Exposes the request's correlation id to the persistence layer (audit).</summary>
public sealed class HttpCorrelationIdAccessor : ICorrelationIdAccessor
{
    private readonly IHttpContextAccessor _http;

    public HttpCorrelationIdAccessor(IHttpContextAccessor http)
    {
        _http = http;
    }

    public string? CorrelationId => _http.HttpContext is { } ctx ? CorrelationIdMiddleware.Get(ctx) : null;
}
