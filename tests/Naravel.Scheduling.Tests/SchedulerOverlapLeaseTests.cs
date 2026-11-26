using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Naravel.Cache.Abstractions;

namespace Naravel.Scheduling.Tests;

/// <summary>
/// AD-002 step 4b: the overlap lock of <c>WithoutOverlapping()</c> is held through a renewed lease. The scheduler is built directly with a scripted lock, a manual clock and a
/// collecting logger, so the lease length, the renewal, the loss and the release are checked without waiting for real time.
/// </summary>
public sealed class SchedulerOverlapLeaseTests
{
    private static readonly DateTimeOffset Minute = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task A_long_overlap_time_is_the_longest_hold_while_the_lock_itself_gets_a_one_minute_lease()
    {
        var cacheLock = new RefreshableLock();
        var (scheduler, outcomes) = Build(schedule => schedule.Call("t", (_, _) => Task.CompletedTask).WithoutOverlapping(), cacheLock, new ManualClock(), new LogSink());

        await scheduler.RunDueAsync(Minute).WaitAsync(TimeSpan.FromSeconds(10));

        cacheLock.AcquireNames.Should().Equal(new[] { "schedule:overlap:t" }, "the lock name is part of the contract between instances");
        cacheLock.AcquireTtls.Should().Equal(new[] { OneMinute }, "a crashed instance must free the lock within a minute, not after the default hour");
        cacheLock.ReleasedTokens.Should().Equal(cacheLock.IssuedToken);
        outcomes.Should().Equal("succeeded");
    }

    [Fact]
    public async Task An_overlap_time_shorter_than_a_minute_is_used_as_the_lease()
    {
        var cacheLock = new RefreshableLock();
        var (scheduler, _) = Build(schedule => schedule.Call("t", (_, _) => Task.CompletedTask).WithoutOverlapping(TimeSpan.FromSeconds(30)), cacheLock, new ManualClock(), new LogSink());

        await scheduler.RunDueAsync(Minute).WaitAsync(TimeSpan.FromSeconds(10));

        cacheLock.AcquireTtls.Should().Equal(new[] { TimeSpan.FromSeconds(30) });
    }

    [Fact]
    public async Task A_lock_that_cannot_refresh_keeps_one_fixed_lease_of_the_overlap_time()
    {
        var cacheLock = new PlainLock();
        var (scheduler, _) = Build(schedule => schedule.Call("t", (_, _) => Task.CompletedTask).WithoutOverlapping(TimeSpan.FromHours(6)), cacheLock, new ManualClock(), new LogSink());

        await scheduler.RunDueAsync(Minute).WaitAsync(TimeSpan.FromSeconds(10));

        cacheLock.AcquireTtls.Should().Equal(new[] { TimeSpan.FromHours(6) }, "a lock that cannot refresh, without ownership-checked refresh, gets the whole hold time as its lease");
        cacheLock.ReleasedTokens.Should().Equal(cacheLock.IssuedToken);
    }

    [Fact]
    public async Task The_lock_is_renewed_while_the_task_runs_and_released_with_its_own_token_afterwards()
    {
        var cacheLock = new RefreshableLock();
        var time = new ManualClock();
        var (scheduler, outcomes) = Build(schedule => schedule.Call("t", async (_, _) =>
        {
            time.Advance(TimeSpan.FromSeconds(20));
            await cacheLock.RefreshCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            cacheLock.ReleaseCount.Should().Be(0, "the lock is still held while the task runs");
        }).WithoutOverlapping(), cacheLock, time, new LogSink());

        await scheduler.RunDueAsync(Minute).WaitAsync(TimeSpan.FromSeconds(10));

        cacheLock.RefreshTtls.Should().Equal(new[] { OneMinute }, "the lease is renewed every third of its length");
        cacheLock.ReleasedTokens.Should().Equal(cacheLock.IssuedToken);
        outcomes.Should().Equal("succeeded");
    }

    [Fact]
    public async Task A_task_that_loses_the_lock_is_cancelled_with_the_outcome_lost_and_no_failure_hook_runs()
    {
        var cacheLock = new RefreshableLock { RefreshResult = false };
        var time = new ManualClock();
        var log = new LogSink();
        var failureHooks = 0;
        var sawCancellation = false;
        var (scheduler, outcomes) = Build(schedule => schedule.Call("t", async (_, cancellationToken) =>
        {
            time.Advance(TimeSpan.FromSeconds(20));
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                sawCancellation = true;
                throw;
            }
        }).WithoutOverlapping().OnFailure(_ =>
        {
            Interlocked.Increment(ref failureHooks);
            return Task.CompletedTask;
        }), cacheLock, time, log);

        await scheduler.RunDueAsync(Minute).WaitAsync(TimeSpan.FromSeconds(10));

        sawCancellation.Should().BeTrue("the token given to the task is cancelled when the lock is lost");
        outcomes.Should().Equal("lost");
        failureHooks.Should().Be(0, "losing the lock is not a failure of the task");
        log.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Error);
        log.Entries[0].Message.Should().Contain("scheduled task t");
    }

    [Fact]
    public async Task A_task_that_ignores_the_loss_and_finishes_is_still_a_success_but_the_loss_is_logged()
    {
        var cacheLock = new RefreshableLock { RefreshResult = false };
        var time = new ManualClock();
        var log = new LogSink();
        var successHooks = 0;
        var (scheduler, outcomes) = Build(schedule => schedule.Call("t", async (_, _) =>
        {
            time.Advance(TimeSpan.FromSeconds(20));
            await log.FirstEntry.WaitAsync(TimeSpan.FromSeconds(5));
        }).WithoutOverlapping().OnSuccess(() =>
        {
            Interlocked.Increment(ref successHooks);
            return Task.CompletedTask;
        }), cacheLock, time, log);

        await scheduler.RunDueAsync(Minute).WaitAsync(TimeSpan.FromSeconds(10));

        outcomes.Should().Equal("succeeded");
        successHooks.Should().Be(1);
        log.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Error);
    }

    [Fact]
    public async Task A_task_that_fails_after_the_loss_keeps_its_own_exception_and_the_outcome_failed()
    {
        var cacheLock = new RefreshableLock { RefreshResult = false };
        var time = new ManualClock();
        var log = new LogSink();
        Exception? seenByHook = null;
        var (scheduler, outcomes) = Build(schedule => schedule.Call("t", async (_, _) =>
        {
            time.Advance(TimeSpan.FromSeconds(20));
            await log.FirstEntry.WaitAsync(TimeSpan.FromSeconds(5));
            throw new InvalidOperationException("boom");
        }).WithoutOverlapping().OnFailure(exception =>
        {
            seenByHook = exception;
            return Task.CompletedTask;
        }), cacheLock, time, log);

        await scheduler.RunDueAsync(Minute).WaitAsync(TimeSpan.FromSeconds(10));

        outcomes.Should().Equal("failed");
        seenByHook.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom");
    }

    [Fact]
    public async Task A_release_that_fails_is_logged_as_a_warning_and_does_not_change_the_outcome()
    {
        var cacheLock = new PlainLock { ReleaseThrows = true };
        var log = new LogSink();
        var (scheduler, outcomes) = Build(schedule => schedule.Call("t", (_, _) => Task.CompletedTask).WithoutOverlapping(), cacheLock, new ManualClock(), log);

        await scheduler.RunDueAsync(Minute).WaitAsync(TimeSpan.FromSeconds(10));

        outcomes.Should().Equal("succeeded");
        log.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
        log.Entries[0].Message.Should().Contain("Releasing the overlap lock of scheduled task t failed");
    }

    [Fact]
    public void An_overlap_time_too_short_for_a_lease_is_refused_when_the_scheduler_or_the_task_is_built_and_not_when_a_task_runs()
    {
        var provider = new ServiceCollection().BuildServiceProvider();

        FluentActions.Invoking(() => new Scheduler(new Schedule(), new PlainLock(), provider, new SchedulingOptions { OverlapTtl = TimeSpan.FromTicks(1) }, TimeProvider.System, NullLogger.Instance))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new Schedule().Call("t", (_, _) => Task.CompletedTask).WithoutOverlapping(TimeSpan.FromTicks(1)))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new Schedule().Call("t", (_, _) => Task.CompletedTask).WithoutOverlapping(TimeSpan.FromTicks(2)))
            .Should().NotThrow();
    }

    private static (Scheduler Scheduler, List<string> Outcomes) Build(Action<Schedule> configure, ICacheLock cacheLock, ManualClock time, LogSink log)
    {
        var schedule = new Schedule();
        configure(schedule);
        var outcomes = new List<string>();
        var scheduler = new Scheduler(
            schedule,
            cacheLock,
            new ServiceCollection().BuildServiceProvider(),
            new SchedulingOptions(),
            time,
            log,
            (_, outcome) =>
            {
                lock (outcomes) outcomes.Add(outcome);
            });
        return (scheduler, outcomes);
    }

    // A lock that cannot refresh a lease, like the Memcached lock.
    private class PlainLock : ICacheLock
    {
        private int _releaseCount;

        public bool ReleaseThrows { get; init; }
        public string IssuedToken { get; } = "token-" + Guid.NewGuid().ToString("N");
        public List<string> AcquireNames { get; } = [];
        public List<TimeSpan> AcquireTtls { get; } = [];
        public List<string> ReleasedTokens { get; } = [];
        public int ReleaseCount => Volatile.Read(ref _releaseCount);

        public Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            AcquireNames.Add(name);
            AcquireTtls.Add(ttl);
            return Task.FromResult<string?>(IssuedToken);
        }

        public Task<bool> ReleaseAsync(string name, string token, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _releaseCount);
            ReleasedTokens.Add(token);
            return ReleaseThrows ? Task.FromException<bool>(new IOException("the lock store is unreachable")) : Task.FromResult(true);
        }
    }

    // A lock that can refresh a lease (the Memory and Redis locks), with a scripted answer to every refresh.
    private sealed class RefreshableLock : PlainLock, IRefreshableCacheLock
    {
        private readonly TaskCompletionSource _refreshCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool RefreshResult { get; init; } = true;
        public List<TimeSpan> RefreshTtls { get; } = [];
        public Task RefreshCompleted => _refreshCompleted.Task;

        public Task<bool> RefreshAsync(string name, string token, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            RefreshTtls.Add(ttl);
            _refreshCompleted.TrySetResult();
            return Task.FromResult(RefreshResult);
        }
    }

    // A clock that only moves when the test says so. The lease reads its monotonic timestamp, and its timers fire from Advance.
    private sealed class ManualClock : TimeProvider
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

            foreach (var timer in due) timer.Fire();
        }

        private sealed class ManualTimer(ManualClock owner, TimerCallback callback, object? state) : ITimer
        {
            private long? _due;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate) _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._ticks + Math.Max(0, dueTime.Ticks);
                return true;
            }

            public bool IsDue(long now) => _due is { } due && due <= now;

            public void Fire()
            {
                lock (owner._gate) _due = null;
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

    // Collects what the scheduler logs.
    private sealed class LogSink : ILogger
    {
        private readonly TaskCompletionSource _firstEntry = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public Task FirstEntry => _firstEntry.Task;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries) Entries.Add((logLevel, formatter(state, exception)));
            _firstEntry.TrySetResult();
        }
    }
}
