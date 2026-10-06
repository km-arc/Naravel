using Microsoft.EntityFrameworkCore;
namespace Naravel.Queue.Database;

/// <summary>EF Core entity backing the "database" driver - equivalent to Laravel's jobs table.</summary>
public class JobRecord
{
    public string Id { get; set; } = default!;
    public string Queue { get; set; } = default!;
    public string? Connection { get; set; }
    public string JobType { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? ReservedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public string? Error { get; set; }
    public string? ChainedJobPayload { get; set; }
    public string? BatchId { get; set; }
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
}
