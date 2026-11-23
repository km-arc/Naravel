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
        await cacheLock.WaitForRefreshCompletedAsync(1).WaitAsync(TimeSpan.FromSeconds(5));
        lease.IsLost.Should().BeFalse();

        time.Advance(TimeSpan.FromMilliseconds(10));
        await cacheLock.WaitForRefreshCompletedAsync(2).WaitAsync(TimeSpan.FromSeconds(5));

        lease.IsLost.Should().BeFalse();
        cacheLock.RefreshCount.Should().Be(2);
    }

    [Fact]
    public async Task A_refresh_that_reports_a_different_owner_loses_the_lease()
    {
        var time = new ManualTimeProvider();
        var cacheLock = new TestLock { Refresh = (_, _) => Task.FromResult(false) };
        await using var lease = (await cacheLock.TryAcquireLeaseAsync("lost", Options(time)))!;

        var lost = NewSignal();
        using var registration = lease.LostToken.Register(lost.SetResult);

        time.Advance(TimeSpan.FromMilliseconds(10));
        await lost.Task.WaitAsync(TimeSpan.FromSeconds(5));

        lease.IsLost.Should().BeTrue();
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

        var lost = NewSignal();
        using var registration = lease.LostToken.Register(lost.SetResult);

        time.Advance(TimeSpan.FromMilliseconds(10));
        await cacheLock.WaitForRefreshCompletedAsync(1).WaitAsync(TimeSpan.FromSeconds(5));
        lease.IsLost.Should().BeFalse();

        // The first lease would have ended at 30 ms without the refresh. The next attempt also
        // succeeds, but the local deadline must remain capped at 35 ms rather than sliding.
        time.Advance(TimeSpan.FromMilliseconds(20));
        await cacheLock.WaitForRefreshCompletedAsync(2).WaitAsync(TimeSpan.FromSeconds(5));
        lease.IsLost.Should().BeFalse();

        time.Advance(TimeSpan.FromMilliseconds(5));
        await lost.Task.WaitAsync(TimeSpan.FromSeconds(5));

        lease.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task Acquisition_latency_that_consumes_max_hold_prevents_callback_execution()
    {
        var time = new ManualTimeProvider();
        var acquireStarted = NewSignal();
        var pendingAcquire = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cacheLock = new TestLock
        {
            AcquireStarted = acquireStarted,
            Acquire = () => pendingAcquire.Task
        };
        var callbackRuns = 0;
        var run = cacheLock.TryRunAsync("slow-acquire", Options(time), _ =>
        {
            Interlocked.Increment(ref callbackRuns);
            return Task.CompletedTask;
        });

        await acquireStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromMilliseconds(100));
        pendingAcquire.SetResult("owner-token");

        Func<Task> act = async () => await run;
        await act.Should().ThrowAsync<CacheLockLostException>();
        callbackRuns.Should().Be(0);
    }

    [Fact]
    public async Task A_hung_refresh_cannot_hide_the_previous_lease_deadline_or_revive_a_lost_lease()
    {
        var time = new ManualTimeProvider();
        var pendingRefresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cacheLock = new TestLock { Refresh = (_, _) => pendingRefresh.Task };
        var lease = (await cacheLock.TryAcquireLeaseAsync("hung-refresh", Options(time)))!;
        var lost = NewSignal();
        using var registration = lease.LostToken.Register(lost.SetResult);

        time.Advance(TimeSpan.FromMilliseconds(10));
        await cacheLock.WaitForRefreshStartedAsync(1).WaitAsync(TimeSpan.FromSeconds(5));

        // The provider call remains pending beyond the original 30 ms backend lease.
        time.Advance(TimeSpan.FromMilliseconds(20));
        await lost.Task.WaitAsync(TimeSpan.FromSeconds(5));

        lease.IsLost.Should().BeTrue();

        // Disposing must release the token even when the provider ignores refresh cancellation.
        await lease.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        cacheLock.ReleaseCount.Should().Be(1);

        pendingRefresh.SetResult(true);
        await cacheLock.WaitForRefreshCompletedAsync(1).WaitAsync(TimeSpan.FromSeconds(5));

        // A successful response arriving after the confirmed deadline must never restore ownership.
        lease.IsLost.Should().BeTrue();
    }

    [Fact]
    public async Task A_non_refreshable_lock_gets_one_fixed_max_hold_lease()
    {
        var time = new ManualTimeProvider();
        var cacheLock = new FixedTestLock();
        var lease = (await cacheLock.TryAcquireLeaseAsync("fixed", Options(time)))!;

        lease.CanRenew.Should().BeFalse();
        cacheLock.AcquiredTtl.Should().Be(TimeSpan.FromMilliseconds(100));
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

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestLock : ICacheLock, IRefreshableCacheLock
    {
        private int _refreshCount;
        private int _releaseCount;
        private int _refreshCompletedCount;
        private readonly object _refreshGate = new();
        private readonly Dictionary<int, TaskCompletionSource> _refreshStartedWaiters = [];
        private readonly Dictionary<int, TaskCompletionSource> _refreshCompletedWaiters = [];

        public Func<string, string, Task<bool>>? Refresh { get; init; }
        public Func<string, string, Task<bool>>? RefreshAfterFailure { get; init; }
        public Func<Task<string?>>? Acquire { get; init; }
        public TaskCompletionSource? AcquireStarted { get; init; }
        public Func<string, string, Task<bool>>? Release { get; init; }
        public Task? ReleaseGate { get; set; }
        public int RefreshCount => Volatile.Read(ref _refreshCount);
        public int ReleaseCount => Volatile.Read(ref _releaseCount);
        public Task WaitForRefreshStartedAsync(int count) => WaitForRefreshCountAsync(count, started: true);
        public Task WaitForRefreshCompletedAsync(int count) => WaitForRefreshCountAsync(count, started: false);

        private Task WaitForRefreshCountAsync(int count, bool started)
        {
            lock (_refreshGate)
            {
                var current = started ? _refreshCount : _refreshCompletedCount;
                if (current >= count) return Task.CompletedTask;

                var waiters = started ? _refreshStartedWaiters : _refreshCompletedWaiters;
                if (!waiters.TryGetValue(count, out var signal))
                {
                    signal = NewSignal();
                    waiters.Add(count, signal);
                }

                return signal.Task;
            }
        }

        public async Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcquireStarted?.TrySetResult();
            return Acquire is null ? "owner-token" : await Acquire();
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
            int count;
            lock (_refreshGate)
            {
                count = ++_refreshCount;
                if (_refreshStartedWaiters.Remove(count, out var started)) started.TrySetResult();
            }

            try
            {
                if (RefreshAfterFailure is not null && count > 1) return await RefreshAfterFailure(name, token);
                return Refresh is null || await Refresh(name, token);
            }
            finally
            {
                lock (_refreshGate)
                {
                    _refreshCompletedCount = count;
                    if (_refreshCompletedWaiters.Remove(count, out var completed)) completed.TrySetResult();
                }
            }
        }
    }

    private sealed class FixedTestLock : ICacheLock
    {
        public TimeSpan AcquiredTtl { get; private set; }

        public Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            AcquiredTtl = ttl;
            return Task.FromResult<string?>("fixed-token");
        }
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
