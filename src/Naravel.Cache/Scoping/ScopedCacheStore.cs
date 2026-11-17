using Naravel.Cache.Abstractions;

namespace Naravel.Cache.Scoping;

/// <summary>Prefixes all cache keys with an explicit application, user or tenant scope.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> key namespacing used for per-user or per-tenant cache isolation.</para>
/// <para><b>Flush:</b> <see cref="FlushAsync"/> removes the keys of this scope and nothing else. A scope also covers the scopes nested under it, because their prefixes start with its prefix
/// (flushing <c>tenant:a</c> also removes <c>tenant:a:sub</c>). It needs a store that implements <see cref="IPrefixFlushableCacheStore"/>; for any other store it throws
/// <see cref="NotSupportedException"/> instead of clearing more than this scope.</para>
/// </remarks>
public sealed class ScopedCacheStore : ICacheStore, IPrefixFlushableCacheStore
{
    private readonly ICacheStore _inner;
    private readonly string _prefix;

    /// <summary>Creates a scoped view of a cache store.</summary>
    public ScopedCacheStore(ICacheStore inner, string prefix)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _inner = inner;
        _prefix = prefix.TrimEnd(':') + ":";
    }

    /// <inheritdoc />
    public string Name => _inner.Name;

    private string BuildKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _prefix + key;
    }

    /// <inheritdoc />
    public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        _inner.TryGetAsync<T>(BuildKey(key), cancellationToken);

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken = default) =>
        _inner.SetAsync(BuildKey(key), value, ttl, cancellationToken);

    /// <inheritdoc />
    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        _inner.RemoveAsync(BuildKey(key), cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        _inner.ExistsAsync(BuildKey(key), cancellationToken);

    /// <inheritdoc />
    public Task<long> IncrementAsync(string key, long by, CancellationToken cancellationToken = default) =>
        _inner.IncrementAsync(BuildKey(key), by, cancellationToken);

    /// <inheritdoc />
    public Task<long> IncrementAsync(string key, long by, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        _inner.IncrementAsync(BuildKey(key), by, ttl, cancellationToken);

    /// <inheritdoc />
    public Task<long> DecrementAsync(string key, long by, CancellationToken cancellationToken = default) =>
        _inner.DecrementAsync(BuildKey(key), by, cancellationToken);

    /// <summary>Removes every key of this scope, and of the scopes nested under it, and no other key.</summary>
    /// <param name="cancellationToken">Cancels the call; keys already removed stay removed.</param>
    /// <exception cref="NotSupportedException">The underlying store cannot remove keys by prefix. Invalidate a group with <see cref="Tagging.TaggingExtensions"/> instead, or flush through the store itself.</exception>
    /// <remarks>Complete-or-fail and not atomic: see <see cref="IPrefixFlushableCacheStore"/>.</remarks>
    public Task FlushAsync(CancellationToken cancellationToken = default) => FlushUnderAsync(_prefix, cancellationToken);

    /// <summary>Removes every key of this scope that starts with <paramref name="keyPrefix"/>.</summary>
    /// <param name="keyPrefix">The key prefix inside this scope; matched literally.</param>
    /// <param name="cancellationToken">Cancels the call; keys already removed stay removed.</param>
    /// <exception cref="ArgumentException"><paramref name="keyPrefix"/> is <see langword="null"/>, empty or white space; use <see cref="FlushAsync"/> for the whole scope.</exception>
    /// <exception cref="NotSupportedException">The underlying store cannot remove keys by prefix.</exception>
    public Task FlushPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        return FlushUnderAsync(_prefix + keyPrefix, cancellationToken);
    }

    private Task FlushUnderAsync(string fullPrefix, CancellationToken cancellationToken)
    {
        if (_inner is not IPrefixFlushableCacheStore flushable)
        {
            throw new NotSupportedException(
                $"The cache store '{_inner.Name}' ({_inner.GetType().Name}) cannot remove only the keys of one scope. " +
                "Invalidate a group of keys with Tags(...).FlushAsync(), or flush the store itself, which clears more than this scope.");
        }

        return flushable.FlushPrefixAsync(fullPrefix, cancellationToken);
    }
}
