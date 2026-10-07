using System.Globalization;
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
        if (TryGetIntegralType(typeof(T), out var integralType))
        {
            // Integral values are stored as decimal text so that memcached INCR/DECR can operate on them.
            var raw = await _client.GetAsync<object>(fullKey).ConfigureAwait(false);
            if (!raw.HasValue || raw.Value is null) return (false, default);
            var number = Convert.ChangeType(raw.Value, integralType, CultureInfo.InvariantCulture);
            return (true, (T)number);
        }

        var result = await _client.GetAsync<T>(fullKey).ConfigureAwait(false);
        return result.HasValue ? (true, result.Value) : (false, default);
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        object stored = TryGetIntegralType(typeof(T), out _)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)!
            : value!;
        await _client.SetAsync(Key(key), stored, ttl ?? TimeSpan.Zero).ConfigureAwait(false);
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
        var fullKey = Key(key);
        if (await _client.AddAsync(fullKey, Counter(by), TimeSpan.Zero).ConfigureAwait(false)) return by;
        return await Task.Run(() => (long)_client.Increment(fullKey, (ulong)by, (ulong)by), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> IncrementAsync(string key, long by, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(by);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        var fullKey = Key(key);
        if (await _client.AddAsync(fullKey, Counter(by), ttl).ConfigureAwait(false)) return by;
        return await Task.Run(() => (long)_client.Increment(fullKey, (ulong)by, (ulong)by), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> DecrementAsync(string key, long by, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(by);
        var fullKey = Key(key);
        await _client.AddAsync(fullKey, Counter(0), TimeSpan.Zero).ConfigureAwait(false);
        return await Task.Run(() => (long)_client.Decrement(fullKey, 0UL, (ulong)by), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _client.FlushAllAsync();
    }

    // memcached INCR/DECR only work on values stored as decimal ASCII text. Enyim serializes numbers
    // (for example a boxed long) as typed binary, which the server rejects as non-numeric, so every
    // counter is created and written as text.
    private static string Counter(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool TryGetIntegralType(Type type, out Type integralType)
    {
        integralType = Nullable.GetUnderlyingType(type) ?? type;
        return integralType == typeof(long) || integralType == typeof(int) || integralType == typeof(short)
            || integralType == typeof(sbyte) || integralType == typeof(ulong) || integralType == typeof(uint)
            || integralType == typeof(ushort) || integralType == typeof(byte);
    }

    private async Task<bool> ExistsCoreAsync(string key) => (await _client.GetAsync<object>(key).ConfigureAwait(false)).HasValue;
}
