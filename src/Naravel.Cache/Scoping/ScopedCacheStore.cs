using Naravel.Cache.Abstractions;

namespace Naravel.Cache.Scoping;

/// <summary>Prefixes all cache keys with an explicit application, user or tenant scope.</summary>
/// <remarks><b>Laravel equivalent:</b> key namespacing used for per-user or per-tenant cache isolation.</remarks>
public sealed class ScopedCacheStore : ICacheStore
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
    public Task<long> DecrementAsync(string key, long by, CancellationToken cancellationToken = default) =>
        _inner.DecrementAsync(BuildKey(key), by, cancellationToken);

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default) => _inner.FlushAsync(cancellationToken);
}
