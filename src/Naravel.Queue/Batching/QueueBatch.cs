using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Naravel.Queue.Batching;

/// <summary>A handle representing a group of jobs dispatched together. Mirrors Laravel's Bus::batch().</summary>
public class QueueBatch
{
    /// <summary>Unique identifier of the batch, generated as a GUID without hyphens.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    /// <summary>Gets the total number of jobs in the batch.</summary>
    public int TotalJobs { get; init; }
    /// <summary>Gets the number of jobs that have not yet completed, failed or been cancelled.</summary>
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
    /// <summary>Gets a value indicating whether every job in the batch has completed, failed or been cancelled.</summary>
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
    [RequiresUnreferencedCode("Serialises the callback payload with reflection based System.Text.Json. Trimmed or Native AOT applications use the overload that takes JsonTypeInfo<TPayload>.")]
    [RequiresDynamicCode("Serialises the callback payload with reflection based System.Text.Json. Trimmed or Native AOT applications use the overload that takes JsonTypeInfo<TPayload>.")]
    public BatchOptions Then<TPayload>(string alias, TPayload payload)
    {
        _then.Add(BatchCallbackReference.Create(alias, payload));
        return this;
    }

    /// <summary>Same as <see cref="Then{TPayload}(string, TPayload)"/>, with source-generated metadata for the payload (trimming and Native AOT).</summary>
    /// <param name="alias">The alias registered in <see cref="BatchCallbackRegistry"/>.</param>
    /// <param name="payload">The payload passed to the callback.</param>
    /// <param name="payloadTypeInfo">JSON metadata for <typeparamref name="TPayload"/>.</param>
    public BatchOptions Then<TPayload>(string alias, TPayload payload, JsonTypeInfo<TPayload> payloadTypeInfo)
    {
        _then.Add(BatchCallbackReference.Create(alias, payload, payloadTypeInfo));
        return this;
    }

    /// <summary>Registers a callback for a batch that contains permanent failures.</summary>
    [RequiresUnreferencedCode("Serialises the callback payload with reflection based System.Text.Json. Trimmed or Native AOT applications use the overload that takes JsonTypeInfo<TPayload>.")]
    [RequiresDynamicCode("Serialises the callback payload with reflection based System.Text.Json. Trimmed or Native AOT applications use the overload that takes JsonTypeInfo<TPayload>.")]
    public BatchOptions Catch<TPayload>(string alias, TPayload payload)
    {
        _catch.Add(BatchCallbackReference.Create(alias, payload));
        return this;
    }

    /// <summary>Same as <see cref="Catch{TPayload}(string, TPayload)"/>, with source-generated metadata for the payload (trimming and Native AOT).</summary>
    /// <param name="alias">The alias registered in <see cref="BatchCallbackRegistry"/>.</param>
    /// <param name="payload">The payload passed to the callback.</param>
    /// <param name="payloadTypeInfo">JSON metadata for <typeparamref name="TPayload"/>.</param>
    public BatchOptions Catch<TPayload>(string alias, TPayload payload, JsonTypeInfo<TPayload> payloadTypeInfo)
    {
        _catch.Add(BatchCallbackReference.Create(alias, payload, payloadTypeInfo));
        return this;
    }

    /// <summary>Registers a callback for any terminal batch outcome.</summary>
    [RequiresUnreferencedCode("Serialises the callback payload with reflection based System.Text.Json. Trimmed or Native AOT applications use the overload that takes JsonTypeInfo<TPayload>.")]
    [RequiresDynamicCode("Serialises the callback payload with reflection based System.Text.Json. Trimmed or Native AOT applications use the overload that takes JsonTypeInfo<TPayload>.")]
    public BatchOptions Finally<TPayload>(string alias, TPayload payload)
    {
        _finally.Add(BatchCallbackReference.Create(alias, payload));
        return this;
    }

    /// <summary>Same as <see cref="Finally{TPayload}(string, TPayload)"/>, with source-generated metadata for the payload (trimming and Native AOT).</summary>
    /// <param name="alias">The alias registered in <see cref="BatchCallbackRegistry"/>.</param>
    /// <param name="payload">The payload passed to the callback.</param>
    /// <param name="payloadTypeInfo">JSON metadata for <typeparamref name="TPayload"/>.</param>
    public BatchOptions Finally<TPayload>(string alias, TPayload payload, JsonTypeInfo<TPayload> payloadTypeInfo)
    {
        _finally.Add(BatchCallbackReference.Create(alias, payload, payloadTypeInfo));
        return this;
    }

    /// <summary>Gets the callbacks registered with <see cref="Then{TPayload}(string, TPayload)"/>.</summary>
    public IReadOnlyList<BatchCallbackReference> ThenCallbacks => _then;
    /// <summary>Gets the callbacks registered with <see cref="Catch{TPayload}(string, TPayload)"/>.</summary>
    public IReadOnlyList<BatchCallbackReference> CatchCallbacks => _catch;
    /// <summary>Gets the callbacks registered with <see cref="Finally{TPayload}(string, TPayload)"/>.</summary>
    public IReadOnlyList<BatchCallbackReference> FinallyCallbacks => _finally;
}

/// <summary>Serializable alias and JSON payload for a registered batch callback.</summary>
/// <param name="Alias">The alias under which the callback is registered in <see cref="BatchCallbackRegistry"/>.</param>
/// <param name="Payload">The JSON-serialized payload passed to the callback.</param>
/// <remarks>Laravel callbacks are delegates; Naravel intentionally persists no delegate or CLR type name.</remarks>
public sealed record BatchCallbackReference(string Alias, string Payload)
{
    [RequiresUnreferencedCode("Serialises the callback payload with reflection based System.Text.Json.")]
    [RequiresDynamicCode("Serialises the callback payload with reflection based System.Text.Json.")]
    internal static BatchCallbackReference Create<TPayload>(string alias, TPayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        return new BatchCallbackReference(alias, JsonSerializer.Serialize(payload));
    }

    internal static BatchCallbackReference Create<TPayload>(string alias, TPayload payload, JsonTypeInfo<TPayload> payloadTypeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentNullException.ThrowIfNull(payloadTypeInfo);
        return new BatchCallbackReference(alias, JsonSerializer.Serialize(payload, payloadTypeInfo));
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

    private sealed class Handler<TPayload>(Func<QueueBatch, TPayload, CancellationToken, Task> callback, JsonTypeInfo<TPayload>? payloadTypeInfo) : IHandler
    {
        public Task InvokeAsync(QueueBatch batch, string payload, CancellationToken cancellationToken)
        {
            var value = (payloadTypeInfo is null ? ReflectionDeserialize(payload) : JsonSerializer.Deserialize(payload, payloadTypeInfo))
                ?? throw new InvalidDataException($"Batch callback payload for '{typeof(TPayload).Name}' was null.");
            return callback(batch, value, cancellationToken);
        }

        // Reached only for a handler registered through Register<TPayload>(alias, callback), which carries
        // RequiresUnreferencedCode and RequiresDynamicCode, so the warning is raised where the handler is registered.
        // With Native AOT, System.Text.Json reflection is disabled and this call throws instead of misreading the payload.
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The reflection path is only registered through the annotated Register<TPayload>(alias, callback) overload.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The reflection path is only registered through the annotated Register<TPayload>(alias, callback) overload; Native AOT disables it and System.Text.Json throws.")]
        private static TPayload? ReflectionDeserialize(string payload) => JsonSerializer.Deserialize<TPayload>(payload);
    }

    private readonly ConcurrentDictionary<string, IHandler> _handlers = new(StringComparer.Ordinal);

    /// <summary>Registers a callback under <paramref name="alias"/> that receives the batch and a payload deserialized as <typeparamref name="TPayload"/>.</summary>
    [RequiresUnreferencedCode("Deserialises the callback payload with reflection based System.Text.Json. Trimmed or Native AOT applications use the overload that takes JsonTypeInfo<TPayload>.")]
    [RequiresDynamicCode("Deserialises the callback payload with reflection based System.Text.Json. Trimmed or Native AOT applications use the overload that takes JsonTypeInfo<TPayload>.")]
    public void Register<TPayload>(string alias, Func<QueueBatch, TPayload, CancellationToken, Task> callback)
        => AddHandler(alias, callback, null);

    /// <summary>Same as <see cref="Register{TPayload}(string, Func{QueueBatch, TPayload, CancellationToken, Task})"/>, with source-generated metadata for the payload (trimming and Native AOT).</summary>
    /// <param name="alias">The alias batches refer to.</param>
    /// <param name="callback">The callback.</param>
    /// <param name="payloadTypeInfo">JSON metadata for <typeparamref name="TPayload"/>.</param>
    public void Register<TPayload>(string alias, Func<QueueBatch, TPayload, CancellationToken, Task> callback, JsonTypeInfo<TPayload> payloadTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(payloadTypeInfo);
        AddHandler(alias, callback, payloadTypeInfo);
    }

    private void AddHandler<TPayload>(string alias, Func<QueueBatch, TPayload, CancellationToken, Task> callback, JsonTypeInfo<TPayload>? payloadTypeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentNullException.ThrowIfNull(callback);
        if (!_handlers.TryAdd(alias, new Handler<TPayload>(callback, payloadTypeInfo)))
            throw new InvalidOperationException($"A batch callback is already registered as '{alias}'.");
    }

    /// <summary>Invokes the registered handler for each callback reference in order, throwing when an alias is not registered.</summary>
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
    /// <summary>Starts tracking a batch together with its failure behavior and callbacks.</summary>
    Task RegisterAsync(QueueBatch batch, BatchOptions options, CancellationToken cancellationToken);
    /// <summary>Returns the batch with the given id, or <see langword="null"/> if it is unknown.</summary>
    Task<QueueBatch?> GetAsync(string batchId, CancellationToken cancellationToken);
    /// <summary>Returns whether the batch has been cancelled or stopped after a failure.</summary>
    Task<bool> IsCancelledAsync(string batchId, CancellationToken cancellationToken);
    /// <summary>Marks the batch as cancelled so its remaining jobs are skipped.</summary>
    Task CancelAsync(string batchId, CancellationToken cancellationToken);
    /// <summary>Records that one job of the batch completed successfully.</summary>
    Task MarkJobCompletedAsync(string batchId, CancellationToken cancellationToken);
    /// <summary>Records that one job of the batch failed permanently.</summary>
    Task MarkJobFailedAsync(string batchId, Exception exception, CancellationToken cancellationToken);
    /// <summary>Records that one job of the batch was skipped because the batch was cancelled.</summary>
    Task MarkJobCancelledAsync(string batchId, CancellationToken cancellationToken);
    /// <summary>Re-runs completion callbacks for batches that finished but whose callbacks were not yet delivered.</summary>
    Task ResumePendingCallbacksAsync(CancellationToken cancellationToken);
}

/// <summary>In-process <see cref="IBatchRepository"/> that keeps batch state in memory and runs callbacks in the process that records the final job.</summary>
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

    /// <summary>Initializes a new instance of the <see cref="InMemoryBatchRepository"/> class using the given callback registry, or an empty one when <see langword="null"/>.</summary>
    public InMemoryBatchRepository(BatchCallbackRegistry? callbacks = null)
        => _callbacks = callbacks ?? new BatchCallbackRegistry();

    /// <inheritdoc />
    public Task RegisterAsync(QueueBatch batch, BatchOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        batch.AllowFailures = options.AllowFailures;
        _batches[batch.Id] = new Entry { Batch = batch, Options = options };
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<QueueBatch?> GetAsync(string batchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_batches.TryGetValue(batchId, out var entry) ? entry.Batch : null);
    }

    /// <inheritdoc />
    public Task<bool> IsCancelledAsync(string batchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_batches.TryGetValue(batchId, out var entry) && entry.Batch.IsCancelled);
    }

    /// <inheritdoc />
    public Task CancelAsync(string batchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_batches.TryGetValue(batchId, out var entry))
            lock (entry.Lock) entry.Batch.IsCancelled = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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
