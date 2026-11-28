using Naravel.Cache.Abstractions;

namespace Naravel.Cache.Tests;

public sealed class CacheLockLeaseTests
{
    [Fact]
    public async Task Disposing_an_unlost_lease_keeps_it_unlost_after_its_original_deadline()
    {
        var time = new ManualTimeProvider();
        var cacheLock = new TestLock();
        var lease = (await cacheLock.TryAcquireLeaseAsync("disposed", Options(time)))!;

        await lease.DisposeAsync();
        time.Advance(TimeSpan.FromSeconds(1));

        lease.IsLost.Should().BeFalse();
        lease.LostToken.IsCancellationRequested.Should().BeFalse();
        cacheLock.ReleaseCount.Should().Be(1);
    }

    [Fact]
    public async Task Disposing_while_a_refresh_is_pending_cancels_monitoring_without_losing_the_lease()
    {
        var time = new ManualTimeProvider();
        var pendingRefresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cacheLock = new TestLock { Refresh = (_, _) => pendingRefresh.Task };
        var lease = (await cacheLock.TryAcquireLeaseAsync("dispose-pending-refresh", Options(time)))!;

        time.Advance(TimeSpan.FromMilliseconds(10));
        await cacheLock.WaitForRefreshStartedAsync(1).WaitAsync(TimeSpan.FromSeconds(5));

        await lease.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        lease.IsLost.Should().BeFalse();
        lease.LostToken.IsCancellationRequested.Should().BeFalse();
        cacheLock.ReleaseCount.Should().Be(1);

        pendingRefresh.SetResult(true);
        await cacheLock.WaitForRefreshCompletedAsync(1).WaitAsync(TimeSpan.FromSeconds(5));
        lease.IsLost.Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_refresh_loses_the_lease_at_the_previous_deadline_and_preserves_the_provider_error()
    {
        var time = new ManualTimeProvider();
        var refreshError = new IOException("redis unavailable");
        var cacheLock = new TestLock { Refresh = (_, _) => Task.FromException<bool>(refreshError) };
        var callbackStarted = NewSignal();
        var run = cacheLock.TryRunAsync("renewal-error", Options(time), async token =>
        {
            callbackStarted.SetResult();
            var lost = NewSignal();
            using var registration = token.Register(lost.SetResult);
            // The callback observes the loss and returns normally. In this path the provider
            // error, rather than a callback-thrown cancellation, is the useful inner exception.
            await lost.Task.WaitAsync(TimeSpan.FromSeconds(5));
        });

        await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromMilliseconds(10));
        await cacheLock.WaitForRefreshCompletedAsync(1).WaitAsync(TimeSpan.FromSeconds(5));

        // Wait until the monitor has consumed the first failure and started its scheduled retry.
        time.Advance(TimeSpan.FromMilliseconds(10));
        await cacheLock.WaitForRefreshCompletedAsync(2).WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromMilliseconds(10));

        Func<Task> act = async () => await run;
        var exception = await act.Should().ThrowAsync<CacheLockLostException>();
        exception.Which.Message.Should().Contain("could not be renewed before it ended");
        exception.Which.InnerException.Should().BeSameAs(refreshError);
        cacheLock.ReleaseCount.Should().Be(1);
    }

    [Fact]
    public async Task A_callback_own_cancellation_is_not_misclassified_as_lease_loss()
    {
        var cacheLock = new TestLock();
        Func<Task> act = async () => await cacheLock.TryRunAsync(
            "callback-cancellation",
            Options(new ManualTimeProvider()),
            _ => Task.FromException(new OperationCanceledException("callback stopped itself")));

        await act.Should().ThrowAsync<OperationCanceledException>().WithMessage("callback stopped itself");
        cacheLock.ReleaseCount.Should().Be(1);
    }

    [Fact]
    public async Task A_refreshable_lease_does_not_renew_when_its_initial_ttl_is_already_the_max_hold()
    {
        var time = new ManualTimeProvider();
        var cacheLock = new TestLock { Refresh = (_, _) => Task.FromResult(true) };
        var options = new CacheLockLeaseOptions(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30))
        {
            RenewInterval = TimeSpan.FromMilliseconds(10),
            TimeProvider = time
        };
        var lease = (await cacheLock.TryAcquireLeaseAsync("ttl-is-max-hold", options))!;
        var lost = NewSignal();
        using var registration = lease.LostToken.Register(lost.SetResult);

        time.Advance(TimeSpan.FromMilliseconds(30));
        await lost.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cacheLock.RefreshCount.Should().Be(0);
        lease.IsLost.Should().BeTrue();
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task Block_async_returns_false_without_running_the_callback_when_the_lease_is_busy()
    {
        var cacheLock = new TestLock { Acquire = () => Task.FromResult<string?>(null) };
        var callbackRuns = 0;

        var acquired = await cacheLock.BlockAsync("busy", Options(new ManualTimeProvider()), TimeSpan.Zero, _ =>
        {
            Interlocked.Increment(ref callbackRuns);
            return Task.CompletedTask;
        });

        acquired.Should().BeFalse();
        callbackRuns.Should().Be(0);
        cacheLock.ReleaseCount.Should().Be(0);
    }

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
        cacheLock.RefreshTtls.Should().Equal(new[] { TimeSpan.FromMilliseconds(25) });

        // The refresh is shortened to the remaining 25 ms, so its local end is exactly
        // MaxHold (35 ms). No further refresh is useful or permitted past that hard boundary.
        time.Advance(TimeSpan.FromMilliseconds(25));
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
    public async Task Callback_that_returns_normally_after_lease_loss_still_reports_lease_loss()
    {
        var time = new ManualTimeProvider();
        var cacheLock = new TestLock { Refresh = (_, _) => Task.FromResult(false) };
        var run = cacheLock.TryRunAsync("callback-returns-after-loss", Options(time), async token =>
        {
            var lost = NewSignal();
            using var registration = token.Register(lost.SetResult);
            time.Advance(TimeSpan.FromMilliseconds(10));
            await lost.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The callback deliberately returns normally instead of throwing on cancellation.
        });

        Func<Task> act = async () => await run;
        await act.Should().ThrowAsync<CacheLockLostException>();
        cacheLock.ReleaseCount.Should().Be(1);
    }

    [Fact]
    public async Task A_token_checked_release_that_reports_not_owner_is_reported_as_lease_loss()
    {
        var cacheLock = new TestLock { Release = (_, _) => Task.FromResult(false) };

        Func<Task> act = () => cacheLock.TryRunAsync("release-not-owner", Options(new ManualTimeProvider()),
            _ => Task.CompletedTask);

        await act.Should().ThrowAsync<CacheLockLostException>();
        cacheLock.ReleaseCount.Should().Be(1);
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
    public void Max_hold_must_not_be_shorter_than_the_lease_ttl()
    {
        var act = () => new CacheLockLeaseOptions(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(19));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void An_explicit_renewal_interval_is_used_and_must_be_less_than_the_ttl()
    {
        var options = new CacheLockLeaseOptions(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(100))
        {
            RenewInterval = TimeSpan.FromMilliseconds(5)
        };
        options.ResolvedRenewInterval.Should().Be(TimeSpan.FromMilliseconds(5));

        var tooLong = () => new CacheLockLeaseOptions(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(100))
        {
            RenewInterval = TimeSpan.FromMilliseconds(30)
        };
        tooLong.Should().Throw<ArgumentOutOfRangeException>();
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
        public List<TimeSpan> RefreshTtls { get; } = [];
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
                RefreshTtls.Add(ttl);
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
