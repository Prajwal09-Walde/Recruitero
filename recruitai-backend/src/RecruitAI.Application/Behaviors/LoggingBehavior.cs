using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace RecruitAI.Application.Behaviors;

/// <summary>
/// Structured logging behavior: logs every request with timing and outcome.
/// Slow queries (>500ms) are logged at Warning level.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int SlowRequestThresholdMs = 500;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();

        logger.LogInformation("[MediatR] Handling {RequestName}", requestName);

        try
        {
            var response = await next();
            sw.Stop();

            if (sw.ElapsedMilliseconds > SlowRequestThresholdMs)
                logger.LogWarning("[MediatR] Slow request detected: {RequestName} took {Elapsed}ms",
                    requestName, sw.ElapsedMilliseconds);
            else
                logger.LogInformation("[MediatR] Handled {RequestName} in {Elapsed}ms",
                    requestName, sw.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[MediatR] Error handling {RequestName} after {Elapsed}ms",
                requestName, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
