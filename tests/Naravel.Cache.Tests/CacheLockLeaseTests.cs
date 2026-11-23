using Naravel.Cache.Abstractions;

namespace Naravel.Cache.Tests;

public sealed class CacheLockLeaseTests
{
    [Fact]
    public async Task A_refresh_failure_is_retried_while_the_previous_lease_is_still_valid()
    {
        var time = new ManualTimeProvider();
        var cacheLock = new TestLock
        {
            Refresh = (_, _) => Task.FromException<bool>(new IOException("temporary")),
            RefreshAfterFailure = (_, _) => Task.FromResult(true)
        };
        var options = Options(time);

        await using var lease = (await cacheLock.TryAcquireLeaseAsync("retry", options))!;

        time.Advance(TimeSpan.FromMilliseconds(10));
        await Eventually(() => cacheLock.RefreshCount == 1);
        lease.IsLost.Should().BeFalse();

        time.Advance(TimeSpan.FromMilliseconds(10));
        await Eventually(() => cacheLock.RefreshCount == 2);

        lease.IsLost.Should().BeFalse();
        cacheLock.RefreshCount.Should().Be(2);
    }

    [Fact]
    public async Task A_refresh_that_reports_a_different_owner_loses_the_lease()
    {
        var time = new ManualTimeProvider();
        var cacheLock = new TestLock { Refresh = (_, _) => Task.FromResult(false) };
        await using var lease = (await cacheLock.TryAcquireLeaseAsync("lost", Options(time)))!;

        time.Advance(TimeSpan.FromMilliseconds(10));
        await Eventually(() => lease.IsLost);

        lease.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task Max_hold_is_a_hard_local_deadline_even_after_a_successful_refresh()
    {
        var time = new ManualTimeProvider();
        var cacheLock = new TestLock { Refresh = (_, _) => Task.FromResult(true) };
        var options = new CacheLockLeaseOptions(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(35))
        {
            RenewInterval = TimeSpan.FromMilliseconds(10),
            TimeProvider = time
        };
        await using var lease = (await cacheLock.TryAcquireLeaseAsync("max-hold", options))!;

        time.Advance(TimeSpan.FromMilliseconds(10));
        await Eventually(() => cacheLock.RefreshCount == 1);
        lease.IsLost.Should().BeFalse();

        // The first lease would have ended at 30 ms without the refresh. The next attempt also
        // succeeds, but the local deadline must remain capped at 35 ms rather than sliding.
        time.Advance(TimeSpan.FromMilliseconds(20));
        await Eventually(() => cacheLock.RefreshCount == 2);
        lease.IsLost.Should().BeFalse();

        time.Advance(TimeSpan.FromMilliseconds(5));
        await Eventually(() => lease.IsLost);

        lease.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task A_non_refreshable_lock_gets_one_fixed_max_hold_lease()
    {
        var time = new ManualTimeProvider();
        var cacheLock = new FixedTestLock();
        var lease = (await cacheLock.TryAcquireLeaseAsync("fixed", Options(time)))!;

        lease.Renews.Should().BeFalse();
        time.Advance(TimeSpan.FromMilliseconds(100));
        lease.IsLost.Should().BeTrue();
        lease.LostToken.IsCancellationRequested.Should().BeTrue();

        await lease.DisposeAsync();
    }

    [Fact]
    public async Task Concurrent_disposals_await_the_same_release_operation()
    {
        var cacheLock = new TestLock();
        var lease = (await cacheLock.TryAcquireLeaseAsync("dispose", Options(new ManualTimeProvider())))!;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cacheLock.ReleaseGate = release.Task;

        var first = lease.DisposeAsync().AsTask();
        var second = lease.DisposeAsync().AsTask();

        first.IsCompleted.Should().BeFalse();
        second.IsCompleted.Should().BeFalse();
        release.SetResult();
        await Task.WhenAll(first, second);

        cacheLock.ReleaseCount.Should().Be(1);
    }

    [Fact]
    public async Task Callback_reports_lease_loss_when_the_lost_token_cancels_it()
    {
        var time = new ManualTimeProvider();
        var cacheLock = new TestLock { Refresh = (_, _) => Task.FromResult(false) };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = cacheLock.TryRunAsync("callback-loss", Options(time), async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });

        await started.Task;
        time.Advance(TimeSpan.FromMilliseconds(10));

        Func<Task> act = async () => await run;
        await act.Should().ThrowAsync<CacheLockLostException>();
    }

    [Fact]
    public async Task Callback_exception_is_preserved_when_release_also_fails()
    {
        var cacheLock = new TestLock { Release = (_, _) => Task.FromException<bool>(new IOException("release")) };

        Func<Task> act = () => cacheLock.TryRunAsync("callback-error", Options(new ManualTimeProvider()),
            _ => throw new InvalidOperationException("callback"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("callback");
    }

    [Fact]
    public async Task Release_failure_is_reported_when_callback_succeeded()
    {
        var cacheLock = new TestLock { Release = (_, _) => Task.FromException<bool>(new IOException("release")) };

        Func<Task> act = () => cacheLock.TryRunAsync("release-error", Options(new ManualTimeProvider()),
            _ => Task.CompletedTask);

        await act.Should().ThrowAsync<IOException>().WithMessage("release");
    }

    [Fact]
    public void The_default_renewal_interval_is_strictly_less_than_small_ttls()
    {
        var options = new CacheLockLeaseOptions(TimeSpan.FromTicks(2), TimeSpan.FromTicks(4));

        options.ResolvedRenewInterval.Should().Be(TimeSpan.FromTicks(1));
        options.ResolvedRenewInterval.Should().BeLessThan(options.Ttl);
    }

    [Fact]
    public void A_ttl_too_small_to_have_a_positive_renewal_interval_is_rejected()
    {
        var act = () => new CacheLockLeaseOptions(TimeSpan.FromTicks(1), TimeSpan.FromTicks(2));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static CacheLockLeaseOptions Options(TimeProvider time) =>
        new(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(100))
        {
            RenewInterval = TimeSpan.FromMilliseconds(10),
            TimeProvider = time
        };

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Yield();
        condition().Should().BeTrue("the asynchronous lease continuation should have observed the advanced test clock");
    }

    private sealed class TestLock : ICacheLock, IRefreshableCacheLock
    {
        private int _refreshCount;
        private int _releaseCount;

        public Func<string, string, Task<bool>>? Refresh { get; init; }
        public Func<string, string, Task<bool>>? RefreshAfterFailure { get; init; }
        public Func<string, string, Task<bool>>? Release { get; init; }
        public Task? ReleaseGate { get; set; }
        public int RefreshCount => Volatile.Read(ref _refreshCount);
        public int ReleaseCount => Volatile.Read(ref _releaseCount);

        public Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>("owner-token");
        }

        public async Task<bool> ReleaseAsync(string name, string token, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _releaseCount);
            if (ReleaseGate is not null) await ReleaseGate;
            return Release is null || await Release(name, token);
        }

        public async Task<bool> RefreshAsync(string name, string token, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _refreshCount);
            if (RefreshAfterFailure is not null && RefreshCount > 1) return await RefreshAfterFailure(name, token);
            return Refresh is null || await Refresh(name, token);
        }
    }

    private sealed class FixedTestLock : ICacheLock
    {
        public Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.FromResult<string?>("fixed-token");
        public Task<bool> ReleaseAsync(string name, string token, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp()
        {
            lock (_gate) return _ticks;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_gate) _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            List<ManualTimer> due;
            lock (_gate)
            {
                _ticks += amount.Ticks;
                due = _timers.Where(timer => timer.IsDue(_ticks)).ToList();
            }

            foreach (var timer in due) timer.Fire(_ticks);
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private long? _due;
            private TimeSpan _period;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate)
                {
                    _period = period;
                    _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._ticks + Math.Max(0, dueTime.Ticks);
                }

                return true;
            }

            public bool IsDue(long now) => _due is { } due && due <= now;

            public void Fire(long now)
            {
                lock (owner._gate) _due = _period > TimeSpan.Zero ? now + _period.Ticks : null;
                callback(state);
            }

            public void Dispose()
            {
                lock (owner._gate) _due = null;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
