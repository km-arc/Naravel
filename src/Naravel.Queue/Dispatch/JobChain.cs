using Naravel.Queue.Jobs;

namespace Naravel.Queue.Dispatch;

/// <summary>
/// A sequence of jobs that run one after another; if any job in the chain fails permanently,
/// the rest of the chain is not executed. Equivalent to Laravel's Bus::chain([...]).
/// </summary>
public class JobChain
{
    private readonly IJobDispatcher _dispatcher;
    internal readonly List<IJob> Jobs;

    internal JobChain(IJobDispatcher dispatcher, IEnumerable<IJob> jobs)
    {
        _dispatcher = dispatcher;
        Jobs = jobs.ToList();
        if (Jobs.Count == 0)
            throw new ArgumentException("A chain must contain at least one job.", nameof(jobs));
    }

    /// <summary>Dispatch the chain: pushes the first job now; each subsequent job is pushed only after the previous one succeeds.</summary>
    public Task<string> DispatchAsync(Action<DispatchOptions>? configure = null, CancellationToken cancellationToken = default)
        => _dispatcher.DispatchChainAsync(Jobs, configure, cancellationToken);
}
