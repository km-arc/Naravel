using System.Reflection;
using Naravel.Queue.Drivers;
using Naravel.Queue.Redis;
using StackExchange.Redis;

namespace Naravel.Queue.Providers.Tests;

public sealed class RedisQueueAtomicityTests
{
    [Fact]
    public async Task Push_uses_one_atomic_script_for_message_and_ready_or_delayed_state()
    {
        var (multiplexer, database) = CreateRedisProxy();
        var driver = new RedisQueueDriver(multiplexer, TimeSpan.FromMinutes(1));

        await driver.PushAsync(new QueuedMessage
        {
            Queue = "atomic-test-" + Guid.NewGuid().ToString("N"),
            JobType = "atomic-test",
            Payload = "{}"
        });

        database.ScriptCalls.Should().Be(1);
        database.LastScript.Should().Contain("redis.call('HSET'");
        database.LastScript.Should().Contain("redis.call('ZADD'");
        database.LastScript.Should().Contain("redis.call('RPUSH'");
    }

    [Fact]
    public async Task Pop_uses_one_atomic_script_for_promotion_reclaim_and_reservation()
    {
        var (multiplexer, database) = CreateRedisProxy();
        var driver = new RedisQueueDriver(multiplexer, TimeSpan.FromMinutes(1));

        (await driver.PopAsync("atomic-test-" + Guid.NewGuid().ToString("N"))).Should().BeNull();

        database.ScriptCalls.Should().Be(1);
        database.LastScript.Should().Contain("ZRANGEBYSCORE");
        database.LastScript.Should().Contain("redis.call('LPOP'");
        database.LastScript.Should().Contain("redis.call('ZADD'");
    }

    private static (IConnectionMultiplexer Multiplexer, RedisDatabaseProxy Database) CreateRedisProxy()
    {
        var database = DispatchProxy.Create<IDatabase, RedisDatabaseProxy>();
        var multiplexer = DispatchProxy.Create<IConnectionMultiplexer, RedisMultiplexerProxy>();
        ((RedisMultiplexerProxy)(object)multiplexer).Database = database;
        return (multiplexer, (RedisDatabaseProxy)(object)database);
    }

    public class RedisMultiplexerProxy : DispatchProxy
    {
        public IDatabase? Database { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "GetDatabase" => Database,
            nameof(IDisposable.Dispose) => null,
            _ => throw new NotSupportedException($"Unexpected Redis multiplexer call: {targetMethod?.Name}")
        };
    }

    public class RedisDatabaseProxy : DispatchProxy
    {
        public int ScriptCalls { get; private set; }
        public string? LastScript { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDatabase.ScriptEvaluateAsync))
            {
                ScriptCalls++;
                LastScript = args?[0]?.ToString();
                return Task.FromResult(RedisResult.Create(RedisValue.Null));
            }

            throw new NotSupportedException($"Unexpected Redis database call: {targetMethod?.Name}");
        }
    }
}