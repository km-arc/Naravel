using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Naravel.Queue.Batching;
using Naravel.Queue.Serialization;

namespace Naravel.Queue.Database;

/// <summary>Persistent batch tracking using the application's EF Core context.</summary>
/// <remarks><b>Laravel equivalent:</b> queue batch repository. It exists for restart-safe batch state; it does not own the application DbContext or persist delegates.</remarks>
public sealed class DatabaseBatchRepository<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TContext> : IBatchRepository where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly BatchCallbackRegistry _callbacks;

    /// <summary>Initializes the repository with the context factory and the registry used to resolve batch callbacks.</summary>
    public DatabaseBatchRepository(IDbContextFactory<TContext> contextFactory, BatchCallbackRegistry callbacks)
    {
        _contextFactory = contextFactory;
        _callbacks = callbacks;
    }

    /// <inheritdoc />
    public async Task RegisterAsync(QueueBatch batch, BatchOptions options, CancellationToken cancellationToken)
    {
        EnsurePersistable(options);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        batch.AllowFailures = options.AllowFailures;
        db.Set<QueueBatchRecord>().Add(new QueueBatchRecord
        {
            Id = batch.Id,
            TotalJobs = batch.TotalJobs,
            AllowFailures = options.AllowFailures,
            ThenCallbacks = QueueEnvelopeJson.SerializeCallbacks(options.ThenCallbacks),
            CatchCallbacks = QueueEnvelopeJson.SerializeCallbacks(options.CatchCallbacks),
            FinallyCallbacks = QueueEnvelopeJson.SerializeCallbacks(options.FinallyCallbacks)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<QueueBatch?> GetAsync(string batchId, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.Set<QueueBatchRecord>().AsNoTracking()
            .SingleOrDefaultAsync(batch => batch.Id == batchId, cancellationToken);
        return record is null ? null : ToBatch(record);
    }

    /// <inheritdoc />
    public async Task<bool> IsCancelledAsync(string batchId, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<QueueBatchRecord>().AsNoTracking()
            .Where(batch => batch.Id == batchId)
            .Select(batch => batch.IsCancelled)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task CancelAsync(string batchId, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Set<QueueBatchRecord>().Where(batch => batch.Id == batchId)
            .ExecuteUpdateAsync(update => update.SetProperty(batch => batch.IsCancelled, true), cancellationToken);
    }

    /// <inheritdoc />
    public Task MarkJobCompletedAsync(string batchId, CancellationToken cancellationToken)
        => IncrementAsync(batchId, completed: true, failed: false, cancelled: false, cancellationToken);

    /// <inheritdoc />
    public Task MarkJobFailedAsync(string batchId, Exception exception, CancellationToken cancellationToken)
        => IncrementAsync(batchId, completed: false, failed: true, cancelled: false, cancellationToken);

    /// <inheritdoc />
    public Task MarkJobCancelledAsync(string batchId, CancellationToken cancellationToken)
        => IncrementAsync(batchId, completed: false, failed: false, cancelled: true, cancellationToken);

    /// <inheritdoc />
    public async Task ResumePendingCallbacksAsync(CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var records = await db.Set<QueueBatchRecord>().AsNoTracking()
            .Where(batch => batch.CompletedJobs + batch.FailedJobs + batch.CancelledJobs >= batch.TotalJobs)
            .ToListAsync(cancellationToken);
        foreach (var record in records)
            await InvokeTerminalCallbacksAsync(record, cancellationToken);
    }

    private async Task IncrementAsync(string batchId, bool completed, bool failed, bool cancelled, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Set<QueueBatchRecord>().Where(batch => batch.Id == batchId &&
            batch.CompletedJobs + batch.FailedJobs + batch.CancelledJobs < batch.TotalJobs);
        var update = query.ExecuteUpdateAsync(setters => setters
            .SetProperty(batch => batch.CompletedJobs, batch => batch.CompletedJobs + (completed ? 1 : 0))
            .SetProperty(batch => batch.FailedJobs, batch => batch.FailedJobs + (failed ? 1 : 0))
            .SetProperty(batch => batch.CancelledJobs, batch => batch.CancelledJobs + (cancelled ? 1 : 0))
            .SetProperty(batch => batch.IsCancelled, batch => batch.IsCancelled || (failed && !batch.AllowFailures)), cancellationToken);
        if (await update == 0) return;

        var record = await db.Set<QueueBatchRecord>().AsNoTracking()
            .SingleOrDefaultAsync(batch => batch.Id == batchId, cancellationToken);
        if (record is not null && record.CompletedJobs + record.FailedJobs + record.CancelledJobs >= record.TotalJobs)
            await InvokeTerminalCallbacksAsync(record, cancellationToken);
    }

    private async Task InvokeTerminalCallbacksAsync(QueueBatchRecord record, CancellationToken cancellationToken)
    {
        var batch = ToBatch(record);
        if (record.FailedJobs == 0 && !record.IsCancelled && !record.ThenCallbacksCompleted)
        {
            await _callbacks.InvokeAsync(DeserializeCallbacks(record.ThenCallbacks), batch, cancellationToken);
            await SetCallbackCompletedAsync(record.Id, CallbackGroup.Then, cancellationToken);
        }
        if (record.FailedJobs > 0 && !record.CatchCallbacksCompleted)
        {
            await _callbacks.InvokeAsync(DeserializeCallbacks(record.CatchCallbacks), batch, cancellationToken);
            await SetCallbackCompletedAsync(record.Id, CallbackGroup.Catch, cancellationToken);
        }
        if (!record.FinallyCallbacksCompleted)
        {
            await _callbacks.InvokeAsync(DeserializeCallbacks(record.FinallyCallbacks), batch, cancellationToken);
            await SetCallbackCompletedAsync(record.Id, CallbackGroup.Finally, cancellationToken);
        }
    }

    private async Task SetCallbackCompletedAsync(string id, CallbackGroup group, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Set<QueueBatchRecord>().Where(batch => batch.Id == id);
        if (group == CallbackGroup.Then)
            await query.ExecuteUpdateAsync(update => update.SetProperty(batch => batch.ThenCallbacksCompleted, true), cancellationToken);
        else if (group == CallbackGroup.Catch)
            await query.ExecuteUpdateAsync(update => update.SetProperty(batch => batch.CatchCallbacksCompleted, true), cancellationToken);
        else
            await query.ExecuteUpdateAsync(update => update.SetProperty(batch => batch.FinallyCallbacksCompleted, true), cancellationToken);
    }

    private static List<BatchCallbackReference> DeserializeCallbacks(string callbacks)
        => QueueEnvelopeJson.DeserializeCallbacks(callbacks);

    private static QueueBatch ToBatch(QueueBatchRecord record) => new()
    {
        Id = record.Id,
        TotalJobs = record.TotalJobs,
        CompletedJobs = record.CompletedJobs,
        FailedJobs = record.FailedJobs,
        CancelledJobs = record.CancelledJobs,
        AllowFailures = record.AllowFailures,
        IsCancelled = record.IsCancelled
    };

    private static void EnsurePersistable(BatchOptions options)
    {
        if (options.OnCompleted is not null || options.OnJobFailed is not null)
            throw new InvalidOperationException("Persistent batch repositories require alias-registered Then, Catch, and Finally callbacks; delegates are not persisted.");
    }

    private enum CallbackGroup { Then, Catch, Finally }
}