namespace Naravel.Queue.Jobs;

/// <summary>
/// Contract that every background job must implement. Equivalent to Laravel's ShouldQueue jobs.
/// </summary>
public interface IJob
{
    Task HandleAsync(JobContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Optional contract a job can implement to react when it has permanently failed
/// (i.e. after exhausting MaxAttempts). Equivalent to Laravel's Job::failed().
/// </summary>
public interface IFailedJobHandler
{
    Task FailedAsync(JobContext context, Exception exception, CancellationToken cancellationToken);
}

/// <summary>
/// Convenience base class that gives sensible defaults for retry/backoff/queue placement,
/// similar to Laravel's Illuminate\Bus\Queueable trait.
/// </summary>
public abstract class Job : IJob, IFailedJobHandler
{
    /// <summary>Maximum number of attempts before the job is considered permanently failed.</summary>
    public virtual int MaxAttempts => 3;

    /// <summary>Backoff delay to apply between retries. If there are more attempts than entries, the last value is reused.</summary>
    public virtual TimeSpan[] Backoff => new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60) };

    /// <summary>Queue name this job should be pushed to, unless overridden at dispatch time. Null = "default".</summary>
    public virtual string? Queue => null;

    /// <summary>Connection (driver) name this job should be pushed to, unless overridden at dispatch time. Null = configured default connection.</summary>
    public virtual string? Connection => null;

    public abstract Task HandleAsync(JobContext context, CancellationToken cancellationToken);

    public virtual Task FailedAsync(JobContext context, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;
}
