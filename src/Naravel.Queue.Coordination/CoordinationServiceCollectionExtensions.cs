using Microsoft.Extensions.Hosting;
using Naravel.Cache.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration for the Naravel.Queue coordination middleware.</summary>
public static class CoordinationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Cache-backed rate limiter used by <c>RateLimited</c> and checks at startup that the Cache lock and store
    /// the middleware need are registered, so a missing <c>AddNaravelCache</c> fails at startup instead of on the first job.
    /// </summary>
    /// <remarks>
    /// Call <c>services.AddNaravelCache(configuration)</c> (and a provider such as <c>AddNaravelRedisCache</c> for cross-process
    /// coordination) as well. With a Memory store the coordination holds inside one process only.
    /// </remarks>
    public static IServiceCollection AddQueueCoordination(this IServiceCollection services, string? rateLimiterStore = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNaravelRateLimiter(rateLimiterStore);
        services.AddHostedService<CoordinationStartupCheck>();
        return services;
    }

    private sealed class CoordinationStartupCheck(IServiceProvider services) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                services.GetRequiredService<ICacheLock>();
                services.GetRequiredService<ICacheStore>();
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    "Naravel.Queue.Coordination needs a Cache lock and store. Call services.AddNaravelCache(configuration) " +
                    "(and a provider such as AddNaravelRedisCache for coordination across processes) before starting the host.",
                    exception);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
