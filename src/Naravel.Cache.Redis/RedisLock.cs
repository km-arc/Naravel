using Naravel.Cache.Abstractions;
using StackExchange.Redis;

namespace Naravel.Cache.Redis;

/// <summary>Redis lock using SET NX, with atomic token-checked release and refresh.</summary>
/// <remarks><b>Laravel equivalent:</b> the Redis implementation of <c>Cache::lock()</c>.</remarks>
public sealed class RedisLock : ICacheLock, IRefreshableCacheLock, IForceReleasableCacheLock, IDisposable
{
    // The part of a lock key after the store prefix. RedisCacheStore skips keys that start with it, so a flush of the cache never releases a held lock.
    internal const string KeyNamespace = "lock:";

    private const string ReleaseScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    // The expiry is set only while the key still holds the caller's token, so a lease that ended is not revived and a lock that another holder took is not touched.
    private const string RefreshScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDatabase _database;
    private readonly string _keyPrefix;

    private readonly bool _ownsConnection;

    /// <summary>Creates a Redis lock provider.</summary>
    /// <param name="multiplexer">The Redis connection.</param>
    /// <param name="prefix">The key prefix.</param>
    /// <param name="database">The Redis database number, or -1 for the connection default.</param>
    /// <param name="ownsConnection">
    /// <see langword="true"/> (the default, as before) when <see cref="Dispose"/> should dispose <paramref name="multiplexer"/>; <see langword="false"/> when it is borrowed from <c>Naravel.Redis</c>.
    /// </param>
    public RedisLock(IConnectionMultiplexer multiplexer, string prefix, int database = -1, bool ownsConnection = true)
    {
        _ownsConnection = ownsConnection;
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _multiplexer = multiplexer;
        _database = multiplexer.GetDatabase(database);
        _keyPrefix = prefix.TrimEnd(':') + ":" + KeyNamespace;
    }

    /// <inheritdoc />
    public async Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        var token = Guid.NewGuid().ToString("N");
        var acquired = await _database.StringSetAsync(_keyPrefix + name, token, ttl, When.NotExists).ConfigureAwait(false);
        return acquired ? token : null;
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseAsync(string name, string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var result = (long)await _database.ScriptEvaluateAsync(ReleaseScript,
            new RedisKey[] { _keyPrefix + name }, new RedisValue[] { token }).ConfigureAwait(false);
        return result == 1;
    }

    /// <inheritdoc />
    public async Task<bool> RefreshAsync(string name, string token, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        var milliseconds = Math.Max(1L, (long)Math.Ceiling(ttl.TotalMilliseconds));
        var result = (long)await _database.ScriptEvaluateAsync(RefreshScript,
            new RedisKey[] { _keyPrefix + name }, new RedisValue[] { token, milliseconds }).ConfigureAwait(false);
        return result == 1;
    }

    /// <inheritdoc />
    public async Task<bool> ForceReleaseAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return await _database.KeyDeleteAsync(_keyPrefix + name).ConfigureAwait(false);
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
