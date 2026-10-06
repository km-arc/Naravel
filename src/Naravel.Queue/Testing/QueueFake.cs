using Naravel.Queue.Batching;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Jobs;

namespace Naravel.Queue.Testing;

/// <summary>In-memory dispatcher fake for application tests; jobs are recorded and never executed.</summary>
/// <remarks>
/// <b>Laravel equivalent:</b> `Queue::fake`. It exists to assert dispatch and chaining without a worker or broker;
/// it intentionally does not run jobs or reproduce Laravel's facade/global state.
/// </remarks>
public sealed class QueueFake : IJobDispatcher
{
    private readonly object _gate = new();
    private readonly List<IJob> _dispatched = new();
    private readonly List<IReadOnlyList<IJob>> _chains = new();

    public Task<string> DispatchAsync(IJob job, Action<DispatchOptions>? configure = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        configure?.Invoke(new DispatchOptions());
        lock (_gate) _dispatched.Add(job);
        return Task.FromResult(Guid.NewGuid().ToString("N"));
    }

    public JobChain Chain(params IJob[] jobs) => new(this, jobs);

    public Task<string> DispatchChainAsync(
        IReadOnlyList<IJob> remainingJobs,
        Action<DispatchOptions>? configure,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (remainingJobs.Count == 0)
            throw new ArgumentException("Chain has no jobs left to dispatch.", nameof(remainingJobs));
        configure?.Invoke(new DispatchOptions());
        lock (_gate)
        {
            _dispatched.Add(remainingJobs[0]);
            if (remainingJobs.Count > 1) _chains.Add(remainingJobs.Skip(1).ToArray());
        }
        return Task.FromResult(Guid.NewGuid().ToString("N"));
    }

    public Task<QueueBatch> BatchAsync(
        IEnumerable<IJob> jobs,
        Action<DispatchOptions>? configure = null,
        Action<BatchOptions>? batchConfigure = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var batchJobs = jobs.ToArray();
        var options = new BatchOptions();
        batchConfigure?.Invoke(options);
        configure?.Invoke(new DispatchOptions());
        lock (_gate) _dispatched.AddRange(batchJobs);
        return Task.FromResult(new QueueBatch { TotalJobs = batchJobs.Length, AllowFailures = options.AllowFailures });
    }

    public TJob AssertDispatched<TJob>() where TJob : class, IJob
    {
        lock (_gate)
            return _dispatched.OfType<TJob>().FirstOrDefault()
                ?? throw new InvalidOperationException($"No {typeof(TJob).Name} job was dispatched.");
    }

    public IReadOnlyList<TJob> AssertChained<TJob>() where TJob : class, IJob
    {
        lock (_gate)
        {
            var chained = _chains.SelectMany(chain => chain).OfType<TJob>().ToArray();
            if (chained.Length == 0)
                throw new InvalidOperationException($"No {typeof(TJob).Name} job was found in a dispatched chain.");
            return chained;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _dispatched.Clear();
            _chains.Clear();
        }
    }
}