namespace Naravel.Queue.Drivers;

/// <summary>
/// The wire/storage representation of a queued job. Every driver (file, redis, database,
/// rabbitmq, kafka, memory) works exclusively in terms of this envelope, so drivers never
/// need to know anything about job types or serialization strategy.
/// </summary>
public class QueuedMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Logical queue name (e.g. "default", "emails", "high").</summary>
    public string Queue { get; set; } = "default";

    /// <summary>Connection selected when dispatching; retained so chain links use the same store.</summary>
    public string? Connection { get; set; }

    /// <summary>Serializer-specific type identifier used to reconstruct the job instance.</summary>
    public string JobType { get; set; } = default!;

    /// <summary>Serialized job payload (JSON by default).</summary>
    public string Payload { get; set; } = default!;

    public int Attempts { get; set; } = 0;

    public int MaxAttempts { get; set; } = 3;

    /// <summary>0 = normal. Higher numbers are dequeued first (range 0-9 is guaranteed to be honored by every driver).</summary>
    public int Priority { get; set; } = 0;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The job will not be handed out by PopAsync before this time (used for delayed dispatch and retries).</summary>
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set by a driver when a worker reserves the message. Null means "not currently being processed".</summary>
    public DateTimeOffset? ReservedAt { get; set; }

    /// <summary>Last error message/stack trace, populated after a failed attempt.</summary>
    public string? Error { get; set; }

    /// <summary>JSON-encoded list of remaining chained jobs to dispatch after this one succeeds. Internal use.</summary>
    public string? ChainedJobPayload { get; set; }

    /// <summary>Identifier of the batch this job belongs to, if dispatched via IJobDispatcher.BatchAsync.</summary>
    public string? BatchId { get; set; }

    /// <summary>W3C traceparent captured when this message was dispatched.</summary>
    public string? TraceParent { get; set; }

    /// <summary>W3C tracestate captured when this message was dispatched.</summary>
    public string? TraceState { get; set; }
}
