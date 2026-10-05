using Enyim.Caching;
using Naravel.Cache.Abstractions;

namespace Naravel.Cache.Memcached;

/// <summary>Memcached cache store using the configured Enyim transcoder and store key prefix.</summary>
/// <remarks><b>Laravel equivalent:</b> the Memcached cache store. Flush clears the entire configured Memcached cluster.</remarks>
public sealed class MemcachedCacheStore : ICacheStore
{
    private readonly IMemcachedClient _client;
    private readonly string _keyPrefix;

    /// <summary>Creates a named Memcached store.</summary>
    public MemcachedCacheStore(string name, IMemcachedClient client, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        Name = name;
        _client = client;
        _keyPrefix = prefix.TrimEnd(':') + ":";
    }

    /// <inheritdoc />
    public string Name { get; }

    private string Key(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _keyPrefix + key;
    }

    /// <inheritdoc />
    public async Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = Key(key);
        var result = await _client.GetAsync<T>(fullKey).ConfigureAwait(false);
        return result.HasValue ? (true, result.Value) : (false, default);
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _client.SetAsync(Key(key), value!, ttl ?? TimeSpan.Zero).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _client.RemoveAsync(Key(key));
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExistsCoreAsync(Key(key));
    }

    /// <inheritdoc />
    public async Task<long> IncrementAsync(string key, long by, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(by);
        return await Task.Run(() => (long)_client.Increment(Key(key), (ulong)by, (ulong)by), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> DecrementAsync(string key, long by, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(by);
        return await Task.Run(() => (long)_client.Decrement(Key(key), 0UL, (ulong)by), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _client.FlushAllAsync();
    }

    private async Task<bool> ExistsCoreAsync(string key) => (await _client.GetAsync<object>(key).ConfigureAwait(false)).HasValue;
}
