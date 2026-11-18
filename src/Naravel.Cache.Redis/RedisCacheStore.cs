using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Naravel.Cache.Abstractions;
using StackExchange.Redis;

namespace Naravel.Cache.Redis;

/// <summary>Redis cache store with JSON values and prefix-bounded flush.</summary>
/// <remarks><b>Laravel equivalent:</b> the Redis cache store. Every key and flush scan is restricted to this store's configured prefix.</remarks>
public sealed class RedisCacheStore : ICacheStore, IPrefixFlushableCacheStore, IDisposable
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
    private readonly string _lockKeyPrefix;
    private readonly string _scanPattern;

    private readonly bool _ownsConnection;

    /// <summary>Creates a named Redis cache store.</summary>
    /// <param name="name">The store name.</param>
    /// <param name="multiplexer">The Redis connection.</param>
    /// <param name="prefix">The key prefix.</param>
    /// <param name="database">The Redis database number, or -1 for the connection default.</param>
    /// <param name="ownsConnection">
    /// <see langword="true"/> (the default, as before) when <see cref="Dispose"/> should dispose <paramref name="multiplexer"/>; <see langword="false"/> when the connection is
    /// borrowed from <c>Naravel.Redis</c>, which owns and disposes it.
    /// </param>
    /// <param name="serializerOptions">
    /// Options for the JSON value format. <see langword="null"/> (the default, unchanged behaviour) uses the default reflection resolver. For trimming or Native AOT pass options
    /// whose <see cref="JsonSerializerOptions.TypeInfoResolver"/> is a source-generated <c>JsonSerializerContext</c> covering every cached type; a type without metadata then throws
    /// <see cref="NotSupportedException"/>. Changing the options changes the stored format, so keep them identical across all instances that share a store.
    /// </param>
    public RedisCacheStore(string name, IConnectionMultiplexer multiplexer, string prefix, int database = -1, bool ownsConnection = true, JsonSerializerOptions? serializerOptions = null)
    {
        _serializerOptions = serializerOptions;
        _ownsConnection = ownsConnection;
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        Name = name;
        _multiplexer = multiplexer;
        _database = multiplexer.GetDatabase(database);
        _keyPrefix = prefix.TrimEnd(':') + ":";
        _lockKeyPrefix = _keyPrefix + RedisLock.KeyNamespace;
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

    private readonly JsonSerializerOptions? _serializerOptions;

    private JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)(_serializerOptions ?? DefaultOptions()).GetTypeInfo(typeof(T));

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Without caller options the default reflection resolver is used exactly as before; when reflection is disabled (Native AOT) GetTypeInfo throws NotSupportedException, so a missing resolver fails loudly. Applications that trim or use AOT pass options with a source-generated resolver.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "See IL2026.")]
    private static JsonSerializerOptions DefaultOptions() => JsonSerializerOptions.Default;

    /// <inheritdoc />
    public async Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await _database.StringGetAsync(Key(key)).ConfigureAwait(false);
        if (!value.HasValue) return (false, default);
        return (true, JsonSerializer.Deserialize((string)value!, TypeInfo<T>()));
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var serialized = JsonSerializer.Serialize(value, TypeInfo<T>());
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

    // Keys are removed in bounded batches of single-key deletes, never one multi-key DEL, which a Redis Cluster refuses when the keys sit in different slots.
    private const int DeleteBatchSize = 256;

    /// <summary>Removes every key of this store, which means every key under its prefix except the keys of <see cref="RedisLock"/>.</summary>
    /// <param name="cancellationToken">Cancels the call; keys already removed stay removed.</param>
    /// <exception cref="InvalidOperationException">A Redis primary is not connected. Nothing is removed in that case, because skipping it would leave its keys in place.</exception>
    /// <remarks>
    /// The keys of <see cref="RedisLock"/> live under the same prefix (<c>&lt;prefix&gt;:lock:&lt;name&gt;</c>) and are not touched: clearing the cache must not release a lock that is held.
    /// The call is complete-or-fail but not atomic; see <see cref="IPrefixFlushableCacheStore"/>. It scans the whole keyspace of every primary, so its cost grows with the number of keys in the database.
    /// </remarks>
    public Task FlushAsync(CancellationToken cancellationToken = default) => DeleteMatchingAsync(_scanPattern, cancellationToken);

    /// <summary>Removes every key of this store whose name, after the store prefix, starts with <paramref name="keyPrefix"/>; the keys of <see cref="RedisLock"/> are never removed.</summary>
    /// <param name="keyPrefix">The key prefix, relative to the store; matched literally, so Redis glob characters in it have no special meaning.</param>
    /// <param name="cancellationToken">Cancels the call; keys already removed stay removed.</param>
    /// <exception cref="ArgumentException"><paramref name="keyPrefix"/> is <see langword="null"/>, empty or white space.</exception>
    /// <exception cref="InvalidOperationException">A Redis primary is not connected. Nothing is removed in that case.</exception>
    /// <remarks>Same cost and guarantees as <see cref="FlushAsync"/>.</remarks>
    public Task FlushPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        return DeleteMatchingAsync(EscapeGlob(_keyPrefix + keyPrefix) + "*", cancellationToken);
    }

    private async Task DeleteMatchingAsync(string pattern, CancellationToken cancellationToken)
    {
        // Look at every endpoint before deleting anything: a primary that is down is reported, not skipped, so the call never claims
        // to have cleared keys that it could not reach.
        var primaries = new List<IServer>();
        foreach (var endpoint in _multiplexer.GetEndPoints())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = _multiplexer.GetServer(endpoint);
            if (server.IsReplica) continue;
            if (!server.IsConnected)
            {
                throw new InvalidOperationException(
                    $"Redis server {endpoint} is not connected, so the keys under it cannot be removed. Nothing was removed; repeat the call when the server is back.");
            }

            primaries.Add(server);
        }

        var pending = new List<Task>(DeleteBatchSize);
        foreach (var server in primaries)
        {
            await foreach (var key in server.KeysAsync(database: _database.Database, pattern: pattern).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (key.ToString().StartsWith(_lockKeyPrefix, StringComparison.Ordinal)) continue;
                pending.Add(_database.KeyDeleteAsync(key));
                if (pending.Count < DeleteBatchSize) continue;
                await Task.WhenAll(pending).ConfigureAwait(false);
                pending.Clear();
            }
        }

        await Task.WhenAll(pending).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsConnection)
        {
            _multiplexer.Dispose();
        }
    }
}
