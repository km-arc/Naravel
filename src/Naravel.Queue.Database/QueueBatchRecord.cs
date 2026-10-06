namespace Naravel.Queue.Database;

/// <summary>EF Core entity backing persistent queue batch state and callback progress.</summary>
/// <remarks>It exists for restart-safe batches; typed callbacks are stored as aliases and JSON payloads, not delegates.</remarks>
public sealed class QueueBatchRecord
{
    public string Id { get; set; } = default!;
    public int TotalJobs { get; set; }
    public int CompletedJobs { get; set; }
    public int FailedJobs { get; set; }
    public int CancelledJobs { get; set; }
    public bool AllowFailures { get; set; }
    public bool IsCancelled { get; set; }
    public string ThenCallbacks { get; set; } = "[]";
    public string CatchCallbacks { get; set; } = "[]";
    public string FinallyCallbacks { get; set; } = "[]";
    public bool ThenCallbacksCompleted { get; set; }
    public bool CatchCallbacksCompleted { get; set; }
    public bool FinallyCallbacksCompleted { get; set; }
}