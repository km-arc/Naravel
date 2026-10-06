using System.Collections.Concurrent;
using System.Text.Json;

namespace Naravel.Queue.Batching;

/// <summary>A handle representing a group of jobs dispatched together. Mirrors Laravel's Bus::batch().</summary>
public class QueueBatch
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int TotalJobs { get; init; }
    public int PendingJobs => TotalJobs - CompletedJobs - FailedJobs - CancelledJobs;
    /// <summary>Number of jobs that completed successfully.</summary>
    public int CompletedJobs { get; set; }
    /// <summary>Number of jobs that reached permanent failure.</summary>
    public int FailedJobs { get; set; }
    /// <summary>Number of queued jobs skipped after cancellation.</summary>
    public int CancelledJobs { get; set; }
    /// <summary>Whether later jobs continue after a permanent failure.</summary>
    public bool AllowFailures { get; set; }
    /// <summary>Whether the batch has been cancelled or stopped after a failure.</summary>
    public bool IsCancelled { get; set; }
    public bool IsFinished => CompletedJobs + FailedJobs + CancelledJobs >= TotalJobs;
}

/// <summary>Controls batch failure behavior and completion callbacks.</summary>
/// <remarks><b>Laravel equivalent:</b> `Bus::batch` callbacks. Typed callback payloads are registered by alias because delegates are not persisted.</remarks>
public class BatchOptions
{
    private readonly List<BatchCallbackReference> _then = new();
    private readonly List<BatchCallbackReference> _catch = new();
    private readonly List<BatchCallbackReference> _finally = new();

    /// <summary>Whether jobs not yet started continue after a permanent job failure.</summary>
    public bool AllowFailures { get; set; }

    /// <summary>Invoked once when every job in the batch has finished (successfully or not), if the process is still alive.</summary>
    public Func<QueueBatch, CancellationToken, Task>? OnCompleted { get; set; }

    /// <summary>Invoked every time a single job in the batch fails permanently.</summary>
    public Func<QueueBatch, Exception, CancellationToken, Task>? OnJobFailed { get; set; }

    /// <summary>Registers a callback for a batch that completes without failures or cancellation.</summary>
    public BatchOptions Then<TPayload>(string alias, TPayload payload)
    {
        _then.Add(BatchCallbackReference.Create(alias, payload));
        return this;
    }

    /// <summary>Registers a callback for a batch that contains permanent failures.</summary>
    public BatchOptions Catch<TPayload>(string alias, TPayload payload)
    {
        _catch.Add(BatchCallbackReference.Create(alias, payload));
        return this;
    }

    /// <summary>Registers a callback for any terminal batch outcome.</summary>
    public BatchOptions Finally<TPayload>(string alias, TPayload payload)
    {
        _finally.Add(BatchCallbackReference.Create(alias, payload));
        return this;
    }

    public IReadOnlyList<BatchCallbackReference> ThenCallbacks => _then;
    public IReadOnlyList<BatchCallbackReference> CatchCallbacks => _catch;
    public IReadOnlyList<BatchCallbackReference> FinallyCallbacks => _finally;
}

/// <summary>Serializable alias and JSON payload for a registered batch callback.</summary>
/// <remarks>Laravel callbacks are delegates; Naravel intentionally persists no delegate or CLR type name.</remarks>
public sealed record BatchCallbackReference(string Alias, string Payload)
{
    internal static BatchCallbackReference Create<TPayload>(string alias, TPayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        return new BatchCallbackReference(alias, JsonSerializer.Serialize(payload));
    }
}

/// <summary>Resolves batch callback aliases registered by application code.</summary>
/// <remarks>It exists for durable typed callbacks without reflection or type resolution from stored data.</remarks>
public sealed class BatchCallbackRegistry
{
    private interface IHandler
    {
        Task InvokeAsync(QueueBatch batch, string payload, CancellationToken cancellationToken);
    }

    private sealed class Handler<TPayload>(Func<QueueBatch, TPayload, CancellationToken, Task> callback) : IHandler
    {
        public Task InvokeAsync(QueueBatch batch, string payload, CancellationToken cancellationToken)
        {
            var value = JsonSerializer.Deserialize<TPayload>(payload)
                ?? throw new InvalidDataException($"Batch callback payload for '{typeof(TPayload).Name}' was null.");
            return callback(batch, value, cancellationToken);
        }
    }

    private readonly ConcurrentDictionary<string, IHandler> _handlers = new(StringComparer.Ordinal);

    public void Register<TPayload>(string alias, Func<QueueBatch, TPayload, CancellationToken, Task> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentNullException.ThrowIfNull(callback);
        if (!_handlers.TryAdd(alias, new Handler<TPayload>(callback)))
            throw new InvalidOperationException($"A batch callback is already registered as '{alias}'.");
    }

    public async Task InvokeAsync(
        IEnumerable<BatchCallbackReference> callbacks,
        QueueBatch batch,
        CancellationToken cancellationToken)
    {
        foreach (var callback in callbacks)
        {
            if (!_handlers.TryGetValue(callback.Alias, out var handler))
                throw new InvalidOperationException($"No batch callback is registered as '{callback.Alias}'.");
            await handler.InvokeAsync(batch, callback.Payload, cancellationToken);
        }
    }
}

/// <summary>
/// Tracks in-progress batches. Default implementation is in-memory (works great for a single worker
/// process, which is the common case for small/medium apps). Swap in a Redis/DB-backed implementation
/// for multi-process deployments by registering your own IBatchRepository.
/// </summary>
public interface IBatchRepository
{
    Task RegisterAsync(QueueBatch batch, BatchOptions options, CancellationToken cancellationToken);
    Task<QueueBatch?> GetAsync(string batchId, CancellationToken cancellationToken);
    Task<bool> IsCancelledAsync(string batchId, CancellationToken cancellationToken);
    Task CancelAsync(string batchId, CancellationToken cancellationToken);
    Task MarkJobCompletedAsync(string batchId, CancellationToken cancellationToken);
    Task MarkJobFailedAsync(string batchId, Exception exception, CancellationToken cancellationToken);
    Task MarkJobCancelledAsync(string batchId, CancellationToken cancellationToken);
    Task ResumePendingCallbacksAsync(CancellationToken cancellationToken);
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
    private readonly BatchCallbackRegistry _callbacks;

    public InMemoryBatchRepository(BatchCallbackRegistry? callbacks = null)
        => _callbacks = callbacks ?? new BatchCallbackRegistry();

    public Task RegisterAsync(QueueBatch batch, BatchOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        batch.AllowFailures = options.AllowFailures;
        _batches[batch.Id] = new Entry { Batch = batch, Options = options };
        return Task.CompletedTask;
    }

    public Task<QueueBatch?> GetAsync(string batchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_batches.TryGetValue(batchId, out var entry) ? entry.Batch : null);
    }

    public Task<bool> IsCancelledAsync(string batchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_batches.TryGetValue(batchId, out var entry) && entry.Batch.IsCancelled);
    }

    public Task CancelAsync(string batchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_batches.TryGetValue(batchId, out var entry))
            lock (entry.Lock) entry.Batch.IsCancelled = true;
        return Task.CompletedTask;
    }

    public async Task MarkJobCompletedAsync(string batchId, CancellationToken cancellationToken)
    {
        if (!_batches.TryGetValue(batchId, out var entry)) return;
        bool finished;
        lock (entry.Lock)
        {
            if (entry.Batch.IsFinished) return;
            entry.Batch.CompletedJobs++;
            finished = entry.Batch.IsFinished;
        }
        if (finished && entry.Options.OnCompleted != null)
        {
            await entry.Options.OnCompleted(entry.Batch, cancellationToken);
        }
        if (finished) await InvokeTerminalCallbacksAsync(entry, cancellationToken);
    }

    public async Task MarkJobFailedAsync(string batchId, Exception exception, CancellationToken cancellationToken)
    {
        if (!_batches.TryGetValue(batchId, out var entry)) return;
        bool finished;
        lock (entry.Lock)
        {
            if (entry.Batch.IsFinished) return;
            entry.Batch.FailedJobs++;
            if (!entry.Batch.AllowFailures) entry.Batch.IsCancelled = true;
            finished = entry.Batch.IsFinished;
        }
        if (entry.Options.OnJobFailed != null)
            await entry.Options.OnJobFailed(entry.Batch, exception, cancellationToken);
        if (finished && entry.Options.OnCompleted != null)
        {
            await entry.Options.OnCompleted(entry.Batch, cancellationToken);
        }
        if (finished) await InvokeTerminalCallbacksAsync(entry, cancellationToken);
    }

    public async Task MarkJobCancelledAsync(string batchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_batches.TryGetValue(batchId, out var entry))
        {
            bool finished;
            lock (entry.Lock)
            {
                if (entry.Batch.IsFinished) return;
                entry.Batch.CancelledJobs++;
                finished = entry.Batch.IsFinished;
            }
            if (finished)
            {
                if (entry.Options.OnCompleted != null)
                    await entry.Options.OnCompleted(entry.Batch, cancellationToken);
                await InvokeTerminalCallbacksAsync(entry, cancellationToken);
            }
        }
    }

    public Task ResumePendingCallbacksAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private async Task InvokeTerminalCallbacksAsync(Entry entry, CancellationToken cancellationToken)
    {
        if (entry.Batch.FailedJobs == 0 && !entry.Batch.IsCancelled)
            await _callbacks.InvokeAsync(entry.Options.ThenCallbacks, entry.Batch, cancellationToken);
        if (entry.Batch.FailedJobs > 0)
            await _callbacks.InvokeAsync(entry.Options.CatchCallbacks, entry.Batch, cancellationToken);
        await _callbacks.InvokeAsync(entry.Options.FinallyCallbacks, entry.Batch, cancellationToken);
    }
}
