namespace Naravel.Cache.Abstractions;

/// <summary>A named cache store supporting values, counters, expiry and store-scoped flush.</summary>
/// <remarks><b>Laravel equivalent:</b> <c>Illuminate\Contracts\Cache\Store</c>.</remarks>
public interface ICacheStore
{
    /// <summary>The registered store name.</summary>
    string Name { get; }

    /// <summary>Gets a value and reports whether the key exists.</summary>
    Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores a value, optionally with a time-to-live.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken = default);

    /// <summary>Removes a value.</summary>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Checks whether a key exists.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Atomically increments an integer value by the given amount.</summary>
    Task<long> IncrementAsync(string key, long by, CancellationToken cancellationToken = default);

    /// <summary>Atomically decrements an integer value by the given amount.</summary>
    Task<long> DecrementAsync(string key, long by, CancellationToken cancellationToken = default);

    /// <summary>Flushes this store's configured namespace. Provider limitations may apply.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
