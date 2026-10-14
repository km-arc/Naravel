using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Locks;
using Naravel.Cache.Stores;
using Naravel.Queue.Coordination;
using Naravel.Queue;
using Naravel.Queue.Dispatch;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration for the Naravel.Queue coordination middleware.</summary>
public static class CoordinationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Cache-backed rate limiter used by <c>RateLimited</c>, the <see cref="IUniqueJobGate"/> that makes
    /// <see cref="Naravel.Queue.Jobs.IUniqueJob"/> enforceable, and checks at startup that the Cache lock and store
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
        // The worker takes the gate as a constructor dependency, so a missing Cache registration surfaces while the hosted
        // services are built, before CoordinationStartupCheck can run. Build the gate here so that failure carries the same message.
        services.TryAddSingleton<IUniqueJobGate>(provider =>
        {
            try
            {
                return new CacheUniqueJobGate(provider.GetRequiredService<ICacheLock>(), provider.GetRequiredService<ICacheStore>());
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(MissingCacheMessage, exception);
            }
        });
        services.AddHostedService<CoordinationStartupCheck>();
        return services;
    }

    private const string MissingCacheMessage =
        "Naravel.Queue.Coordination needs a Cache lock and store. Call services.AddNaravelCache(configuration) " +
        "(and a provider such as AddNaravelRedisCache for coordination across processes) before starting the host.";

    private sealed class CoordinationStartupCheck(IServiceProvider services) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                var locks = services.GetRequiredService<ICacheLock>();
                var store = services.GetRequiredService<ICacheStore>();
                WarnWhenMemoryCoordinatesSharedQueue(locks, store);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(MissingCacheMessage, exception);
            }

            return Task.CompletedTask;
        }

        // PDR-012b section 4: a Memory lock/store is invisible to other processes. Combined with a queue that several processes can
        // share, WithoutOverlapping/RateLimited/IUniqueJob would look active while coordinating nothing, so say so once at startup.
        // The Memory queue driver lives in a package this one does not reference, hence the type-name check.
        private void WarnWhenMemoryCoordinatesSharedQueue(ICacheLock locks, ICacheStore store)
        {
            if (locks is not MemoryLock && store is not MemoryCacheStore) return;

            var queue = services.GetService<QueueManager>();
            string? driverName;
            try { driverName = queue?.Connection().GetType().Name; }
            catch (Exception) { return; }
            if (driverName is null || driverName == "MemoryQueueDriver") return;

            services.GetService<ILoggerFactory>()?.CreateLogger("Naravel.Queue.Coordination").LogWarning(
                "Naravel.Queue.Coordination is using a Memory cache lock/store with the {Driver} queue driver. Locks, rate limits and unique keys are then " +
                "visible inside this process only, so several worker or web processes do not coordinate with each other. Register a shared Cache store " +
                "(for example AddNaravelRedisCache) if more than one process runs jobs.",
                driverName);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
