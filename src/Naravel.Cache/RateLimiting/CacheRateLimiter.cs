using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Naravel.Cache;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Diagnostics;

namespace Naravel.Cache.RateLimiting;

/// <summary>Cache-backed fixed-window limiter that decides every attempt with one atomic increment of the selected store.</summary>
/// <remarks>
/// <b>Laravel equivalent:</b> the cache rate limiter. It exists for shared quotas and intentionally does not replace ASP.NET Core's local rate-limiting middleware.
/// <para>
/// <b>Why it takes no lock:</b> a lock is a lease, and an attempt that outlives its lease is no longer exclusive. Each attempt instead calls <c>ICacheStore.IncrementAsync(key, 1, window)</c>,
/// which is atomic in every store and hands every caller its own count. The attempt is admitted when its count is not above the limit, however many instances count at the same moment.
/// A second key only records when the window began, to work out <c>RetryAfter</c>; it takes no part in the decision.
/// </para>
/// <para>
/// <b>Intentionally not ported:</b> Laravel's separate check and hit calls. A caller that checks and then hits can let several callers pass the check together.
/// </para>
/// </remarks>
public sealed class CacheRateLimiter : IRateLimiter
{
    private readonly CacheManager _cacheManager;
    private readonly string? _storeName;

    /// <summary>Creates a rate limiter that uses the given cache manager and an optional named store.</summary>
    /// <param name="cacheManager">The cache manager that resolves the store that holds the counters.</param>
    /// <param name="lockManager">Not used. The limiter no longer takes a lock, so this argument is ignored; it stays so that code that creates the limiter keeps compiling.</param>
    /// <param name="storeName">The named store, or <see langword="null"/> for the default store.</param>
    public CacheRateLimiter(CacheManager cacheManager, LockManager lockManager, string? storeName = null)
    {
        _ = lockManager;
        _cacheManager = cacheManager;
        _storeName = storeName;
    }

    /// <inheritdoc />
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

        // The whole decision is this one atomic call. The window is the lifetime of the counter and is applied only when the call creates the counter,
        // so a later attempt, admitted or rejected, never slides the end of the window. Every attempt receives its own count.
        var current = await store.IncrementAsync(keys.Count, 1, window, cancellationToken).ConfigureAwait(false);
        var windowStart = await WindowStartAsync(store, keys.StartedAt, current, window, cancellationToken).ConfigureAwait(false);
        var retryAfter = NonNegative(windowStart + window - DateTimeOffset.UtcNow);

        var decision = current <= maxAttempts
            ? new RateLimitDecision(true, maxAttempts - current, retryAfter)
            : new RateLimitDecision(false, 0, retryAfter);

        var outcome = decision.Allowed ? "allowed" : "rejected";
        var tags = new TagList { { "limiter", limiter }, { "outcome", outcome } };
        if (decision.Allowed) CacheTelemetry.Allowed.Add(1, tags);
        else CacheTelemetry.Rejected.Add(1, tags);
        CacheTelemetry.Duration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, tags);
        activity?.SetTag("naravel.cache.outcome", outcome);
        return decision;
    }

    /// <inheritdoc />
    public async Task ClearAsync(string limiter, string subjectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(limiter);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);
        cancellationToken.ThrowIfCancellationRequested();
        var keys = BuildKeys(limiter, subjectKey);
        var store = _cacheManager.Store(_storeName);
        await store.RemoveAsync(keys.Count, cancellationToken).ConfigureAwait(false);
        await store.RemoveAsync(keys.StartedAt, cancellationToken).ConfigureAwait(false);
    }

    // The first attempt of a window records when the window began, so that every later attempt can tell how long is left. A later attempt that cannot find a usable record
    // (the first attempt has not written it yet, the key was evicted, or it still holds the previous window) records the current time instead. That can only make RetryAfter
    // longer than the true remainder, never shorter, and it never changes whether an attempt is admitted.
    private static async Task<DateTimeOffset> WindowStartAsync(
        ICacheStore store,
        string startedKey,
        long current,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (current > 1)
        {
            var (found, startedAtTicks) = await store.TryGetAsync<long>(startedKey, cancellationToken).ConfigureAwait(false);
            if (found)
            {
                var recorded = new DateTimeOffset(startedAtTicks, TimeSpan.Zero);
                if (recorded + window > now) return recorded;
            }
        }

        await store.SetAsync(startedKey, now.UtcTicks, window, cancellationToken).ConfigureAwait(false);
        return now;
    }

    private static (string Count, string StartedAt) BuildKeys(string limiter, string subjectKey)
    {
        var bytes = Encoding.UTF8.GetBytes(limiter + "\0" + subjectKey);
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        var key = "rate-limit:" + digest;
        return (key + ":count", key + ":started");
    }

    private static TimeSpan NonNegative(TimeSpan value) => value > TimeSpan.Zero ? value : TimeSpan.Zero;
}
