using Enyim.Caching;
using Naravel.Cache.Abstractions;

namespace Naravel.Cache.Memcached;

/// <summary>Memcached lock using atomic add for acquisition and token-checked release.</summary>
/// <remarks><b>Laravel equivalent:</b> a Memcached-backed cache lock; release is not fully atomic on this backend.</remarks>
public sealed class MemcachedLock : ICacheLock
{
    private readonly IMemcachedClient _client;
    private readonly string _keyPrefix;

    /// <summary>Creates a Memcached lock provider.</summary>
    public MemcachedLock(IMemcachedClient client, string prefix)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _client = client;
        _keyPrefix = prefix.TrimEnd(':') + ":lock:";
    }

    /// <inheritdoc />
    public async Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        var token = Guid.NewGuid().ToString("N");
        var acquired = await _client.AddAsync(_keyPrefix + name, token, ttl).ConfigureAwait(false);
        return acquired ? token : null;
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseAsync(string name, string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var key = _keyPrefix + name;
        var current = await _client.GetValueAsync<string>(key).ConfigureAwait(false);
        if (!string.Equals(current, token, StringComparison.Ordinal)) return false;
        await _client.RemoveAsync(key).ConfigureAwait(false);
        return true;
    }
}
