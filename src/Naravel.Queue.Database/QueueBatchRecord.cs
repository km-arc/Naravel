namespace Naravel.Queue.Database;

/// <summary>EF Core entity backing persistent queue batch state and callback progress.</summary>
/// <remarks>It exists for restart-safe batches; typed callbacks are stored as aliases and JSON payloads, not delegates.</remarks>
public sealed class QueueBatchRecord
{
    /// <summary>Gets or sets the batch identifier.</summary>
    public string Id { get; set; } = default!;
    /// <summary>Gets or sets the total number of jobs in the batch.</summary>
    public int TotalJobs { get; set; }
    /// <summary>Gets or sets the number of jobs that completed.</summary>
    public int CompletedJobs { get; set; }
    /// <summary>Gets or sets the number of jobs that failed.</summary>
    public int FailedJobs { get; set; }
    /// <summary>Gets or sets the number of jobs that were cancelled.</summary>
    public int CancelledJobs { get; set; }
    /// <summary>Gets or sets whether the batch continues after a job fails.</summary>
    public bool AllowFailures { get; set; }
    /// <summary>Gets or sets whether the batch has been cancelled.</summary>
    public bool IsCancelled { get; set; }
    /// <summary>Gets or sets the serialized callbacks run when the batch succeeds.</summary>
    public string ThenCallbacks { get; set; } = "[]";
    /// <summary>Gets or sets the serialized callbacks run when a job fails.</summary>
    public string CatchCallbacks { get; set; } = "[]";
    /// <summary>Gets or sets the serialized callbacks run when the batch finishes.</summary>
    public string FinallyCallbacks { get; set; } = "[]";
    /// <summary>Gets or sets whether the success callbacks have already run.</summary>
    public bool ThenCallbacksCompleted { get; set; }
    /// <summary>Gets or sets whether the failure callbacks have already run.</summary>
    public bool CatchCallbacksCompleted { get; set; }
    /// <summary>Gets or sets whether the finally callbacks have already run.</summary>
    public bool FinallyCallbacksCompleted { get; set; }
}