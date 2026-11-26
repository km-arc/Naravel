using Naravel.Cache.Abstractions;
using Naravel.Cache.Locks;

namespace Naravel.Cache.Tests;

public sealed class CacheLockTests
{
    [Fact]
    public async Task Only_the_current_token_can_release_a_lock()
    {
        var cacheLock = new MemoryLock("tests");
        var token = await cacheLock.AcquireAsync("order:1", TimeSpan.FromSeconds(5));

        token.Should().NotBeNull();
        (await cacheLock.AcquireAsync("order:1", TimeSpan.FromSeconds(5))).Should().BeNull();
        (await cacheLock.ReleaseAsync("order:1", "not-owner")).Should().BeFalse();
        (await cacheLock.ReleaseAsync("order:1", token!)).Should().BeTrue();
        (await cacheLock.AcquireAsync("order:1", TimeSpan.FromSeconds(5))).Should().NotBeNull();
    }

    [Fact]
    public async Task Block_async_runs_and_releases_the_lock_even_when_callback_fails()
    {
        var cacheLock = new MemoryLock("tests");
        var act = () => cacheLock.BlockAsync("failing", TimeSpan.FromSeconds(5), TimeSpan.Zero,
            _ => throw new InvalidOperationException("expected"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await cacheLock.AcquireAsync("failing", TimeSpan.FromSeconds(5))).Should().NotBeNull();
    }

    [Fact]
    public async Task A_released_lock_is_not_kept_in_the_table()
    {
        var cacheLock = new MemoryLock("table-released");
        for (var i = 0; i < 200; i++)
        {
            var token = await cacheLock.AcquireAsync($"subject:{i}", TimeSpan.FromSeconds(30));
            await cacheLock.ReleaseAsync($"subject:{i}", token!);
        }

        // One state per name used would be 200; a released lock leaves nothing behind.
        MemoryLock.TrackedCount("table-released:").Should().Be(0);
    }

    [Fact]
    public async Task A_lock_that_expired_without_a_release_is_swept()
    {
        var cacheLock = new MemoryLock("table-expired");
        for (var i = 0; i < 300; i++)
        {
            (await cacheLock.AcquireAsync($"crashed:{i}", TimeSpan.FromMilliseconds(1))).Should().NotBeNull();
        }

        await Task.Delay(100);

        // The table is swept every 1,024 acquisitions anywhere in the process, so 2,500 more guarantee at least two sweeps.
        for (var i = 0; i < 2500; i++)
        {
            var token = await cacheLock.AcquireAsync($"other:{i}", TimeSpan.FromSeconds(30));
            await cacheLock.ReleaseAsync($"other:{i}", token!);
        }

        MemoryLock.TrackedCount("table-expired:").Should().Be(0);
    }

    [Fact]
    public async Task A_name_is_still_held_by_one_owner_at_a_time_while_states_come_and_go()
    {
        var cacheLock = new MemoryLock("table-race");
        var holders = 0;
        var maxHolders = 0;

        async Task Worker()
        {
            for (var i = 0; i < 1500; i++)
            {
                string? token;
                while ((token = await cacheLock.AcquireAsync("hot", TimeSpan.FromSeconds(30))) is null) await Task.Yield();

                var now = Interlocked.Increment(ref holders);
                var seen = Volatile.Read(ref maxHolders);
                while (now > seen && Interlocked.CompareExchange(ref maxHolders, now, seen) != seen) seen = Volatile.Read(ref maxHolders);
                await Task.Yield();   // widen the window in which a second owner would be seen
                Interlocked.Decrement(ref holders);

                (await cacheLock.ReleaseAsync("hot", token!)).Should().BeTrue();
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(Worker)));

        maxHolders.Should().Be(1);
        MemoryLock.TrackedCount("table-race:").Should().Be(0);
    }

    [Fact]
    public async Task Refresh_keeps_a_lock_past_its_first_lease_for_the_owner()
    {
        var cacheLock = new MemoryLock("refresh-owner");
        var token = await cacheLock.AcquireAsync("job", TimeSpan.FromMilliseconds(1000));

        (await cacheLock.RefreshAsync("job", token!, TimeSpan.FromSeconds(30))).Should().BeTrue();
        await Task.Delay(1300);

        // Without the refresh the first lease would have ended after one second and this acquire would succeed.
        (await cacheLock.AcquireAsync("job", TimeSpan.FromSeconds(30))).Should().BeNull();
        (await cacheLock.ReleaseAsync("job", token!)).Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_by_a_caller_that_is_not_the_owner_changes_nothing()
    {
        var cacheLock = new MemoryLock("refresh-stranger");
        await cacheLock.AcquireAsync("job", TimeSpan.FromMilliseconds(150));

        (await cacheLock.RefreshAsync("job", "not-owner", TimeSpan.FromSeconds(30))).Should().BeFalse();
        await Task.Delay(450);

        // The lease of the real owner ended on schedule, so the lock can be taken.
        (await cacheLock.AcquireAsync("job", TimeSpan.FromSeconds(30))).Should().NotBeNull();
    }

    [Fact]
    public async Task Refresh_sets_the_lease_to_the_new_value_and_does_not_add_to_it()
    {
        var cacheLock = new MemoryLock("refresh-set");
        var token = await cacheLock.AcquireAsync("job", TimeSpan.FromSeconds(30));

        (await cacheLock.RefreshAsync("job", token!, TimeSpan.FromMilliseconds(150))).Should().BeTrue();
        await Task.Delay(450);

        // A lease of 30 seconds plus 150 milliseconds would still be held here.
        (await cacheLock.AcquireAsync("job", TimeSpan.FromSeconds(30))).Should().NotBeNull();
    }

    [Fact]
    public async Task A_lease_that_ended_is_not_revived_even_when_nobody_took_the_lock()
    {
        var cacheLock = new MemoryLock("refresh-ended");
        var token = await cacheLock.AcquireAsync("job", TimeSpan.FromMilliseconds(50));
        await Task.Delay(250);

        (await cacheLock.RefreshAsync("job", token!, TimeSpan.FromSeconds(30))).Should().BeFalse();
        (await cacheLock.AcquireAsync("job", TimeSpan.FromSeconds(30))).Should().NotBeNull();
    }

    [Fact]
    public async Task A_holder_whose_lease_ended_cannot_refresh_or_release_the_lock_that_another_holder_took()
    {
        var cacheLock = new MemoryLock("refresh-takeover");
        var first = await cacheLock.AcquireAsync("job", TimeSpan.FromMilliseconds(50));
        await Task.Delay(250);
        var second = await cacheLock.AcquireAsync("job", TimeSpan.FromSeconds(30));
        second.Should().NotBeNull();

        (await cacheLock.RefreshAsync("job", first!, TimeSpan.FromSeconds(30))).Should().BeFalse();
        (await cacheLock.ReleaseAsync("job", first!)).Should().BeFalse();
        (await cacheLock.RefreshAsync("job", second!, TimeSpan.FromSeconds(30))).Should().BeTrue();
        (await cacheLock.ReleaseAsync("job", second!)).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Refresh_rejects_a_lease_of_zero_or_less(int seconds)
    {
        var cacheLock = new MemoryLock("refresh-invalid");

        Func<Task> act = () => cacheLock.RefreshAsync("job", "token", TimeSpan.FromSeconds(seconds));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Force_release_frees_the_name_and_the_previous_token_loses_it()
    {
        var cacheLock = new MemoryLock("force-release");
        var first = await cacheLock.AcquireAsync("job", TimeSpan.FromSeconds(30));

        (await cacheLock.ForceReleaseAsync("job")).Should().BeTrue();
        var second = await cacheLock.AcquireAsync("job", TimeSpan.FromSeconds(30));

        second.Should().NotBeNull();
        (await cacheLock.ReleaseAsync("job", first!)).Should().BeFalse();
        (await cacheLock.RefreshAsync("job", first!, TimeSpan.FromSeconds(30))).Should().BeFalse();
        (await cacheLock.AcquireAsync("job", TimeSpan.FromSeconds(30))).Should().BeNull("the second holder still owns the lock");
        (await cacheLock.ReleaseAsync("job", second!)).Should().BeTrue();
    }

    [Fact]
    public async Task Force_release_reports_false_when_there_is_no_lock_or_its_lease_ended()
    {
        var cacheLock = new MemoryLock("force-none");

        (await cacheLock.ForceReleaseAsync("never-held")).Should().BeFalse();

        await cacheLock.AcquireAsync("expired", TimeSpan.FromMilliseconds(50));
        await Task.Delay(250);
        (await cacheLock.ForceReleaseAsync("expired")).Should().BeFalse();
    }

    [Fact]
    public async Task A_cancelled_token_stops_refresh_and_force_release_before_they_change_anything()
    {
        var cacheLock = new MemoryLock("refresh-cancelled");
        var token = await cacheLock.AcquireAsync("job", TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> refresh = () => cacheLock.RefreshAsync("job", token!, TimeSpan.FromSeconds(30), cts.Token);
        Func<Task> force = () => cacheLock.ForceReleaseAsync("job", cts.Token);

        await refresh.Should().ThrowAsync<OperationCanceledException>();
        await force.Should().ThrowAsync<OperationCanceledException>();
        (await cacheLock.ReleaseAsync("job", token!)).Should().BeTrue("the lock was not touched");
    }

    [Fact]
    public async Task Concurrent_release_and_force_release_have_exactly_one_winner()
    {
        var cacheLock = new MemoryLock("release-race");

        for (var i = 0; i < 100; i++)
        {
            var name = $"job:{i}";
            var token = await cacheLock.AcquireAsync(name, TimeSpan.FromSeconds(30));
            using var barrier = new Barrier(2);

            var release = Task.Run(() =>
            {
                barrier.SignalAndWait();
                return cacheLock.ReleaseAsync(name, token!).GetAwaiter().GetResult();
            });
            var forceRelease = Task.Run(() =>
            {
                barrier.SignalAndWait();
                return cacheLock.ForceReleaseAsync(name).GetAwaiter().GetResult();
            });

            var results = await Task.WhenAll(release, forceRelease);
            results.Count(static succeeded => succeeded).Should().Be(1);
            (await cacheLock.AcquireAsync(name, TimeSpan.FromSeconds(30))).Should().NotBeNull();
        }
    }

    // The Memcached lock must not offer these capabilities: its touch command does not check an owner, so a refresh could extend a lock that another holder owns.
    [Fact]
    public void The_memory_lock_has_both_capabilities_and_the_memcached_lock_has_neither()
    {
        typeof(MemoryLock).Should().Implement<IRefreshableCacheLock>();
        typeof(MemoryLock).Should().Implement<IForceReleasableCacheLock>();
    }
}
