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
}
