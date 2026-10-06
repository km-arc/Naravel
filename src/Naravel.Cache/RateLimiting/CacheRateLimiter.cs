using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Naravel.Cache;
using Naravel.Cache.Diagnostics;

namespace Naravel.Cache.RateLimiting;

/// <summary>Cache-backed fixed-window limiter using the selected store and its token-owned lock.</summary>
/// <remarks><b>Laravel equivalent:</b> the cache rate limiter. It exists for shared quotas and intentionally does not replace ASP.NET Core's local rate-limiting middleware.</remarks>
public sealed class CacheRateLimiter : IRateLimiter
{
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);
    private readonly CacheManager _cacheManager;
    private readonly LockManager _lockManager;
    private readonly string? _storeName;

    public CacheRateLimiter(CacheManager cacheManager, LockManager lockManager, string? storeName = null)
    {
        _cacheManager = cacheManager;
        _lockManager = lockManager;
        _storeName = storeName;
    }

    public async Task<RateLimitDecision> AttemptAsync(
        string limiter,
        string subjectKey,
        long maxAttempts,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(limiter);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var startedAt = Stopwatch.GetTimestamp();
        using var activity = CacheTelemetry.ActivitySource.StartActivity("cache.rate_limit.attempt", ActivityKind.Client);
        activity?.SetTag("naravel.cache.rate_limiter", limiter);
        var keys = BuildKeys(limiter, subjectKey);
        var store = _cacheManager.Store(_storeName);
        var decision = default(RateLimitDecision);
        await RunLockedAsync(keys.Lock, async token =>
        {
            var now = DateTimeOffset.UtcNow;
            var (hasCount, count) = await store.TryGetAsync<long>(keys.Count, token).ConfigureAwait(false);
            var (hasStartedAt, startedAtTicks) = await store.TryGetAsync<long>(keys.StartedAt, token).ConfigureAwait(false);
            var startedAtUtc = hasStartedAt
                ? new DateTimeOffset(startedAtTicks, TimeSpan.Zero)
                : now;

            if (hasCount && count >= maxAttempts)
            {
                var retryAfter = NonNegative(startedAtUtc + window - now);
                decision = new RateLimitDecision(false, 0, retryAfter);
                return;
            }

            var current = await store.IncrementAsync(keys.Count, 1, window, token).ConfigureAwait(false);
            if (current == 1 || !hasStartedAt)
            {
                startedAtUtc = now;
                await store.SetAsync(keys.StartedAt, startedAtUtc.UtcTicks, window, token).ConfigureAwait(false);
            }

            decision = new RateLimitDecision(
                true,
                Math.Max(0, maxAttempts - current),
                NonNegative(startedAtUtc + window - DateTimeOffset.UtcNow));
        }, cancellationToken).ConfigureAwait(false);

        var outcome = decision.Allowed ? "allowed" : "rejected";
        var tags = new TagList { { "limiter", limiter }, { "outcome", outcome } };
        if (decision.Allowed) CacheTelemetry.Allowed.Add(1, tags);
        else CacheTelemetry.Rejected.Add(1, tags);
        CacheTelemetry.Duration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, tags);
        activity?.SetTag("naravel.cache.outcome", outcome);
        return decision;
    }

    public async Task ClearAsync(string limiter, string subjectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(limiter);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);
        cancellationToken.ThrowIfCancellationRequested();
        var keys = BuildKeys(limiter, subjectKey);
        var store = _cacheManager.Store(_storeName);
        await RunLockedAsync(keys.Lock, async token =>
        {
            await store.RemoveAsync(keys.Count, token).ConfigureAwait(false);
            await store.RemoveAsync(keys.StartedAt, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunLockedAsync(string lockName, Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        var cacheLock = _lockManager.Driver(_storeName);
        var acquired = await cacheLock.BlockAsync(lockName, LockTtl, LockWait, operation, cancellationToken).ConfigureAwait(false);
        if (!acquired) throw new TimeoutException("Could not acquire the cache rate-limit lock within the configured wait period.");
    }

    private static (string Count, string StartedAt, string Lock) BuildKeys(string limiter, string subjectKey)
    {
        var bytes = Encoding.UTF8.GetBytes(limiter + "\0" + subjectKey);
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        var key = "rate-limit:" + digest;
        return (key + ":count", key + ":started", key + ":lock");
    }

    private static TimeSpan NonNegative(TimeSpan value) => value > TimeSpan.Zero ? value : TimeSpan.Zero;
}
