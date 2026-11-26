using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Naravel.Cache.Abstractions;
using Naravel.Queue.Coordination;
using Naravel.Queue.Drivers;
using Naravel.Queue.Jobs;

namespace Naravel.Queue.Coordination.Tests;

/// <summary>
/// AD-002 step 4: <see cref="WithoutOverlapping"/> holds its lock through a renewed lease. These tests call the middleware directly with a scripted lock and a
/// manual clock, so the lease length, the renewal, the release and the loss warning are checked without waiting for real time.
/// </summary>
public sealed class WithoutOverlappingLeaseTests
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task A_long_ExpireAfter_is_the_longest_hold_while_the_lock_itself_gets_a_one_minute_lease()
    {
        var cacheLock = new RefreshableLock();
        var time = new ManualClock();
        var middleware = new WithoutOverlapping("invoice:7").ExpireAfter(TimeSpan.FromMinutes(15));

        await middleware.InvokeAsync(Context(cacheLock, time), () =>
        {
            cacheLock.AcquireTtls.Should().Equal(new[] { OneMinute }, "a worker that dies must free the lock within a minute, not after 15");
            return Task.CompletedTask;
        });

        cacheLock.AcquireNames.Should().Equal(new[] { "naravel:queue:overlap:invoice:7" }, "workers on different machines must agree on the lock name");
    }

    [Fact]
    public async Task An_ExpireAfter_shorter_than_a_minute_is_used_as_the_lease()
    {
        var cacheLock = new RefreshableLock();
        var middleware = new WithoutOverlapping("k").ExpireAfter(TimeSpan.FromSeconds(30));

        await middleware.InvokeAsync(Context(cacheLock, new ManualClock()), () => Task.CompletedTask);

        cacheLock.AcquireTtls.Should().Equal(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_lock_that_cannot_refresh_keeps_one_fixed_lease_of_ExpireAfter()
    {
        var cacheLock = new PlainLock();
        var middleware = new WithoutOverlapping("k").ExpireAfter(TimeSpan.FromMinutes(15));

        await middleware.InvokeAsync(Context(cacheLock, new ManualClock()), () => Task.CompletedTask);

        cacheLock.AcquireTtls.Should().Equal(new[] { TimeSpan.FromMinutes(15) }, "a lock that cannot refresh, without ownership-checked refresh, gets the whole hold time as its lease");
        cacheLock.ReleasedTokens.Should().Equal(cacheLock.IssuedToken);
    }

    [Fact]
    public async Task The_lock_is_renewed_while_the_job_runs_and_released_with_its_own_token_afterwards()
    {
        var cacheLock = new RefreshableLock();
        var time = new ManualClock();
        var middleware = new WithoutOverlapping("k").ExpireAfter(TimeSpan.FromMinutes(15));

        await middleware.InvokeAsync(Context(cacheLock, time), async () =>
        {
            time.Advance(TimeSpan.FromSeconds(20));
            await cacheLock.RefreshCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            cacheLock.ReleaseCount.Should().Be(0, "the lock is still held while the job runs");
        });

        cacheLock.RefreshCount.Should().Be(1, "the lease is renewed every third of its length");
        cacheLock.RefreshTtls.Should().Equal(OneMinute);
        cacheLock.ReleasedTokens.Should().Equal(cacheLock.IssuedToken);
    }

    [Fact]
    public async Task The_lock_is_released_and_the_original_exception_is_kept_when_the_job_throws()
    {
        var cacheLock = new PlainLock();
        var middleware = new WithoutOverlapping("k");

        var act = () => middleware.InvokeAsync(Context(cacheLock, new ManualClock()), () => throw new InvalidOperationException("boom"));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("boom");
        cacheLock.ReleasedTokens.Should().Equal(cacheLock.IssuedToken);
    }

    [Fact]
    public async Task A_blocked_job_does_not_run_and_has_no_lease_to_release()
    {
        var cacheLock = new PlainLock { Blocked = true };
        var ran = false;

        await new WithoutOverlapping("k").InvokeAsync(Context(cacheLock, new ManualClock()), () =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        ran.Should().BeFalse();
        cacheLock.ReleaseCount.Should().Be(0);
    }

    [Fact]
    public async Task A_lost_lock_logs_a_warning_with_the_job_and_queue_and_does_not_stop_the_job()
    {
        var cacheLock = new RefreshableLock { RefreshResult = false };
        var time = new ManualClock();
        var log = new LogSink();
        var completed = false;

        await new WithoutOverlapping("secret-key").InvokeAsync(Context(cacheLock, time, log), async () =>
        {
            time.Advance(TimeSpan.FromSeconds(20));
            await log.FirstEntry.WaitAsync(TimeSpan.FromSeconds(5));
            completed = true;
        });

        completed.Should().BeTrue("the middleware cannot cancel the job, so it only reports the loss");
        log.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
        log.Entries[0].Message.Should().Contain("job-1").And.Contain("emails");
        log.Entries[0].Message.Should().NotContain("secret-key", "the lock key can carry business data");
    }

    [Fact]
    public async Task A_lost_lock_does_not_fail_a_job_when_no_logging_is_registered()
    {
        var cacheLock = new RefreshableLock { RefreshResult = false };
        var time = new ManualClock();

        var act = () => new WithoutOverlapping("k").InvokeAsync(Context(cacheLock, time), async () =>
        {
            time.Advance(TimeSpan.FromSeconds(20));
            await cacheLock.RefreshCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void ExpireAfter_rejects_a_lifetime_too_short_for_a_lease_when_the_middleware_is_built_and_not_when_a_job_runs()
    {
        FluentActions.Invoking(() => new WithoutOverlapping("k").ExpireAfter(TimeSpan.FromTicks(1)))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("The lock lifetime must be at least two ticks.*");
        FluentActions.Invoking(() => new WithoutOverlapping("k").ExpireAfter(TimeSpan.FromTicks(2))).Should().NotThrow();
    }

    private static JobContext Context(ICacheLock cacheLock, ManualClock time, LogSink? log = null)
    {
        var services = new ServiceCollection().AddSingleton(cacheLock).AddSingleton<TimeProvider>(time);
        if (log is not null) services.AddSingleton<ILoggerFactory>(log);
        var message = new QueuedMessage { Id = "job-1", Queue = "emails", Connection = "test", JobType = "x", Payload = "{}" };
        return new JobContext(services.BuildServiceProvider(), message);
    }

    // A lock that cannot refresh a lease, like the Memcached lock.
    private class PlainLock : ICacheLock
    {
        private int _releaseCount;

        public bool Blocked { get; init; }
        public string IssuedToken { get; } = "token-" + Guid.NewGuid().ToString("N");
        public List<string> AcquireNames { get; } = [];
        public List<TimeSpan> AcquireTtls { get; } = [];
        public List<string> ReleasedTokens { get; } = [];
        public int ReleaseCount => Volatile.Read(ref _releaseCount);

        public Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            AcquireNames.Add(name);
            AcquireTtls.Add(ttl);
            return Task.FromResult<string?>(Blocked ? null : IssuedToken);
        }

        public Task<bool> ReleaseAsync(string name, string token, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _releaseCount);
            ReleasedTokens.Add(token);
            return Task.FromResult(true);
        }
    }

    // A lock that can refresh a lease (the Memory and Redis locks), with a scripted answer to every refresh.
    private sealed class RefreshableLock : PlainLock, IRefreshableCacheLock
    {
        private int _refreshCount;
        private readonly TaskCompletionSource _refreshCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool RefreshResult { get; init; } = true;
        public List<TimeSpan> RefreshTtls { get; } = [];
        public int RefreshCount => Volatile.Read(ref _refreshCount);
        public Task RefreshCompleted => _refreshCompleted.Task;

        public Task<bool> RefreshAsync(string name, string token, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            RefreshTtls.Add(ttl);
            Interlocked.Increment(ref _refreshCount);
            _refreshCompleted.TrySetResult();
            return Task.FromResult(RefreshResult);
        }
    }

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

            foreach (var timer in due) timer.Fire(_ticks);
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

            public void Fire(long now)
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

    // Collects what is logged. The logger factory is all the middleware needs, so no logging package is involved.
    private sealed class LogSink : ILoggerFactory, ILogger
    {
        private readonly TaskCompletionSource _firstEntry = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public Task FirstEntry => _firstEntry.Task;

        public ILogger CreateLogger(string categoryName) => this;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries) Entries.Add((logLevel, formatter(state, exception)));
            _firstEntry.TrySetResult();
        }
    }
}
