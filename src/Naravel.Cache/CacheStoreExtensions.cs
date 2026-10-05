using Naravel.Cache.Abstractions;

namespace Naravel.Cache;

/// <summary>High-level helpers implemented in terms of <see cref="ICacheStore"/>.</summary>
public static class CacheStoreExtensions
{
    /// <summary>Returns a cached value or creates, stores and returns one.</summary>
    public static async Task<T> RememberAsync<T>(
        this ICacheStore store,
        string key,
        TimeSpan? ttl,
        Func<Task<T>> factory,
        CancellationToken cancellationToken = default) =>
        await store.RememberAsync(key, ttl, _ => factory(), cancellationToken).ConfigureAwait(false);

    /// <summary>Returns a cached value or creates, stores and returns one using the supplied cancellation token.</summary>
    public static async Task<T> RememberAsync<T>(
        this ICacheStore store,
        string key,
        TimeSpan? ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        var (found, value) = await store.TryGetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (found) return value!;

        var fresh = await factory(cancellationToken).ConfigureAwait(false);
        await store.SetAsync(key, fresh, ttl, cancellationToken).ConfigureAwait(false);
        return fresh;
    }

    /// <summary>Returns a cached value or creates one without expiry.</summary>
    public static Task<T> RememberForeverAsync<T>(
        this ICacheStore store,
        string key,
        Func<Task<T>> factory,
        CancellationToken cancellationToken = default) =>
        store.RememberAsync(key, null, factory, cancellationToken);

    /// <summary>Gets a value and removes it when it exists.</summary>
    public static async Task<T?> PullAsync<T>(this ICacheStore store, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var (found, value) = await store.TryGetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (!found) return default;
        await store.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        return value;
    }
}
