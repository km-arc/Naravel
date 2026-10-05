using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using StackExchange.Redis;

namespace Naravel.Queue.Redis;

/// <summary>
/// Redis-backed driver - the closest equivalent to Laravel's "redis" queue driver.
/// Data layout per queue (all keys prefixed "netqueue:{queue}:"):
///   messages       HASH   id -> json envelope (source of truth for message content)
///   delayed        ZSET   id, score = AvailableAt (unix ms)          - jobs not due yet
///   ready:{0..9}   LIST   id                                          - one list per priority bucket, 9 = highest
///   reserved       ZSET   id, score = reservation deadline (unix ms)  - jobs currently being worked on
///   failed         LIST   json envelopes that exhausted all attempts
///
/// PopAsync first promotes any due delayed jobs and reclaims any reserved jobs whose visibility
/// timeout expired (e.g. because the worker crashed) back onto the ready lists - a bonus feature
/// on top of what Laravel's own redis driver does out of the box.
/// </summary>
public class RedisQueueDriver : IQueueDriver
{
    private readonly IConnectionMultiplexer _mux;
    private readonly TimeSpan _visibilityTimeout;

    public RedisQueueDriver(IConnectionMultiplexer mux, TimeSpan visibilityTimeout)
    {
        _mux = mux;
        _visibilityTimeout = visibilityTimeout;
    }

    private IDatabase Db => _mux.GetDatabase();

    private static string MessagesKey(string q) => $"netqueue:{q}:messages";
    private static string DelayedKey(string q) => $"netqueue:{q}:delayed";
    private static string ReadyKey(string q, int priority) => $"netqueue:{q}:ready:{Math.Clamp(priority, 0, 9)}";
    private static string ReservedKey(string q) => $"netqueue:{q}:reserved";
    private static string FailedKey(string q) => $"netqueue:{q}:failed";

    private const string PushScript = """
        redis.call('HSET', KEYS[1], ARGV[1], ARGV[2])
        if tonumber(ARGV[3]) > tonumber(ARGV[4]) then
            redis.call('ZADD', KEYS[2], ARGV[3], ARGV[1])
        else
            redis.call('RPUSH', KEYS[3], ARGV[1])
        end
        return 1
        """;

    private const string PopScript = """
        local now = tonumber(ARGV[1])
        local expired = redis.call('ZRANGEBYSCORE', KEYS[2], '-inf', now)
        for _, id in ipairs(expired) do
            if redis.call('ZREM', KEYS[2], id) == 1 then
                local json = redis.call('HGET', KEYS[1], id)
                if json then
                    local message = cjson.decode(json)
                    local priority = math.max(0, math.min(9, tonumber(message.Priority) or 0))
                    redis.call('RPUSH', KEYS[13 - priority], id)
                end
            end
        end

        expired = redis.call('ZRANGEBYSCORE', KEYS[3], '-inf', now)
        for _, id in ipairs(expired) do
            if redis.call('ZREM', KEYS[3], id) == 1 then
                local json = redis.call('HGET', KEYS[1], id)
                if json then
                    local message = cjson.decode(json)
                    local priority = math.max(0, math.min(9, tonumber(message.Priority) or 0))
                    redis.call('RPUSH', KEYS[13 - priority], id)
                end
            end
        end

        for index = 4, 13 do
            while true do
                local id = redis.call('LPOP', KEYS[index])
                if not id then break end
                local json = redis.call('HGET', KEYS[1], id)
                if json then
                    local message = cjson.decode(json)
                    message.ReservedAt = ARGV[3]
                    local updated = cjson.encode(message)
                    redis.call('HSET', KEYS[1], id, updated)
                    redis.call('ZADD', KEYS[3], ARGV[2], id)
                    return updated
                end
            end
        end
        return false
        """;

    private const string AckScript = """
        redis.call('ZREM', KEYS[1], ARGV[1])
        redis.call('HDEL', KEYS[2], ARGV[1])
        return 1
        """;

    private const string ReleaseScript = """
        redis.call('ZREM', KEYS[1], ARGV[1])
        redis.call('HSET', KEYS[2], ARGV[1], ARGV[2])
        if tonumber(ARGV[4]) > tonumber(ARGV[5]) then
            redis.call('ZADD', KEYS[3], ARGV[4], ARGV[1])
        else
            redis.call('RPUSH', KEYS[4], ARGV[1])
        end
        return 1
        """;

    private const string FailScript = """
        redis.call('ZREM', KEYS[1], ARGV[1])
        redis.call('HDEL', KEYS[2], ARGV[1])
        redis.call('RPUSH', KEYS[3], ARGV[2])
        return 1
        """;

    public async Task PushAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = Db;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await db.ScriptEvaluateAsync(PushScript,
            [MessagesKey(message.Queue), DelayedKey(message.Queue), ReadyKey(message.Queue, message.Priority)],
            [message.Id, JsonSerializer.Serialize(message), message.AvailableAt.ToUnixTimeMilliseconds(), nowMs]);
    }

    public async Task<QueuedMessage?> PopAsync(string queue, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = Db;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = await db.ScriptEvaluateAsync(PopScript,
            [MessagesKey(queue), DelayedKey(queue), ReservedKey(queue),
                ReadyKey(queue, 9), ReadyKey(queue, 8), ReadyKey(queue, 7), ReadyKey(queue, 6), ReadyKey(queue, 5),
                ReadyKey(queue, 4), ReadyKey(queue, 3), ReadyKey(queue, 2), ReadyKey(queue, 1), ReadyKey(queue, 0)],
            [nowMs, (DateTimeOffset.UtcNow + _visibilityTimeout).ToUnixTimeMilliseconds(), DateTimeOffset.UtcNow.ToString("O")]);
        if (result.IsNull) return null;
        return JsonSerializer.Deserialize<QueuedMessage>((string)result!);
    }

    public async Task AckAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = Db;
        await db.ScriptEvaluateAsync(AckScript,
            [ReservedKey(message.Queue), MessagesKey(message.Queue)], [message.Id]);
    }

    public async Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = Db;
        message.ReservedAt = null;
        message.AvailableAt = DateTimeOffset.UtcNow + delay;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await db.ScriptEvaluateAsync(ReleaseScript,
            [ReservedKey(message.Queue), MessagesKey(message.Queue), DelayedKey(message.Queue), ReadyKey(message.Queue, message.Priority)],
            [message.Id, JsonSerializer.Serialize(message), message.AvailableAt.ToUnixTimeMilliseconds(), message.AvailableAt.ToUnixTimeMilliseconds(), nowMs]);
    }

    public async Task FailAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = Db;
        await db.ScriptEvaluateAsync(FailScript,
            [ReservedKey(message.Queue), MessagesKey(message.Queue), FailedKey(message.Queue)],
            [message.Id, JsonSerializer.Serialize(message)]);
    }

    public async Task<long> SizeAsync(string queue, CancellationToken cancellationToken = default)
    {
        var db = Db;
        long total = 0;
        for (var p = 0; p <= 9; p++) total += await db.ListLengthAsync(ReadyKey(queue, p));
        total += await db.SortedSetLengthAsync(DelayedKey(queue));
        return total;
    }
}

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the "redis" driver for every configured store whose "Driver" is "redis". Each matching
    /// store needs its own "ConnectionString" and gets its own <see cref="StackExchange.Redis.ConnectionMultiplexer"/> -
    /// two stores can safely point at two different Redis servers. Reads "VisibilityTimeoutSeconds" (default 300).
    /// </summary>
    public static IServiceCollection AddRedisDriver(this IServiceCollection services, IConfiguration configuration, string sectionName = "NaravelQueue")
        => services.AddQueueDriver(configuration, "redis", (_, store) =>
        {
            var connectionString = store.GetRequired("ConnectionString");
            var visibilitySeconds = double.Parse(store.GetOrDefault("VisibilityTimeoutSeconds", "300"));
            var mux = ConnectionMultiplexer.Connect(connectionString);
            return new RedisQueueDriver(mux, TimeSpan.FromSeconds(visibilitySeconds));
        }, sectionName);
}
