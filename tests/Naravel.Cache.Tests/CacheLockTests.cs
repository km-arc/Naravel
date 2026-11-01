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
}
