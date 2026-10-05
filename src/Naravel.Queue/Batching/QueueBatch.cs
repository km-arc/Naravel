using System.Collections.Concurrent;

namespace Naravel.Queue.Batching;

/// <summary>A handle representing a group of jobs dispatched together. Mirrors Laravel's Bus::batch().</summary>
public class QueueBatch
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int TotalJobs { get; init; }
    public int PendingJobs => TotalJobs - CompletedJobs - FailedJobs;
    public int CompletedJobs { get; internal set; }
    public int FailedJobs { get; internal set; }
    public bool IsFinished => CompletedJobs + FailedJobs >= TotalJobs;
}

public class BatchOptions
{
    /// <summary>Invoked once when every job in the batch has finished (successfully or not), if the process is still alive.</summary>
    public Func<QueueBatch, CancellationToken, Task>? OnCompleted { get; set; }

    /// <summary>Invoked every time a single job in the batch fails permanently.</summary>
    public Func<QueueBatch, Exception, CancellationToken, Task>? OnJobFailed { get; set; }
}

/// <summary>
/// Tracks in-progress batches. Default implementation is in-memory (works great for a single worker
/// process, which is the common case for small/medium apps). Swap in a Redis/DB-backed implementation
/// for multi-process deployments by registering your own IBatchRepository.
/// </summary>
public interface IBatchRepository
{
    Task RegisterAsync(QueueBatch batch, BatchOptions options, CancellationToken cancellationToken);
    Task MarkJobCompletedAsync(string batchId, CancellationToken cancellationToken);
    Task MarkJobFailedAsync(string batchId, Exception exception, CancellationToken cancellationToken);
}

public class InMemoryBatchRepository : IBatchRepository
{
    private class Entry
    {
        public required QueueBatch Batch;
        public required BatchOptions Options;
        public readonly object Lock = new();
    }

    private readonly ConcurrentDictionary<string, Entry> _batches = new();

    public Task RegisterAsync(QueueBatch batch, BatchOptions options, CancellationToken cancellationToken)
    {
        _batches[batch.Id] = new Entry { Batch = batch, Options = options };
        return Task.CompletedTask;
    }

    public async Task MarkJobCompletedAsync(string batchId, CancellationToken cancellationToken)
    {
        if (!_batches.TryGetValue(batchId, out var entry)) return;
        bool finished;
        lock (entry.Lock)
        {
            entry.Batch.CompletedJobs++;
            finished = entry.Batch.IsFinished;
        }
        if (finished && entry.Options.OnCompleted != null)
        {
            await entry.Options.OnCompleted(entry.Batch, cancellationToken);
            _batches.TryRemove(batchId, out _);
        }
    }

    public async Task MarkJobFailedAsync(string batchId, Exception exception, CancellationToken cancellationToken)
    {
        if (!_batches.TryGetValue(batchId, out var entry)) return;
        bool finished;
        lock (entry.Lock)
        {
            entry.Batch.FailedJobs++;
            finished = entry.Batch.IsFinished;
        }
        if (entry.Options.OnJobFailed != null)
            await entry.Options.OnJobFailed(entry.Batch, exception, cancellationToken);
        if (finished && entry.Options.OnCompleted != null)
        {
            await entry.Options.OnCompleted(entry.Batch, cancellationToken);
            _batches.TryRemove(batchId, out _);
        }
    }
}
