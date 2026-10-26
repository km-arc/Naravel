using Naravel.Cache.Testing;

namespace Naravel.Cache.Tests;

public sealed class CacheFakeContractTests : CacheStoreContractTests
{
    [Fact]
    public async Task Shared_contract() => await AssertBasicOperationsAsync(new CacheFake());
}

public sealed class CacheFakeTests
{
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static (CacheFake Fake, ManualClock Clock) Create()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
        return (new CacheFake(timeProvider: clock), clock);
    }

    [Fact]
    public async Task Assertions_report_what_is_cached()
    {
        var (fake, _) = Create();
        fake.AssertEmpty();

        await fake.SetAsync("user:1", "Ada", null);

        fake.AssertHas("user:1");
        fake.AssertHas<string>("user:1").Should().Be("Ada");
        fake.Keys.Should().Equal("user:1");
        fake.Invoking(f => f.AssertMissing("user:1")).Should().Throw<InvalidOperationException>();
        fake.Invoking(f => f.AssertHas("user:2")).Should().Throw<InvalidOperationException>();
        fake.Invoking(f => f.AssertHas<int>("user:1")).Should().Throw<InvalidOperationException>();
        fake.Invoking(f => f.AssertEmpty()).Should().Throw<InvalidOperationException>();
        fake.AssertMissing("user:2");
    }

    [Fact]
    public async Task Expiry_follows_the_clock_so_a_test_never_sleeps()
    {
        var (fake, clock) = Create();
        await fake.SetAsync("token", "abc", TimeSpan.FromMinutes(10));

        clock.Advance(TimeSpan.FromMinutes(9));
        (await fake.ExistsAsync("token")).Should().BeTrue();

        clock.Advance(TimeSpan.FromMinutes(1));
        (await fake.ExistsAsync("token")).Should().BeFalse();
        (await fake.TryGetAsync<string>("token")).Found.Should().BeFalse();
        fake.AssertMissing("token");
        fake.AssertEmpty();
    }

    [Fact]
    public async Task Increment_with_ttl_sets_the_expiry_only_when_the_key_is_created()
    {
        var (fake, clock) = Create();
        var ttl = TimeSpan.FromSeconds(60);

        (await fake.IncrementAsync("hits", 1, ttl)).Should().Be(1);
        clock.Advance(TimeSpan.FromSeconds(40));
        (await fake.IncrementAsync("hits", 1, ttl)).Should().Be(2);
        clock.Advance(TimeSpan.FromSeconds(20));

        (await fake.ExistsAsync("hits")).Should().BeFalse("later increments must not extend the original window");
        (await fake.IncrementAsync("hits", 1, ttl)).Should().Be(1);
    }

    [Fact]
    public async Task Flush_and_Reset_forget_every_value()
    {
        var (fake, _) = Create();
        await fake.SetAsync("a", 1, null);
        await fake.SetAsync("b", 2, null);

        await fake.FlushAsync();
        fake.AssertEmpty();

        await fake.SetAsync("c", 3, null);
        fake.Reset();
        fake.AssertEmpty();
    }

    [Fact]
    public async Task Operations_observe_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var fake = new CacheFake();

        await FluentActions.Awaiting(() => fake.SetAsync("a", 1, null, cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        fake.AssertEmpty();
    }
}
