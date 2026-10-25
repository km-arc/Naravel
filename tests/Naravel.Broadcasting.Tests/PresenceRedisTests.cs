using Microsoft.Extensions.Logging.Abstractions;
using Naravel.Cache.Redis;
using Naravel.Testing;
using StackExchange.Redis;

namespace Naravel.Broadcasting.Tests;

/// <summary>
/// The presence list on a real Redis, with two instances that each have their own connection, as two servers would.
/// Runs only when NARAVEL_TEST_REDIS is set (the CI services job sets it).
/// </summary>
public class PresenceRedisTests
{
    private sealed class TwoServers : IDisposable
    {
        private readonly IConnectionMultiplexer _first;
        private readonly IConnectionMultiplexer _second;

        public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);
        public BroadcastingOptions Options { get; } = new();
        public PresenceStore A { get; }
        public PresenceStore B { get; }
        public string Channel { get; } = $"presence-redis-{Guid.NewGuid():N}.1";

        public TwoServers()
        {
            var connection = Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!;
            var prefix = "naravel-test-presence-" + Guid.NewGuid().ToString("N");
            _first = ConnectionMultiplexer.Connect(connection);
            _second = ConnectionMultiplexer.Connect(connection);
            A = Store(_first, prefix);
            B = Store(_second, prefix);
        }

        private PresenceStore Store(IConnectionMultiplexer multiplexer, string prefix) => new(
            new RedisCacheStore("presence-test", multiplexer, prefix, ownsConnection: false),
            new RedisLock(multiplexer, prefix, ownsConnection: false),
            Clock,
            new FixedOptions(Options),
            NullLogger.Instance);

        public void Dispose()
        {
            _first.Dispose();
            _second.Dispose();
        }
    }

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task The_second_server_sees_what_the_first_server_joined_including_the_info()
    {
        using var servers = new TwoServers();

        await servers.A.JoinAsync(servers.Channel, "c1", new PresenceMember("1", new { name = "Ann" }), default);
        var seen = await servers.B.MembersAsync(servers.Channel, default);

        seen.Should().ContainSingle();
        seen[0].UserId.Should().Be("1");
        seen[0].Info!.Value.GetProperty("name").GetString().Should().Be("Ann");
    }

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task One_user_on_both_servers_is_one_member_and_leaves_with_the_last_connection()
    {
        using var servers = new TwoServers();

        var first = await servers.A.JoinAsync(servers.Channel, "c1", new PresenceMember("1"), default);
        var second = await servers.B.JoinAsync(servers.Channel, "c2", new PresenceMember("1"), default);

        first.IsNewMember.Should().BeTrue();
        second.IsNewMember.Should().BeFalse();
        (await servers.A.LeaveAsync(servers.Channel, "c1", default)).Should().BeNull();
        (await servers.B.LeaveAsync(servers.Channel, "c2", default))!.UserId.Should().Be("1");
        (await servers.A.MembersAsync(servers.Channel, default)).Should().BeEmpty();
    }

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task Parallel_joins_from_two_servers_lose_no_member()
    {
        using var servers = new TwoServers();

        await Task.WhenAll(Enumerable.Range(0, 30).Select(i =>
            (i % 2 == 0 ? servers.A : servers.B).JoinAsync(servers.Channel, $"c{i}", new PresenceMember($"u{i}"), default)));

        (await servers.A.MembersAsync(servers.Channel, default)).Should().HaveCount(30);
        (await servers.B.MembersAsync(servers.Channel, default)).Should().HaveCount(30);
    }

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task An_entry_that_is_not_refreshed_expires_for_both_servers()
    {
        using var servers = new TwoServers();
        await servers.A.JoinAsync(servers.Channel, "c1", new PresenceMember("1"), default);

        servers.Clock.Advance(servers.Options.PresenceEntryTtl + TimeSpan.FromSeconds(1));

        (await servers.B.MembersAsync(servers.Channel, default)).Should().BeEmpty();
        (await servers.A.MembersAsync(servers.Channel, default)).Should().BeEmpty();
    }
}
