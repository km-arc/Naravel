using Naravel.Queue.Drivers;

namespace Naravel.Queue.Database;

/// <summary>Maps an envelope to its table row. It is not generic on purpose: <see cref="DatabaseJobEnqueuer"/> uses it without naming a DbContext type, which would otherwise ask the trimmer to keep the constructors of <c>DbContext</c> itself.</summary>
internal static class JobRecordMapper
{
    public static JobRecord ToRecord(QueuedMessage m) => new()
    {
        Id = m.Id,
        Queue = m.Queue,
        Connection = m.Connection,
        JobType = m.JobType,
        Payload = m.Payload,
        Attempts = m.Attempts,
        MaxAttempts = m.MaxAttempts,
        Priority = m.Priority,
        CreatedAt = m.CreatedAt,
        AvailableAt = m.AvailableAt,
        ReservedAt = m.ReservedAt,
        ReservationId = m.ReservationId,
        Error = m.Error,
        ChainedJobPayload = m.ChainedJobPayload,
        BatchId = m.BatchId,
        TraceParent = m.TraceParent,
        TraceState = m.TraceState
    };
}
