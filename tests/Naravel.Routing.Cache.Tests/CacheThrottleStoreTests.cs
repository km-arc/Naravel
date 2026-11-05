using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Testing;
using Naravel.Foundation;

namespace Naravel.Routing.Cache.Tests;

/// <summary>The clock-aligned window and what the adapter sends to the cache (PDR-024). A manual clock moves time; no test waits for a window.</summary>
public sealed class CacheThrottleStoreTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly DateTimeOffset StartOfMinute = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static async Task<ThrottleDecision> AttemptAsync(IThrottleStore store, string key, int limit) =>
        await store.AttemptAsync(key, limit, Minute, CancellationToken.None);

    [Fact]
    public async Task The_window_is_aligned_to_the_clock_and_retry_after_is_the_time_left_in_it()
    {
        var clock = new ManualClock(Backends.MidMinute);
        using var provider = Backends.Provider(new CacheFake("fake", clock), clock);
        var store = Backends.Throttle(provider);

        (await AttemptAsync(store, "p", 2)).Allowed.Should().BeTrue();
        (await AttemptAsync(store, "p", 2)).Allowed.Should().BeTrue();
        var rejected = await AttemptAsync(store, "p", 2);

        rejected.Allowed.Should().BeFalse();
        rejected.Remaining.Should().Be(0);
        rejected.RetryAfter.Should().Be(TimeSpan.FromSeconds(30), "the window ends at 12:01:00 whatever time the first attempt came");
    }

    [Fact]
    public async Task A_new_window_starts_on_the_boundary_and_the_old_count_does_not_follow()
    {
        var clock = new ManualClock(Backends.MidMinute);
        using var provider = Backends.Provider(new CacheFake("fake", clock), clock);
        var store = Backends.Throttle(provider);
        for (var i = 0; i < 3; i++) await AttemptAsync(store, "p", 2);

        clock.Set(StartOfMinute.AddMilliseconds(59_999));
        var last = await AttemptAsync(store, "p", 2);
        last.Allowed.Should().BeFalse("one millisecond before the boundary it is still the old window");
        last.RetryAfter.Should().Be(TimeSpan.FromMilliseconds(1));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        var next = await AttemptAsync(store, "p", 2);
        next.Allowed.Should().BeTrue("at 12:01:00.000 the next window has started");
        next.Remaining.Should().Be(1);
    }

    [Fact]
    public async Task At_the_very_start_of_a_window_a_rejected_caller_waits_the_whole_window()
    {
        var clock = new ManualClock(StartOfMinute);
        using var provider = Backends.Provider(new CacheFake("fake", clock), clock);
        var store = Backends.Throttle(provider);

        (await AttemptAsync(store, "p", 1)).Allowed.Should().BeTrue();
        var rejected = await AttemptAsync(store, "p", 1);

        rejected.Allowed.Should().BeFalse();
        rejected.RetryAfter.Should().Be(Minute);
    }

    [Fact]
    public async Task A_caller_can_use_the_limit_on_both_sides_of_a_boundary()
    {
        // The documented cost of a fixed window (PDR-024): up to twice the limit in a short time across a boundary. No sliding window here.
        var clock = new ManualClock(StartOfMinute.AddMilliseconds(59_900));
        using var provider = Backends.Provider(new CacheFake("fake", clock), clock);
        var store = Backends.Throttle(provider);

        var before = new[] { await AttemptAsync(store, "p", 2), await AttemptAsync(store, "p", 2), await AttemptAsync(store, "p", 2) };
        clock.Advance(TimeSpan.FromMilliseconds(100));
        var after = new[] { await AttemptAsync(store, "p", 2), await AttemptAsync(store, "p", 2), await AttemptAsync(store, "p", 2) };

        before.Select(d => d.Allowed).Should().Equal(true, true, false);
        after.Select(d => d.Allowed).Should().Equal(true, true, false);
    }

    [Fact]
    public async Task A_skewed_clock_moves_the_boundary_for_that_instance_and_nothing_else()
    {
        // Two instances on one shared store; instance B's clock runs 5 ms ahead. Instance B starts the next window 5 ms early and keeps its own
        // count there; every other behaviour is the same (PDR-024).
        var truth = new ManualClock(StartOfMinute.AddMilliseconds(59_990));
        var shared = new CacheFake("shared", truth);
        var fast = new ManualClock(truth.GetUtcNow().AddMilliseconds(5));
        using var a = Backends.Provider(shared, truth);
        using var b = Backends.Provider(shared, fast);
        var onTime = Backends.Throttle(a);
        var ahead = Backends.Throttle(b);

        (await AttemptAsync(onTime, "p", 1)).Allowed.Should().BeTrue();
        (await AttemptAsync(ahead, "p", 1)).Allowed.Should().BeFalse("both are still in the same window 10 ms before the boundary (the fast clock reads 59.995)");

        truth.Set(StartOfMinute.AddMilliseconds(59_996));
        fast.Set(truth.GetUtcNow().AddMilliseconds(5));
        (await AttemptAsync(ahead, "p", 1)).Allowed.Should().BeTrue("the fast instance is already in the next window");
        (await AttemptAsync(onTime, "p", 1)).Allowed.Should().BeFalse("the on-time instance is still in the old window");
    }

    [Fact]
    public async Task One_attempt_is_one_increment_with_the_window_as_expiry_and_nothing_else()
    {
        var clock = new ManualClock(Backends.MidMinute);
        var recording = new RecordingStore(new CacheFake("fake", clock));
        using var provider = Backends.Provider(recording, clock);
        var store = Backends.Throttle(provider);

        for (var i = 0; i < 3; i++) await AttemptAsync(store, "login|/login|203.0.113.7", 2);

        recording.Calls.Should().Equal("increment", "increment", "increment");
        recording.Increments.Select(call => call.Ttl).Should().OnlyContain(ttl => ttl == Minute);
        recording.Increments.Select(call => call.Key).Distinct().Should().HaveCount(1);
    }

    [Fact]
    public async Task A_rejected_attempt_is_counted_too_and_remaining_never_goes_negative()
    {
        var clock = new ManualClock(Backends.MidMinute);
        var cache = new CacheFake("fake", clock);
        using var provider = Backends.Provider(cache, clock);
        var store = Backends.Throttle(provider);

        ThrottleDecision last = default;
        for (var i = 0; i < 5; i++) last = await AttemptAsync(store, "p", 2);

        last.Allowed.Should().BeFalse();
        last.Remaining.Should().Be(0);
        var key = "app:" + CacheThrottleStore.BuildKey("p", Backends.MidMinute.UtcTicks / Minute.Ticks);
        cache.AssertHas<long>(key).Should().Be(5);
    }

    [Fact]
    public async Task The_counter_lives_until_its_window_has_ended_and_not_much_longer()
    {
        var clock = new ManualClock(StartOfMinute);
        var cache = new CacheFake("fake", clock);
        using var provider = Backends.Provider(cache, clock);
        var store = Backends.Throttle(provider);

        await AttemptAsync(store, "p", 5);
        clock.Set(StartOfMinute.AddMilliseconds(59_900));
        cache.Keys.Should().HaveCount(1, "a counter created at the start of its window must still be there when the window ends");

        clock.Set(StartOfMinute.AddMilliseconds(60_100));
        cache.Keys.Should().BeEmpty("the key expires one window after it was created and the next window uses another key");
    }

    [Fact]
    public async Task Keys_are_hashed_and_follow_the_memcached_key_rules()
    {
        var clock = new ManualClock(Backends.MidMinute);
        var cache = new CacheFake("fake", clock);
        using var provider = Backends.Provider(cache, clock);
        var store = Backends.Throttle(provider);
        var partitions = new[]
        {
            "api|" + new string('x', 10_000) + "|203.0.113.7",
            "login|GET /ünïcode/路径/😀|2001:db8::1",
            "ctl|has space\r\nand\ttabs and \0 nul|user 42",
            "a|b|c",
            string.Empty
        };

        foreach (var partition in partitions) await AttemptAsync(store, partition, 5);

        cache.Keys.Should().HaveCount(partitions.Length, "every partition gets its own key");
        foreach (var key in cache.Keys)
        {
            Regex.IsMatch(key, @"^app:throttle:[0-9a-f]{64}:\d+$").Should().BeTrue($"'{key}' is a hash and a bucket, nothing the client chose");
            (key.Length <= 250).Should().BeTrue("Memcached refuses keys longer than 250 characters");
        }

        cache.Keys.Any(key => key.Contains("2001", StringComparison.Ordinal) || key.Contains("203.0.113", StringComparison.Ordinal))
            .Should().BeFalse("a client address is not written into the key");
    }

    [Fact]
    public void The_same_partition_and_window_share_a_key_and_nothing_else_does()
    {
        CacheThrottleStore.BuildKey("p", 7).Should().Be(CacheThrottleStore.BuildKey("p", 7));
        CacheThrottleStore.BuildKey("p", 7).Should().NotBe(CacheThrottleStore.BuildKey("p", 8));
        CacheThrottleStore.BuildKey("p", 7).Should().NotBe(CacheThrottleStore.BuildKey("q", 7));
    }

    [Fact]
    public async Task A_burst_of_one_subject_runs_its_store_calls_at_the_same_time_and_admits_exactly_the_limit()
    {
        // The store holds each increment until 20 are in flight together. An adapter that took a lock around the call would let one in at a
        // time, the meeting would never happen and the burst would time out: the throttle must not queue the caller it exists to stop.
        var clock = new ManualClock(Backends.MidMinute);
        var meeting = new MeetingStore(new CacheFake("fake", clock), release: 20);
        using var provider = Backends.Provider(meeting, clock);
        var store = Backends.Throttle(provider);

        var burst = Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => AttemptAsync(store, "burst|/x|203.0.113.9", 5))));
        var finished = await Task.WhenAny(burst, Task.Delay(TimeSpan.FromSeconds(20)));

        finished.Should().BeSameAs(burst, "20 attempts of one subject were all inside the store at once");
        var decisions = await burst;
        decisions.Count(d => d.Allowed).Should().Be(5);
        decisions.Count(d => !d.Allowed).Should().Be(15);
        meeting.Calls.Should().HaveCount(20);
        meeting.Calls.Distinct().Should().Equal("increment");
    }

    [Fact]
    public async Task Bad_arguments_are_refused()
    {
        var clock = new ManualClock(Backends.MidMinute);
        using var provider = Backends.Provider(new CacheFake("fake", clock), clock);
        var store = Backends.Throttle(provider);

        Func<Task> nullKey = async () => await store.AttemptAsync(null!, 1, Minute, CancellationToken.None);
        Func<Task> noPermits = async () => await store.AttemptAsync("p", 0, Minute, CancellationToken.None);
        Func<Task> noWindow = async () => await store.AttemptAsync("p", 1, TimeSpan.Zero, CancellationToken.None);

        await nullKey.Should().ThrowAsync<ArgumentNullException>();
        await noPermits.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await noWindow.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task The_first_use_creates_the_cache_store_off_the_callers_thread_and_later_uses_do_not_hop()
    {
        // Naravel.Redis connects synchronously when a store is created for the first time. The throttle guard bounds a call with
        // StoreTimeout only once the call has returned a task, so that creation must not run on the thread that made the call.
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var clock = new ManualClock(Backends.MidMinute);
        var cache = new CacheFake("fake", clock);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddNaravelCache(new ConfigurationBuilder().Build());
        services.AddNaravelDriver<ICacheStore>(Backends.StoreName, _ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return cache;
        });
        services.AddNaravelSharedThrottle(Backends.StoreName);
        using var provider = services.BuildServiceProvider();
        var store = Backends.Throttle(provider);

        var watch = Stopwatch.StartNew();
        var pending = store.AttemptAsync("p", 1, Minute, CancellationToken.None);
        watch.Stop();

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the call returned while the store was still being created");
        pending.IsCompleted.Should().BeFalse();
        entered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the store is being created on another thread");

        release.Set();
        (await pending).Allowed.Should().BeTrue();

        var again = store.AttemptAsync("p", 1, Minute, CancellationToken.None);
        again.IsCompleted.Should().BeTrue("once the store exists a call completes on the caller's thread, with no hop");
        (await again).Allowed.Should().BeFalse();
    }
}
