namespace Naravel.Queue.Dispatch;

/// <summary>Fluent options for a single dispatch call. Mirrors Laravel's ->onQueue()->onConnection()->delay() chain.</summary>
public class DispatchOptions
{
    /// <summary>Gets the connection name selected for this dispatch, or <see langword="null"/> for the default connection.</summary>
    public string? Connection { get; private set; }
    /// <summary>Gets the queue name selected for this dispatch, or <see langword="null"/> for the default queue.</summary>
    public string? Queue { get; private set; }
    /// <summary>Gets the delay before the job becomes available, if one was set.</summary>
    public TimeSpan? Delay { get; private set; }
    /// <summary>Gets the absolute time at which the job becomes available, if one was set.</summary>
    public DateTimeOffset? At { get; private set; }
    /// <summary>Gets the per-dispatch override of the maximum attempt count, if one was set.</summary>
    public int? MaxAttempts { get; private set; }
    /// <summary>Gets the dispatch priority in the range 0 to 9; the default is 0.</summary>
    public int Priority { get; private set; } = 0;

    /// <summary>Send this job to a specific connection (driver), e.g. "redis", "database".</summary>
    public DispatchOptions OnConnection(string name) { Connection = name; return this; }

    /// <summary>Send this job to a specific queue name within the connection, e.g. "emails", "high".</summary>
    public DispatchOptions OnQueue(string name) { Queue = name; return this; }

    /// <summary>Delay availability of this job by the given timespan.</summary>
    public DispatchOptions DelayFor(TimeSpan delay) { Delay = delay; At = null; return this; }

    /// <summary>Make this job available at a specific point in time.</summary>
    public DispatchOptions RunAt(DateTimeOffset when) { At = when; Delay = null; return this; }

    /// <summary>Override the job's own MaxAttempts for this dispatch.</summary>
    public DispatchOptions WithMaxAttempts(int attempts) { MaxAttempts = attempts; return this; }

    /// <summary>Higher priority jobs (0-9) are dequeued before lower ones on the same queue, where supported by the driver.</summary>
    public DispatchOptions WithPriority(int priority) { Priority = Math.Clamp(priority, 0, 9); return this; }

    internal DateTimeOffset ResolveAvailableAt()
    {
        if (At.HasValue) return At.Value;
        if (Delay.HasValue) return DateTimeOffset.UtcNow + Delay.Value;
        return DateTimeOffset.UtcNow;
    }
}
