using System.Text.Json;
using System.Diagnostics;
using Naravel.Queue.Batching;
using Naravel.Queue.Drivers;
using Naravel.Queue.Jobs;
using Naravel.Queue.Serialization;

namespace Naravel.Queue.Dispatch;

/// <summary>Wire format for a single link in a chain, stored inside QueuedMessage.ChainedJobPayload.</summary>
public record ChainLink(string JobType, string Payload);

public class JobDispatcher : IJobDispatcher
{
    private readonly QueueManager _manager;
    private readonly IJobSerializer _serializer;
    private readonly IJobTypeRegistry _jobTypes;
    private readonly IBatchRepository _batchRepository;
    private readonly IUniqueJobGate? _uniqueGate;

    public JobDispatcher(
        QueueManager manager,
        IJobSerializer serializer,
        IJobTypeRegistry jobTypes,
        IBatchRepository batchRepository,
        IUniqueJobGate? uniqueGate = null)
    {
        _uniqueGate = uniqueGate;
        _manager = manager;
        _serializer = serializer;
        _jobTypes = jobTypes;
        _batchRepository = batchRepository;
    }

    public async Task<string?> DispatchAsync(IJob job, Action<DispatchOptions>? configure = null, CancellationToken cancellationToken = default)
    {
        var options = BuildOptions(job, configure);
        var message = BuildMessage(job, options);
        var driver = _manager.Connection(options.Connection);

        if (job is not IUniqueJob unique)
        {
            await driver.PushAsync(message, cancellationToken);
            return message.Id;
        }

        var gate = _uniqueGate ?? throw new InvalidOperationException(
            $"{job.GetType().Name} implements IUniqueJob, but no IUniqueJobGate is registered, so uniqueness cannot be enforced. " +
            "Call services.AddQueueCoordination() (package Naravel.Queue.Coordination, which needs AddNaravelCache()).");

        if (!await gate.TryAcquireAsync(unique.UniqueKey, message.Id, unique.UniqueFor, cancellationToken))
            return null;

        try
        {
            await driver.PushAsync(message, cancellationToken);
        }
        catch
        {
            // The job never reached the queue, so nothing would ever release this key: free it now instead of
            // blocking every later dispatch until the TTL runs out.
            try { await gate.ReleaseAsync(unique.UniqueKey, message.Id, CancellationToken.None); }
            catch { /* the original push failure is the error the caller needs; the TTL is the fallback */ }
            throw;
        }
        return message.Id;
    }

    public JobChain Chain(params IJob[] jobs)
    {
        RejectUniqueJobs(jobs, "a chain");
        return new(this, jobs);
    }

    private static void RejectUniqueJobs(IEnumerable<IJob> jobs, string container)
    {
        var unique = jobs.OfType<IUniqueJob>().FirstOrDefault();
        if (unique is not null)
            throw new ArgumentException(
                $"{unique.GetType().Name} implements IUniqueJob and cannot be part of {container}: a rejected duplicate would stall the sequence. Dispatch it on its own.",
                "jobs");
    }

    public async Task<string> DispatchChainAsync(IReadOnlyList<IJob> remainingJobs, Action<DispatchOptions>? configure, CancellationToken cancellationToken)
    {
        if (remainingJobs.Count == 0)
            throw new ArgumentException("Chain has no jobs left to dispatch.", nameof(remainingJobs));
        RejectUniqueJobs(remainingJobs, "a chain");

        var head = remainingJobs[0];
        var options = BuildOptions(head, configure);
        var message = BuildMessage(head, options);

        if (remainingJobs.Count > 1)
        {
            var rest = remainingJobs.Skip(1)
                .Select(CreateChainLink)
                .ToList();
            message.ChainedJobPayload = JsonSerializer.Serialize(rest);
        }

        var driver = _manager.Connection(options.Connection);
        await driver.PushAsync(message, cancellationToken);
        return message.Id;
    }

    public async Task<QueueBatch> BatchAsync(
        IEnumerable<IJob> jobs,
        Action<DispatchOptions>? configure = null,
        Action<BatchOptions>? batchConfigure = null,
        CancellationToken cancellationToken = default)
    {
        var jobList = jobs.ToList();
        RejectUniqueJobs(jobList, "a batch");
        var batchOptions = new BatchOptions();
        batchConfigure?.Invoke(batchOptions);

        var batch = new QueueBatch { TotalJobs = jobList.Count, AllowFailures = batchOptions.AllowFailures };
        await _batchRepository.RegisterAsync(batch, batchOptions, cancellationToken);

        foreach (var job in jobList)
        {
            var options = BuildOptions(job, configure);
            var message = BuildMessage(job, options);
            message.BatchId = batch.Id;
            var driver = _manager.Connection(options.Connection);
            await driver.PushAsync(message, cancellationToken);
        }

        return batch;
    }

    private static DispatchOptions BuildOptions(IJob job, Action<DispatchOptions>? configure)
    {
        var options = new DispatchOptions();
        if (job is Job baseJob)
        {
            if (baseJob.Queue != null) options.OnQueue(baseJob.Queue);
            if (baseJob.Connection != null) options.OnConnection(baseJob.Connection);
            options.WithMaxAttempts(baseJob.MaxAttempts);
        }
        configure?.Invoke(options);
        return options;
    }

    private ChainLink CreateChainLink(IJob job)
    {
        _jobTypes.Register(job.GetType());
        return new ChainLink(_serializer.GetTypeName(job.GetType()), _serializer.Serialize(job));
    }

    private QueuedMessage BuildMessage(IJob job, DispatchOptions options)
    {
        _jobTypes.Register(job.GetType());
        return new QueuedMessage
        {
            JobType = _serializer.GetTypeName(job.GetType()),
            Payload = _serializer.Serialize(job),
            Connection = options.Connection ?? _manager.DefaultConnectionName,
            Queue = options.Queue ?? "default",
            MaxAttempts = options.MaxAttempts ?? (job as Job)?.MaxAttempts ?? 3,
            AvailableAt = options.ResolveAvailableAt(),
            Priority = options.Priority,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString
        };
    }
}
