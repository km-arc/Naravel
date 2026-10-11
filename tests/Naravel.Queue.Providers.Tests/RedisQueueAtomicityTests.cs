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
        database.LastScript.Should().Contain("message.Attempts = (tonumber(message.Attempts) or 0) + 1");
    }

    [Fact]
    public async Task Pop_passes_a_fresh_reservation_id_and_clears_it_when_a_reservation_is_reclaimed()
    {
        var (multiplexer, database) = CreateRedisProxy();
        var driver = new RedisQueueDriver(multiplexer, TimeSpan.FromMinutes(1));

        await driver.PopAsync("fence-test-" + Guid.NewGuid().ToString("N"));
        var first = database.LastValues![3].ToString();
        await driver.PopAsync("fence-test-" + Guid.NewGuid().ToString("N"));
        var second = database.LastValues![3].ToString();

        first.Should().NotBeNullOrEmpty();
        second.Should().NotBe(first);
        database.LastScript.Should().Contain("message.ReservationId = ARGV[4]");
        database.LastScript.Should().Contain("message.ReservationId = nil");
    }

    [Fact]
    public async Task Ack_release_and_fail_pass_the_reservation_id_and_guard_on_it()
    {
        var (multiplexer, database) = CreateRedisProxy();
        var driver = new RedisQueueDriver(multiplexer, TimeSpan.FromMinutes(1));
        QueuedMessage Reserved() => new()
        {
            Queue = "fence-test",
            JobType = "x",
            Payload = "{}",
            ReservationId = "token-1"
        };

        await driver.AckAsync(Reserved());
        database.LastValues![1].ToString().Should().Be("token-1");
        database.LastScript.Should().Contain("stored.ReservationId ~= ARGV[2]");

        await driver.ReleaseAsync(Reserved(), TimeSpan.Zero);
        database.LastValues![5].ToString().Should().Be("token-1");
        database.LastScript.Should().Contain("stored.ReservationId ~= ARGV[6]");

        await driver.FailAsync(Reserved());
        database.LastValues![2].ToString().Should().Be("token-1");
        database.LastScript.Should().Contain("stored.ReservationId ~= ARGV[3]");
    }

    [Fact]
    public async Task A_message_without_a_reservation_id_sends_an_empty_token_and_stays_unfenced()
    {
        var (multiplexer, database) = CreateRedisProxy();
        var driver = new RedisQueueDriver(multiplexer, TimeSpan.FromMinutes(1));

        await driver.AckAsync(new QueuedMessage { Queue = "fence-test", JobType = "x", Payload = "{}" });

        database.LastValues![1].ToString().Should().BeEmpty();
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
        public RedisValue[]? LastValues { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDatabase.ScriptEvaluateAsync))
            {
                ScriptCalls++;
                LastScript = args?[0]?.ToString();
                LastValues = args?[2] as RedisValue[];
                return Task.FromResult(RedisResult.Create(RedisValue.Null));
            }

            throw new NotSupportedException($"Unexpected Redis database call: {targetMethod?.Name}");
        }
    }
}