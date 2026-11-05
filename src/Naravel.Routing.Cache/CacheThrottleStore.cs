using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Naravel.Cache;
using Naravel.Cache.Abstractions;

namespace Naravel.Routing.Cache;

/// <summary>
/// An <see cref="IThrottleStore"/> over <see cref="ICacheStore.IncrementAsync(string, long, TimeSpan, CancellationToken)"/> (PDR-024).
/// </summary>
/// <remarks>
/// <para>
/// <b>Algorithm:</b> a fixed window aligned to the clock. The window index is <c>bucket = floor(UtcNow / window)</c> and goes into the
/// key, so one atomic increment is the whole attempt: <c>Allowed = count &lt;= limit</c>, <c>Remaining = limit - count</c>,
/// <c>RetryAfter = window - (UtcNow mod window)</c>. There is no stored window start, no read and no lock, so a burst from one
/// subject does not queue on anything. A rejected attempt is counted too (the counter says how many tried).
/// </para>
/// <para>
/// <b>Key:</b> <c>throttle:{lowercase hex SHA-256 of the partition key}:{bucket}</c>. The partition key contains the endpoint name and
/// the client address, so it is hashed: the key has a fixed length (at most 93 characters) and only characters that every provider
/// accepts, including Memcached. The cache store adds its own prefix in front.
/// </para>
/// <para>
/// <b>Clock:</b> every instance computes the bucket from its own clock, so instances must keep their clocks within the usual NTP
/// skew; a skew of a few milliseconds moves the window boundary for that instance by that much and does nothing else. As with every
/// fixed window, a caller can use the limit at the end of one window and again at the start of the next, up to twice the limit in a
/// short time.
/// </para>
/// <para>
/// <b>Expiry:</b> the counter expires one window after its first increment (the cache sets an expiry only when it
/// creates the key), which is never before the bucket ends and at most one window after it. The key is already unreachable by then,
/// because the next bucket has another key.
/// </para>
/// </remarks>
internal sealed class CacheThrottleStore(CacheManager caches, string storeName, TimeProvider time) : IThrottleStore
{
    // Naravel.Cache creates a store on first use, and a provider may connect while doing that (Naravel.Redis opens the connection
    // synchronously). The throttle guard bounds a store call with StoreTimeout only from the moment the call returns a task, so that
    // first creation must not run on the caller's thread. Until a store has been created once, the call hops to the thread pool first;
    // afterwards the lookup is a dictionary read and the request path has no extra hop. (A store re-created after a cache
    // configuration reload is created inline once; the cost is the same as for any other Naravel.Cache caller.)
    private volatile bool _opened;

    public async ValueTask<ThrottleDecision> AttemptAsync(
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(partitionKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permitLimit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        // Read the clock once, before the store call: the bucket and the time left in it must describe the same window even when the
        // call is slow.
        var ticks = time.GetUtcNow().UtcTicks;
        var bucket = ticks / window.Ticks;
        var untilNextWindow = TimeSpan.FromTicks(window.Ticks - (ticks % window.Ticks));

        var store = await OpenAsync().ConfigureAwait(false);
        var count = await store
            .IncrementAsync(BuildKey(partitionKey, bucket), 1, window, cancellationToken)
            .ConfigureAwait(false);

        var remaining = (int)Math.Clamp(permitLimit - count, 0L, permitLimit);
        return count <= permitLimit
            ? new ThrottleDecision(true, remaining, TimeSpan.Zero)
            : new ThrottleDecision(false, 0, untilNextWindow);
    }

    internal static string BuildKey(string partitionKey, long bucket)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(partitionKey));
        return string.Concat("throttle:", Convert.ToHexStringLower(hash), ":", bucket.ToString(CultureInfo.InvariantCulture));
    }

    private async ValueTask<ICacheStore> OpenAsync()
    {
        if (_opened)
        {
            return caches.Store(storeName);
        }

        await Task.Yield();
        var store = caches.Store(storeName);
        _opened = true;
        return store;
    }
}
