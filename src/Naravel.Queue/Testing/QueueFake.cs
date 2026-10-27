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
    private readonly HashSet<string> _uniqueKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// Records the job. For an <see cref="IUniqueJob"/> the fake mimics the real gate: a second dispatch of the same key
    /// returns <see langword="null"/> and is not recorded; keys stay locked until <see cref="Reset"/> because the fake never runs jobs.
    /// </summary>
    public Task<string?> DispatchAsync(IJob job, Action<DispatchOptions>? configure = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        configure?.Invoke(new DispatchOptions());
        lock (_gate)
        {
            if (job is IUniqueJob unique && !_uniqueKeys.Add(unique.UniqueKey))
                return Task.FromResult<string?>(null);
            _dispatched.Add(job);
        }
        return Task.FromResult<string?>(Guid.NewGuid().ToString("N"));
    }

    /// <inheritdoc />
    public JobChain Chain(params IJob[] jobs)
    {
        RejectUniqueJobs(jobs, "a chain");
        return new(this, jobs);
    }

    private static void RejectUniqueJobs(IEnumerable<IJob> jobs, string container)
    {
        var unique = jobs.OfType<IUniqueJob>().FirstOrDefault();
        if (unique is not null)
            throw new ArgumentException($"{unique.GetType().Name} implements IUniqueJob and cannot be part of {container}.", "jobs");
    }

    /// <inheritdoc />
    public Task<string> DispatchChainAsync(
        IReadOnlyList<IJob> remainingJobs,
        Action<DispatchOptions>? configure,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (remainingJobs.Count == 0)
            throw new ArgumentException("Chain has no jobs left to dispatch.", nameof(remainingJobs));
        RejectUniqueJobs(remainingJobs, "a chain");
        configure?.Invoke(new DispatchOptions());
        lock (_gate)
        {
            _dispatched.Add(remainingJobs[0]);
            if (remainingJobs.Count > 1) _chains.Add(remainingJobs.Skip(1).ToArray());
        }
        return Task.FromResult(Guid.NewGuid().ToString("N"));
    }

    /// <inheritdoc />
    public Task<QueueBatch> BatchAsync(
        IEnumerable<IJob> jobs,
        Action<DispatchOptions>? configure = null,
        Action<BatchOptions>? batchConfigure = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var batchJobs = jobs.ToArray();
        RejectUniqueJobs(batchJobs, "a batch");
        var options = new BatchOptions();
        batchConfigure?.Invoke(options);
        configure?.Invoke(new DispatchOptions());
        lock (_gate) _dispatched.AddRange(batchJobs);
        return Task.FromResult(new QueueBatch { TotalJobs = batchJobs.Length, AllowFailures = options.AllowFailures });
    }

    /// <summary>Returns the first dispatched job of type <typeparamref name="TJob"/> and throws when none was dispatched.</summary>
    public TJob AssertDispatched<TJob>() where TJob : class, IJob
    {
        lock (_gate)
            return _dispatched.OfType<TJob>().FirstOrDefault()
                ?? throw new InvalidOperationException($"No {typeof(TJob).Name} job was dispatched.");
    }

    /// <summary>Fails when a <typeparamref name="TJob"/> was dispatched (rejected unique duplicates are not recorded, so they pass).</summary>
    public void AssertNotDispatched<TJob>() where TJob : class, IJob
    {
        lock (_gate)
        {
            var count = _dispatched.OfType<TJob>().Count();
            if (count > 0)
                throw new InvalidOperationException($"Expected no {typeof(TJob).Name} job to be dispatched, but {count} {(count == 1 ? "was" : "were")}.");
        }
    }

    /// <summary>Fails unless exactly <paramref name="expected"/> jobs of type <typeparamref name="TJob"/> were dispatched.</summary>
    public void AssertDispatchedCount<TJob>(int expected) where TJob : class, IJob
    {
        lock (_gate)
        {
            var count = _dispatched.OfType<TJob>().Count();
            if (count != expected)
                throw new InvalidOperationException($"Expected {expected} {typeof(TJob).Name} dispatch(es), but found {count}.");
        }
    }

    /// <summary>Returns every job of type <typeparamref name="TJob"/> found in a recorded chain (excluding chain heads) and throws when there is none.</summary>
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

    /// <summary>Clears all recorded dispatches, chains and held unique keys.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _dispatched.Clear();
            _chains.Clear();
            _uniqueKeys.Clear();
        }
    }
}