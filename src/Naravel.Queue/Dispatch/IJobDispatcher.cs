using Naravel.Queue.Batching;
using Naravel.Queue.Jobs;

namespace Naravel.Queue.Dispatch;

/// <summary>Entry point for pushing jobs onto queues. Equivalent to Laravel's Bus/Queue facade.</summary>
public interface IJobDispatcher
{
    /// <summary>Dispatch a single job. Returns the generated message id.</summary>
    Task<string> DispatchAsync(IJob job, Action<DispatchOptions>? configure = null, CancellationToken cancellationToken = default);

    /// <summary>Build a chain of jobs that run one after another, only continuing if the previous one succeeds.</summary>
    JobChain Chain(params IJob[] jobs);

    /// <summary>
    /// Internal: dispatch the head of a chain, encoding the rest so the worker can continue it after success.
    /// Also used by the worker itself to push the next link once the current one completes.
    /// </summary>
    Task<string> DispatchChainAsync(IReadOnlyList<IJob> remainingJobs, Action<DispatchOptions>? configure, CancellationToken cancellationToken);

    /// <summary>
    /// Dispatch many jobs as a batch and get notified when all of them finish.
    /// Note: the completion callback only fires while the dispatching process is alive
    /// (it is tracked in-memory) - for cross-process batches, poll <see cref="IBatchRepository"/> instead.
    /// </summary>
    Task<QueueBatch> BatchAsync(
        IEnumerable<IJob> jobs,
        Action<DispatchOptions>? configure = null,
        Action<BatchOptions>? batchConfigure = null,
        CancellationToken cancellationToken = default);
}
