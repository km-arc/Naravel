using Naravel.Cache.Abstractions;
using StackExchange.Redis;

namespace Naravel.Cache.Redis;

/// <summary>Redis lock using SET NX and atomic token-checked release.</summary>
/// <remarks><b>Laravel equivalent:</b> the Redis implementation of <c>Cache::lock()</c>.</remarks>
public sealed class RedisLock : ICacheLock, IDisposable
{
    private const string ReleaseScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDatabase _database;
    private readonly string _keyPrefix;

    /// <summary>Creates a Redis lock provider.</summary>
    public RedisLock(IConnectionMultiplexer multiplexer, string prefix, int database = -1)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _multiplexer = multiplexer;
        _database = multiplexer.GetDatabase(database);
        _keyPrefix = prefix.TrimEnd(':') + ":lock:";
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
    public void Dispose() => _multiplexer.Dispose();
}
