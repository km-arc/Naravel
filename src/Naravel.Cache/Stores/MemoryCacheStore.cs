using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Naravel.Cache.Abstractions;

namespace Naravel.Cache.Stores;

/// <summary>Single-process cache store backed by <see cref="IMemoryCache"/>.</summary>
/// <remarks><b>Laravel equivalent:</b> the array cache driver; entries are shared only within this process.</remarks>
public sealed class MemoryCacheStore : ICacheStore
{
    private readonly IMemoryCache _cache;
    private readonly string _prefix;
    private readonly ConcurrentDictionary<string, byte> _trackedKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object> _keyLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _incrementExpirations = new(StringComparer.Ordinal);

    /// <summary>Creates a named memory store.</summary>
    public MemoryCacheStore(string name, IMemoryCache cache, string? prefix = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(cache);
        Name = name;
        _cache = cache;
        _prefix = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix.TrimEnd(':') + ":";
    }

    /// <inheritdoc />
    public string Name { get; }

    private string BuildKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _prefix + key;
    }

    /// <inheritdoc />
    public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_cache.TryGetValue(BuildKey(key), out var value))
        {
            if (value is T typed) return Task.FromResult((true, (T?)typed));
            if (value is null && default(T) is null) return Task.FromResult((true, default(T)));
        }

        return Task.FromResult((false, default(T)));
    }

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = BuildKey(key);
        _incrementExpirations.TryRemove(fullKey, out _);
        var entryOptions = new MemoryCacheEntryOptions();
        if (ttl.HasValue) entryOptions.AbsoluteExpirationRelativeToNow = ttl.Value;
        RegisterTrackingCallback(entryOptions);
        _cache.Set(fullKey, value, entryOptions);
        _trackedKeys[fullKey] = 0;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = BuildKey(key);
        var existed = _cache.TryGetValue(fullKey, out _);
        _cache.Remove(fullKey);
        _trackedKeys.TryRemove(fullKey, out _);
        _incrementExpirations.TryRemove(fullKey, out _);
        return Task.FromResult(existed);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_cache.TryGetValue(BuildKey(key), out _));
    }

    /// <inheritdoc />
    public Task<long> IncrementAsync(string key, long by, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = BuildKey(key);
        var gate = _keyLocks.GetOrAdd(fullKey, static _ => new object());
        lock (gate)
        {
            var current = _cache.TryGetValue(fullKey, out var value) && value is long number ? number : 0L;
            var next = checked(current + by);
            _incrementExpirations.TryRemove(fullKey, out _);
            _cache.Set(fullKey, next, new MemoryCacheEntryOptions());
            RegisterTrackingCallback(fullKey);
            _trackedKeys[fullKey] = 0;
            return Task.FromResult(next);
        }
    }

    /// <inheritdoc />
    public Task<long> IncrementAsync(string key, long by, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(by);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        var fullKey = BuildKey(key);
        var gate = _keyLocks.GetOrAdd(fullKey, static _ => new object());
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            var hasValue = _cache.TryGetValue(fullKey, out var value) && value is long;
            var hasLiveExpiry = _incrementExpirations.TryGetValue(fullKey, out var currentExpiry) && currentExpiry > now;
            var current = hasValue && (!hasLiveExpiry && !_incrementExpirations.ContainsKey(fullKey) || hasLiveExpiry)
                ? (long)value!
                : 0L;
            var expiresAt = hasLiveExpiry ? currentExpiry : now + ttl;
            var next = checked(current + by);
            var options = new MemoryCacheEntryOptions { AbsoluteExpiration = expiresAt };
            RegisterTrackingCallback(options);
            _cache.Set(fullKey, next, options);
            _incrementExpirations[fullKey] = expiresAt;
            _trackedKeys[fullKey] = 0;
            return Task.FromResult(next);
        }
    }

    /// <inheritdoc />
    public Task<long> DecrementAsync(string key, long by, CancellationToken cancellationToken = default) =>
        IncrementAsync(key, -by, cancellationToken);

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var key in _trackedKeys.Keys)
        {
            _cache.Remove(key);
            _trackedKeys.TryRemove(key, out _);
            _incrementExpirations.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    private void RegisterTrackingCallback(MemoryCacheEntryOptions options) =>
        options.RegisterPostEvictionCallback((evictedKey, _, _, _) => RegisterTrackingCallback(evictedKey));

    private void RegisterTrackingCallback(object? evictedKey)
    {
        if (evictedKey is string key && !_cache.TryGetValue(key, out _))
        {
            _trackedKeys.TryRemove(key, out _);
            _incrementExpirations.TryRemove(key, out _);
        }
    }
}
