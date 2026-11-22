namespace Naravel.Queue.Batching;

/// <summary>
/// An optional capability of an <see cref="IBatchRepository"/> that keeps batches beyond the process (the Redis and the database repository):
/// it can delete the records of batches that are finished, which the application decides when to do (AD-019).
/// </summary>
/// <remarks>
/// <para>A persistent repository keeps every batch it has ever registered, and a worker that starts re-reads all of them to find callbacks that
/// were not delivered, so the history grows for ever and each worker start costs more. Nothing deletes it on its own, because how long a finished
/// batch must stay (for an audit, for support) is the application's decision, not the library's.</para>
/// <para>Get it from the registered repository: <c>provider.GetRequiredService&lt;IBatchRepository&gt;() is IPrunableBatchRepository prunable</c>.
/// <see cref="InMemoryBatchRepository"/> does not implement it.</para>
/// </remarks>
public interface IPrunableBatchRepository : IBatchRepository
{
    /// <summary>
    /// Deletes every batch that is finished and whose callbacks have all been delivered, and returns how many were deleted.
    /// </summary>
    /// <remarks>
    /// A batch is deleted only when all its jobs are counted (completed, failed or cancelled) <b>and</b> every callback group that applies to it has run:
    /// the finally callbacks, and either the catch callbacks (a job failed) or the then callbacks (none failed and the batch was not cancelled).
    /// A batch that is still running, or that is finished but whose callbacks have not all been delivered, is never deleted, so
    /// <see cref="IBatchRepository.ResumePendingCallbacksAsync"/> still finds it after a restart. The repository stores no finish time, so there is no
    /// minimum age: call this on the schedule that suits the history you want to keep.
    /// </remarks>
    /// <param name="cancellationToken">Checked before each request is sent; a request that was already sent is awaited (see the queue guide).</param>
    Task<int> PruneFinishedAsync(CancellationToken cancellationToken);
}
