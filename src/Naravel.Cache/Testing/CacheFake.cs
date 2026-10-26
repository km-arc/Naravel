using Naravel.Cache.Abstractions;

namespace Naravel.Cache.Testing;

/// <summary>
/// In-memory <see cref="ICacheStore"/> for application tests, with assertions on what is cached.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Cache::fake()</c> / the <c>array</c> cache driver in tests.</para>
/// <para><b>Why it exists:</b> inject it where <see cref="ICacheStore"/> is expected and assert on the keys, with no Redis, no Memcached and no <c>IMemoryCache</c>.</para>
/// <para><b>Time:</b> expiry follows the <see cref="TimeProvider"/> you pass, so a test can move the clock instead of sleeping.</para>
/// <para><b>Not ported on purpose:</b> global fake state; the fake is an ordinary object you pass in.</para>
/// </remarks>
public sealed class CacheFake : ICacheStore
{
    private sealed record Entry(object? Value, DateTimeOffset? ExpiresAt);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates an empty fake store.</summary>
    /// <param name="name">The store name reported by <see cref="Name"/>.</param>
    /// <param name="timeProvider">The clock used for expiry; the system clock when <see langword="null"/>.</param>
    public CacheFake(string name = "fake", TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The keys that are stored and not expired.</summary>
    public IReadOnlyCollection<string> Keys
    {
        get
        {
            lock (_gate)
            {
                PurgeExpired();
                return _entries.Keys.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            if (TryGetLive(key, out var entry))
            {
                if (entry.Value is T typed) return Task.FromResult((true, (T?)typed));
                if (entry.Value is null && default(T) is null) return Task.FromResult((true, default(T)));
            }
        }

        return Task.FromResult((false, default(T)));
    }

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (ttl.HasValue) ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl.Value, TimeSpan.Zero);
        lock (_gate)
        {
            _entries[key] = new Entry(value, ttl.HasValue ? _timeProvider.GetUtcNow() + ttl.Value : null);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            var existed = TryGetLive(key, out _);
            _entries.Remove(key);
            return Task.FromResult(existed);
        }
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate) return Task.FromResult(TryGetLive(key, out _));
    }

    /// <inheritdoc />
    public Task<long> IncrementAsync(string key, long by, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            var current = TryGetLive(key, out var entry) && entry.Value is long number ? number : 0L;
            var next = checked(current + by);
            _entries[key] = new Entry(next, null);
            return Task.FromResult(next);
        }
    }

    /// <inheritdoc />
    public Task<long> IncrementAsync(string key, long by, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(by);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        lock (_gate)
        {
            var live = TryGetLive(key, out var entry) && entry.Value is long;
            var current = live ? (long)entry!.Value! : 0L;
            var expiresAt = live && entry!.ExpiresAt.HasValue ? entry.ExpiresAt : _timeProvider.GetUtcNow() + ttl;
            var next = checked(current + by);
            _entries[key] = new Entry(next, expiresAt);
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
        lock (_gate) _entries.Clear();
        return Task.CompletedTask;
    }

    /// <summary>Asserts that <paramref name="key"/> is stored and not expired, and returns its value.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <exception cref="InvalidOperationException">The key is missing, expired or holds another type.</exception>
    public T AssertHas<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            if (!TryGetLive(key, out var entry))
            {
                throw new InvalidOperationException($"Expected cache key '{key}' to exist, but it does not.");
            }

            return entry.Value is T typed
                ? typed
                : throw new InvalidOperationException(
                    $"Cache key '{key}' holds {entry.Value?.GetType().Name ?? "null"}, not {typeof(T).Name}.");
        }
    }

    /// <summary>Asserts that <paramref name="key"/> is stored and not expired.</summary>
    /// <param name="key">The cache key.</param>
    /// <exception cref="InvalidOperationException">The key is missing or expired.</exception>
    public void AssertHas(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            if (!TryGetLive(key, out _))
            {
                throw new InvalidOperationException($"Expected cache key '{key}' to exist, but it does not.");
            }
        }
    }

    /// <summary>Asserts that <paramref name="key"/> is not stored or has expired.</summary>
    /// <param name="key">The cache key.</param>
    /// <exception cref="InvalidOperationException">The key exists.</exception>
    public void AssertMissing(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            if (TryGetLive(key, out _))
            {
                throw new InvalidOperationException($"Expected cache key '{key}' to be missing, but it exists.");
            }
        }
    }

    /// <summary>Asserts that nothing is stored.</summary>
    /// <exception cref="InvalidOperationException">At least one live key exists.</exception>
    public void AssertEmpty()
    {
        var keys = Keys;
        if (keys.Count != 0)
        {
            throw new InvalidOperationException($"Expected an empty cache, but found {keys.Count} key(s): {string.Join(", ", keys)}.");
        }
    }

    /// <summary>Forgets every stored value.</summary>
    public void Reset()
    {
        lock (_gate) _entries.Clear();
    }

    private bool TryGetLive(string key, out Entry entry)
    {
        if (_entries.TryGetValue(key, out var found))
        {
            if (found.ExpiresAt is { } expiresAt && expiresAt <= _timeProvider.GetUtcNow())
            {
                _entries.Remove(key);
            }
            else
            {
                entry = found;
                return true;
            }
        }

        entry = null!;
        return false;
    }

    private void PurgeExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var key in _entries.Where(pair => pair.Value.ExpiresAt is { } at && at <= now).Select(pair => pair.Key).ToArray())
        {
            _entries.Remove(key);
        }
    }
}
