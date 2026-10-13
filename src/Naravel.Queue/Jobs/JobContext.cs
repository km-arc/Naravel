using Naravel.Queue.Drivers;

namespace Naravel.Queue.Jobs;

/// <summary>
/// Runtime information passed to a job when it executes, plus access to the request-scoped
/// service provider so the job can resolve scoped dependencies (DbContext, etc.) itself.
/// </summary>
public sealed class JobContext
{
    public JobContext(IServiceProvider services, QueuedMessage message)
    {
        Services = services;
        Message = message;
    }

    /// <summary>Scoped service provider, one scope per job execution (like a normal web request scope).</summary>
    public IServiceProvider Services { get; }

    /// <summary>The raw queued message/envelope this job came from.</summary>
    public QueuedMessage Message { get; }

    public string JobId => Message.Id;
    public string QueueName => Message.Queue;
    public int Attempt => Message.Attempts + 1;
    public int MaxAttempts => Message.MaxAttempts;
    public bool IsLastAttempt => Attempt >= MaxAttempts;

    /// <summary>Arbitrary jobs to run right after this one succeeds. Set via IJobDispatcher.Chain(...).</summary>
    public List<IJob> ChainedJobs { get; } = new();

    /// <summary>Queues another job to run immediately after this one completes successfully, on the same connection/queue.</summary>
    public void Then(IJob job) => ChainedJobs.Add(job);

    /// <summary>
    /// For job middleware that decides not to call <c>next</c>: hand the job back to its queue after <paramref name="delay"/>
    /// without counting an attempt, running its chain or reporting success (PDR-012b). Drivers that cannot delay release immediately.
    /// </summary>
    /// <remarks><b>Laravel equivalent:</b> <c>$job-&gt;release($delay)</c> inside job middleware. <b>Not ported:</b> exception-based control flow.</remarks>
    public void Release(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay), "The delay cannot be negative.");
        Outcome = JobOutcome.Released;
        ReleaseDelay = delay;
    }

    /// <summary>
    /// For job middleware that decides not to call <c>next</c>: acknowledge the message and drop the job without running it,
    /// its chain or success handling (PDR-012b).
    /// </summary>
    /// <remarks><b>Laravel equivalent:</b> <c>$job-&gt;delete()</c> inside job middleware.</remarks>
    public void Skip() => Outcome = JobOutcome.Skipped;

    internal JobOutcome Outcome { get; private set; }
    internal TimeSpan ReleaseDelay { get; private set; }
    internal bool JobExecuted { get; set; }
}

internal enum JobOutcome { None, Released, Skipped }
