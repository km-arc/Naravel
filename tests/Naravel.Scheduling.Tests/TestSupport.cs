using System.Globalization;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Locks;

namespace Naravel.Scheduling.Tests;

internal static class Tm
{
    /// <summary>A UTC time from an ISO text such as <c>2026-10-26T09:20:00Z</c>.</summary>
    public static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public static async Task<T0> WithTimeout<T0>(this Task<T0> task) => await task.WaitAsync(TimeSpan.FromSeconds(10));

    public static async Task WithTimeout(this Task task) => await task.WaitAsync(TimeSpan.FromSeconds(10));
}

/// <summary>One in-memory lock that two schedulers can share, reporting itself as shared like Redis does.</summary>
internal sealed class SharedLock : ICacheLock
{
    private readonly MemoryLock _inner = new("test-" + Guid.NewGuid().ToString("N"));

    public bool IsShared => true;

    public Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default) => _inner.AcquireAsync(name, ttl, cancellationToken);

    public Task<bool> ReleaseAsync(string name, string token, CancellationToken cancellationToken = default) => _inner.ReleaseAsync(name, token, cancellationToken);
}

/// <summary>A clock that only moves when the test says so; timers fire from <see cref="Advance"/>.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start;

    public int TimerCount
    {
        get
        {
            lock (_gate) return _timers.Count(t => t.Armed);
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate) _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _now += by;
            due = _timers.Where(t => t.IsDue(_now)).ToList();
        }

        foreach (var timer in due) timer.Fire(_now);
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _due;
        private TimeSpan _period;

        public bool Armed => _due is not null;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                _period = period;
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            }

            return true;
        }

        public bool IsDue(DateTimeOffset now) => _due is { } due && due <= now;

        public void Fire(DateTimeOffset now)
        {
            lock (owner._gate)
            {
                _due = _period > TimeSpan.Zero ? now + _period : null;
            }

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
