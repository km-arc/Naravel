using Microsoft.EntityFrameworkCore;
namespace Naravel.Queue.Database;

/// <summary>EF Core entity backing the "database" driver - equivalent to Laravel's jobs table.</summary>
public class JobRecord
{
    /// <summary>Gets or sets the unique job identifier.</summary>
    public string Id { get; set; } = default!;
    /// <summary>Gets or sets the name of the queue holding the job.</summary>
    public string Queue { get; set; } = default!;
    /// <summary>Gets or sets the queue connection name, if any.</summary>
    public string? Connection { get; set; }
    /// <summary>Gets or sets the registered job type identifier.</summary>
    public string JobType { get; set; } = default!;
    /// <summary>Gets or sets the serialized job payload.</summary>
    public string Payload { get; set; } = default!;
    /// <summary>Gets or sets the number of attempts made so far.</summary>
    public int Attempts { get; set; }
    /// <summary>Gets or sets the maximum number of attempts allowed.</summary>
    public int MaxAttempts { get; set; }
    /// <summary>Gets or sets the job priority; higher values are popped first.</summary>
    public int Priority { get; set; }
    /// <summary>Gets or sets when the job was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets when the job becomes available for processing.</summary>
    public DateTimeOffset AvailableAt { get; set; }
    /// <summary>Gets or sets when a worker reserved the job, or null if it is not reserved.</summary>
    public DateTimeOffset? ReservedAt { get; set; }

    /// <summary>Token of the current reservation; Ack/Release/Fail only affect the row while it still matches.</summary>
    public string? ReservationId { get; set; }
    /// <summary>Gets or sets when the job failed permanently, or null if it has not.</summary>
    public DateTimeOffset? FailedAt { get; set; }
    /// <summary>Gets or sets the last error text recorded for the job.</summary>
    public string? Error { get; set; }
    /// <summary>Gets or sets the serialized payload of the jobs chained after this one.</summary>
    public string? ChainedJobPayload { get; set; }
    /// <summary>Gets or sets the identifier of the batch the job belongs to, if any.</summary>
    public string? BatchId { get; set; }
    /// <summary>Gets or sets the W3C traceparent value captured when the job was queued.</summary>
    public string? TraceParent { get; set; }
    /// <summary>Gets or sets the W3C tracestate value captured when the job was queued.</summary>
    public string? TraceState { get; set; }
}
