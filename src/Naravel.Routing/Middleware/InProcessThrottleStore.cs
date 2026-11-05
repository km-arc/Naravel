using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace Naravel.Routing;

/// <summary>
/// The default <see cref="IThrottleStore"/>: one fixed-window <see cref="PartitionedRateLimiter{TResource}"/> per permit limit and
/// window, counting in the memory of this process. Its behavior is the behavior <c>throttle</c> had before the store seam existed
/// (PDR-024): a restart starts empty and every instance counts alone.
/// </summary>
internal sealed class InProcessThrottleStore : IThrottleStore, IDisposable
{
    private readonly record struct LimiterKey(int PermitLimit, long WindowTicks);

    private readonly ConcurrentDictionary<LimiterKey, PartitionedRateLimiter<string>> _limiters = new();

    public async ValueTask<ThrottleDecision> AttemptAsync(
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(partitionKey);
        var limiter = Get(permitLimit, window);
        using var lease = await limiter.AcquireAsync(partitionKey, 1, cancellationToken).ConfigureAwait(false);
        var remaining = (int)Math.Max(0, limiter.GetStatistics(partitionKey)?.CurrentAvailablePermits ?? 0);

        if (lease.IsAcquired)
        {
            return new ThrottleDecision(true, remaining, TimeSpan.Zero);
        }

        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan value) ? value : window;
        return new ThrottleDecision(false, remaining, retryAfter);
    }

    private PartitionedRateLimiter<string> Get(int permitLimit, TimeSpan window) =>
        _limiters.GetOrAdd(new LimiterKey(permitLimit, window.Ticks), static key =>
            PartitionedRateLimiter.Create<string, string>(partition =>
                RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = key.PermitLimit,
                    Window = TimeSpan.FromTicks(key.WindowTicks),
                    QueueLimit = 0,
                    AutoReplenishment = true
                })));

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values) limiter.Dispose();
        _limiters.Clear();
    }
}
