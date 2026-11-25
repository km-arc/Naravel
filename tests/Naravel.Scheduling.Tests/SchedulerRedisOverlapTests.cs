using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Naravel.Cache.Redis;
using Naravel.Testing;
using StackExchange.Redis;

namespace Naravel.Scheduling.Tests;

/// <summary>
/// AD-002 step 4b against a real Redis server (CI "Provider integration tests" job; skipped unless NARAVEL_TEST_REDIS is set). Every scheduler has its own Redis client and its own lock
/// object, so the only thing two instances share is the server, as two hosts do. The lease clock of the first instance is manual, so the test passes a renewal period without waiting
/// 20 seconds; the expiry of the lock key runs on the server in real time and is read back with PTTL.
/// </summary>
public sealed class SchedulerRedisOverlapTests
{
    private static readonly DateTimeOffset Minute = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout10 = TimeSpan.FromSeconds(10);

    // RedisLock keeps a lock under "<prefix>:lock:<name>"; the scheduler names its overlap lock "schedule:overlap:<task>".
    private static string LockKey(string prefix) => prefix + ":lock:schedule:overlap:t";

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task Two_instances_over_one_Redis_skip_the_overlap_and_the_running_instance_keeps_the_lock_through_a_renewal()
    {
        var redis = Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!;
        var prefix = "naravel-test:" + Guid.NewGuid().ToString("N");
        var started = NewSignal();
        var release = NewSignal();
        var secondRuns = 0;
        var clock = new LeaseClock();

        using var first = new Instance(redis, prefix, schedule => schedule.Call("t", async (_, _) =>
        {
            started.TrySetResult();
            await release.Task;
        }).WithoutOverlapping(TimeSpan.FromHours(2)), clock);
        using var second = new Instance(redis, prefix, schedule => schedule.Call("t", (_, _) =>
        {
            Interlocked.Increment(ref secondRuns);
            return Task.CompletedTask;
        }).WithoutOverlapping(TimeSpan.FromHours(2)), TimeProvider.System);

        try
        {
            var firstRun = first.Scheduler.RunDueAsync(Minute);
            await started.Task.WaitAsync(Timeout10);

            await second.Scheduler.RunDueAsync(Minute.AddMinutes(1)).WaitAsync(Timeout10);
            second.Outcomes.Should().Equal("skipped_overlap");
            secondRuns.Should().Be(0, "the first instance still holds the lock");

            // The lease is 60 seconds, not the two hours of the overlap time: an instance that crashed would free the key within a minute.
            await Task.Delay(1500);
            var before = await first.Database.KeyTimeToLiveAsync(LockKey(prefix));
            before.Should().NotBeNull("the lock is held");
            before!.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(60)).And.BeGreaterThan(TimeSpan.Zero);

            // Pass one renewal period on the lease clock of the first instance, which is still running its task.
            clock.Advance(TimeSpan.FromSeconds(20));
            var renewed = await WaitUntilAsync(
                async () => await first.Database.KeyTimeToLiveAsync(LockKey(prefix)) > before.Value + TimeSpan.FromMilliseconds(500),
                Timeout10);
            renewed.Should().BeTrue("the renewal reached Redis and moved the end of the lease out again, so the lock is held by more than its first acquisition");

            await second.Scheduler.RunDueAsync(Minute.AddMinutes(2)).WaitAsync(Timeout10);
            second.Outcomes.Should().Equal("skipped_overlap", "skipped_overlap");

            release.SetResult();
            await firstRun.WaitAsync(Timeout10);
            first.Outcomes.Should().Equal("succeeded");
            (await first.Database.KeyExistsAsync(LockKey(prefix))).Should().BeFalse("the first instance released the lock with its own token");

            await second.Scheduler.RunDueAsync(Minute.AddMinutes(3)).WaitAsync(Timeout10);
            second.Outcomes.Should().Equal("skipped_overlap", "skipped_overlap", "succeeded");
            secondRuns.Should().Be(1);
        }
        finally
        {
            release.TrySetResult();
            await first.Database.KeyDeleteAsync(LockKey(prefix));
        }
    }

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task A_lock_that_is_cleared_in_Redis_is_lost_at_the_next_renewal_and_the_running_task_is_cancelled()
    {
        var redis = Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!;
        var prefix = "naravel-test:" + Guid.NewGuid().ToString("N");
        var started = NewSignal();
        var sawCancellation = false;
        var failureHooks = 0;
        var clock = new LeaseClock();

        using var first = new Instance(redis, prefix, schedule => schedule.Call("t", async (_, cancellationToken) =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                sawCancellation = true;
                throw;
            }
        }).WithoutOverlapping(TimeSpan.FromHours(2)).OnFailure(_ =>
        {
            Interlocked.Increment(ref failureHooks);
            return Task.CompletedTask;
        }), clock);

        // An operator on another client clears the lock, as for a holder that is believed to have crashed.
        using var operatorClient = new Instance(redis, prefix, _ => { }, TimeProvider.System);

        var run = first.Scheduler.RunDueAsync(Minute);
        await started.Task.WaitAsync(Timeout10);
        (await operatorClient.Locks.ForceReleaseAsync("schedule:overlap:t")).Should().BeTrue();

        // The next renewal finds that the token no longer owns the lock, so the lease is lost and the task is told to stop.
        clock.Advance(TimeSpan.FromSeconds(20));
        await run.WaitAsync(Timeout10);

        sawCancellation.Should().BeTrue();
        first.Outcomes.Should().Equal("lost");
        failureHooks.Should().Be(0);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (await condition()) return true;
            await Task.Delay(50);
        }

        return false;
    }

    // One scheduler with its own Redis client and its own lock object.
    private sealed class Instance : IDisposable
    {
        private readonly ConnectionMultiplexer _multiplexer;
        private readonly List<string> _outcomes = [];

        public Instance(string redis, string prefix, Action<Schedule> configure, TimeProvider time)
        {
            _multiplexer = ConnectionMultiplexer.Connect(redis);
            Locks = new RedisLock(_multiplexer, prefix, ownsConnection: false);
            var schedule = new Schedule();
            configure(schedule);
            Scheduler = new Scheduler(
                schedule,
                Locks,
                new ServiceCollection().BuildServiceProvider(),
                new SchedulingOptions(),
                time,
                NullLogger.Instance,
                (_, outcome) =>
                {
                    lock (_outcomes) _outcomes.Add(outcome);
                });
        }

        public Scheduler Scheduler { get; }
        public RedisLock Locks { get; }
        public IDatabase Database => _multiplexer.GetDatabase();

        public string[] Outcomes
        {
            get
            {
                lock (_outcomes) return _outcomes.ToArray();
            }
        }

        public void Dispose()
        {
            Locks.Dispose();
            _multiplexer.Dispose();
        }
    }

    // A clock that only moves when the test says so. The lease reads its monotonic timestamp, and its timers fire from Advance.
    private sealed class LeaseClock : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<LeaseTimer> _timers = [];
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            lock (_gate) return _ticks;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new LeaseTimer(this, callback, state);
            lock (_gate) _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            List<LeaseTimer> due;
            lock (_gate)
            {
                _ticks += amount.Ticks;
                due = _timers.Where(timer => timer.IsDue(_ticks)).ToList();
            }

            foreach (var timer in due) timer.Fire();
        }

        private sealed class LeaseTimer(LeaseClock owner, TimerCallback callback, object? state) : ITimer
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
}
