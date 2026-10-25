using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Naravel.Cache.Locks;
using Naravel.Cache.Stores;

namespace Naravel.Broadcasting.Tests;

internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    // Waits (lock retries) use the real timers.
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        TimeProvider.System.CreateTimer(callback, state, dueTime, period);
}

internal sealed class FixedOptions(BroadcastingOptions value) : IOptionsMonitor<BroadcastingOptions>
{
    public BroadcastingOptions CurrentValue => value;

    public BroadcastingOptions Get(string? name) => value;

    public IDisposable? OnChange(Action<BroadcastingOptions, string?> listener) => null;
}

public class PresenceStoreTests
{
    private const string Room = "presence-room.1";

    private sealed class Cluster
    {
        public FakeClock Clock { get; } = new(DateTimeOffset.Parse("2026-10-26T09:00:00Z"));
        public BroadcastingOptions Options { get; } = new();
        public PresenceStore A { get; }
        public PresenceStore B { get; }

        public Cluster()
        {
            // Two instances over one cache and one lock, as two servers over Redis.
            var cache = new MemoryCacheStore("shared", new MemoryCache(new MemoryCacheOptions()));
            var locks = new MemoryLock("shared");
            A = new PresenceStore(cache, locks, Clock, new FixedOptions(Options), NullLogger.Instance);
            B = new PresenceStore(cache, locks, Clock, new FixedOptions(Options), NullLogger.Instance);
        }
    }

    [Fact]
    public async Task Another_instance_sees_the_members_a_first_instance_joined()
    {
        var c = new Cluster();

        await c.A.JoinAsync(Room, "c1", new PresenceMember("1", new { name = "Ann" }), default);
        var seen = await c.B.MembersAsync(Room, default);

        seen.Should().ContainSingle();
        seen[0].UserId.Should().Be("1");
        seen[0].Info!.Value.GetProperty("name").GetString().Should().Be("Ann");
    }

    [Fact]
    public async Task Several_connections_of_one_user_are_one_member_and_the_user_leaves_with_the_last_one()
    {
        var c = new Cluster();

        var first = await c.A.JoinAsync(Room, "c1", new PresenceMember("1"), default);
        var second = await c.B.JoinAsync(Room, "c2", new PresenceMember("1"), default);

        first.IsNewMember.Should().BeTrue();
        second.IsNewMember.Should().BeFalse();
        second.Members.Should().ContainSingle();
        (await c.A.LeaveAsync(Room, "c1", default)).Should().BeNull();
        (await c.B.LeaveAsync(Room, "c2", default))!.UserId.Should().Be("1");
        (await c.A.MembersAsync(Room, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Leaving_a_connection_that_never_joined_changes_nothing()
    {
        var c = new Cluster();
        await c.A.JoinAsync(Room, "c1", new PresenceMember("1"), default);

        (await c.B.LeaveAsync(Room, "unknown", default)).Should().BeNull();

        (await c.A.MembersAsync(Room, default)).Should().ContainSingle();
    }

    [Fact]
    public async Task An_entry_that_is_not_refreshed_expires()
    {
        var c = new Cluster();
        await c.A.JoinAsync(Room, "c1", new PresenceMember("1"), default);

        c.Clock.Advance(c.Options.PresenceEntryTtl + TimeSpan.FromSeconds(1));

        (await c.B.MembersAsync(Room, default)).Should().BeEmpty();
        (await c.B.JoinAsync(Room, "c2", new PresenceMember("1"), default)).IsNewMember.Should().BeTrue();
    }

    [Fact]
    public async Task Refreshing_keeps_a_connection_alive_past_its_first_expiry()
    {
        var c = new Cluster();
        var tracker = new PresenceTracker();
        var service = new PresenceService(c.A, tracker);
        await service.JoinAsync(Room, "c1", new PresenceMember("1"), default);

        c.Clock.Advance(TimeSpan.FromSeconds(60));
        await service.RefreshAllAsync(default);
        c.Clock.Advance(TimeSpan.FromSeconds(60));   // 120 seconds in: past the first 90, inside the refreshed 150

        (await c.B.MembersAsync(Room, default)).Should().ContainSingle();
    }

    [Fact]
    public async Task Leaving_everything_removes_the_connection_from_every_channel()
    {
        var c = new Cluster();
        var service = new PresenceService(c.A, new PresenceTracker());
        await service.JoinAsync("presence-a.1", "c1", new PresenceMember("1"), default);
        await service.JoinAsync("presence-b.1", "c1", new PresenceMember("1"), default);

        var departures = await service.LeaveAllAsync("c1", default);

        departures.Select(d => d.Channel).Should().BeEquivalentTo("presence-a.1", "presence-b.1");
        departures.Should().OnlyContain(d => d.Left != null);
        (await c.B.MembersAsync("presence-a.1", default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Parallel_joins_from_two_instances_lose_no_member()
    {
        var c = new Cluster();

        await Task.WhenAll(Enumerable.Range(0, 30).Select(i =>
            (i % 2 == 0 ? c.A : c.B).JoinAsync(Room, $"c{i}", new PresenceMember($"u{i}"), default)));

        (await c.A.MembersAsync(Room, default)).Should().HaveCount(30);
    }

    [Fact]
    public async Task An_entry_lifetime_below_three_seconds_is_refused()
    {
        var c = new Cluster();
        c.Options.PresenceEntryTtl = TimeSpan.FromSeconds(1);

        var act = () => c.A.JoinAsync(Room, "c1", new PresenceMember("1"), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
