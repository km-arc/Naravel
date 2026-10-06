using System.Text;
using System.Text.Json;
using Naravel.Cache.Abstractions;
using StackExchange.Redis;

namespace Naravel.Cache.Redis;

/// <summary>Redis cache store with JSON values and prefix-bounded flush.</summary>
/// <remarks><b>Laravel equivalent:</b> the Redis cache store. Every key and flush scan is restricted to this store's configured prefix.</remarks>
public sealed class RedisCacheStore : ICacheStore, IDisposable
{
    private const string IncrementWithExpiryScript = """
        local value = redis.call('INCRBY', KEYS[1], ARGV[1])
        if redis.call('PTTL', KEYS[1]) < 0 then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        return value
        """;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly string _scanPattern;

    /// <summary>Creates a named Redis cache store.</summary>
    public RedisCacheStore(string name, IConnectionMultiplexer multiplexer, string prefix, int database = -1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        Name = name;
        _multiplexer = multiplexer;
        _database = multiplexer.GetDatabase(database);
        _keyPrefix = prefix.TrimEnd(':') + ":";
        _scanPattern = BuildScanPattern(_keyPrefix);
    }

    /// <inheritdoc />
    public string Name { get; }

    private RedisKey Key(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _keyPrefix + key;
    }

    internal static string EscapeGlob(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\\' or '*' or '?' or '[' or ']') escaped.Append('\\');
            escaped.Append(character);
        }

        return escaped.ToString();
    }

    internal static string BuildScanPattern(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var normalizedPrefix = prefix.TrimEnd(':') + ":";
        return EscapeGlob(normalizedPrefix) + "*";
    }

    /// <inheritdoc />
    public async Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await _database.StringGetAsync(Key(key)).ConfigureAwait(false);
        if (!value.HasValue) return (false, default);
        return (true, JsonSerializer.Deserialize<T>((string)value!));
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var serialized = JsonSerializer.Serialize(value);
        var expiration = ttl.HasValue ? new Expiration(ttl.Value) : Expiration.Default;
        await _database.StringSetAsync(Key(key), serialized, expiration).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _database.KeyDeleteAsync(Key(key)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _database.KeyExistsAsync(Key(key)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> IncrementAsync(string key, long by, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _database.StringIncrementAsync(Key(key), by).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> IncrementAsync(string key, long by, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(by);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        var ttlMilliseconds = Math.Max(1L, checked((long)Math.Ceiling(ttl.TotalMilliseconds)));
        var result = await _database.ScriptEvaluateAsync(IncrementWithExpiryScript,
            [Key(key)], [by, ttlMilliseconds]).ConfigureAwait(false);
        return (long)result;
    }

    /// <inheritdoc />
    public async Task<long> DecrementAsync(string key, long by, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _database.StringDecrementAsync(Key(key), by).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        foreach (var endpoint in _multiplexer.GetEndPoints())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = _multiplexer.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica) continue;

            await foreach (var key in server.KeysAsync(database: _database.Database, pattern: _scanPattern).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _database.KeyDeleteAsync(key).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _multiplexer.Dispose();
}
