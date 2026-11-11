using MediatR;
using Microsoft.Extensions.Logging;

namespace Tahona.BakeryPOS.Application.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        // The request content is not logged: it may carry secrets (e.g. the PIN in LoginByPinCommand).
        _logger.LogInformation("Handling {RequestName}", requestName);

        var response = await next();

        _logger.LogInformation("Handled {RequestName}", requestName);

        return response;
    }
}
