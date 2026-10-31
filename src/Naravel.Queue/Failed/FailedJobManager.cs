using Naravel.Queue.Diagnostics;
using Naravel.Queue.Drivers;
using Naravel.Queue.Serialization;

namespace Naravel.Queue.Failed;

/// <summary>
/// Inspects and retries failed queue messages.
/// </summary>
/// <remarks>
/// <b>Laravel equivalent:</b> failed-job provider retry/forget commands. This .NET service uses the
/// registered queue manager and explicit job aliases; it does not resolve types from stored data.
/// It exists to expose durable recovery operations without porting Artisan commands or a global facade.
/// </remarks>
public sealed class FailedJobManager
{
    private readonly IFailedJobStore _store;
    private readonly QueueManager _queues;

    /// <summary>Initializes a new instance of the <see cref="FailedJobManager"/> class.</summary>
    public FailedJobManager(IFailedJobStore store, QueueManager queues)
    {
        _store = store;
        _queues = queues;
    }

    /// <summary>Lists the recorded failed messages.</summary>
    public Task<IReadOnlyList<QueuedMessage>> ListAsync(CancellationToken cancellationToken = default)
        => _store.ListAsync(cancellationToken);

    /// <summary>Requeues the failed message with the given id and returns <see langword="false"/> if it is not found.</summary>
    public async Task<bool> RetryAsync(string id, CancellationToken cancellationToken = default)
    {
        var message = (await _store.ListAsync(cancellationToken)).FirstOrDefault(job => job.Id == id);
        if (message is null) return false;

        await RequeueAsync(message, cancellationToken);
        return true;
    }

    /// <summary>Requeues every recorded failed message and reports how many succeeded and which ids failed.</summary>
    public async Task<FailedJobRetryResult> RetryAllAsync(CancellationToken cancellationToken = default)
    {
        var messages = await _store.ListAsync(cancellationToken);
        var failedIds = new List<string>();
        var retriedCount = 0;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await RequeueAsync(message, cancellationToken);
                retriedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failedIds.Add(message.Id);
            }
        }

        return new FailedJobRetryResult(retriedCount, failedIds);
    }

    /// <summary>Removes the failed message with the given id and returns whether it existed.</summary>
    public Task<bool> ForgetAsync(string id, CancellationToken cancellationToken = default)
        => _store.ForgetAsync(id, cancellationToken);

    /// <summary>Removes all recorded failed messages and returns how many were removed.</summary>
    public Task<int> FlushAsync(CancellationToken cancellationToken = default)
        => _store.FlushAsync(cancellationToken);

    private async Task RequeueAsync(QueuedMessage failedMessage, CancellationToken cancellationToken)
    {
        var message = QueueEnvelopeJson.Clone(failedMessage);
        message.Id = Guid.NewGuid().ToString("N");
        message.Attempts = 0;
        message.Error = null;
        message.ReservedAt = null;
        message.ReservationId = null;
        message.AvailableAt = DateTimeOffset.UtcNow;

        await _queues.Connection(message.Connection).PushAsync(message, cancellationToken);
        await _store.ForgetAsync(failedMessage.Id, cancellationToken);
        QueueTelemetry.Retried.Add(1);
    }
}

/// <summary>Summary of a retry-all operation. Failed records remain available for later retry.</summary>
/// <param name="RetriedCount">The number of failed messages that were requeued.</param>
/// <param name="FailedIds">The ids of the failed messages that could not be requeued.</param>
/// <remarks>Unlike Laravel's CLI output, this typed .NET result lets applications inspect partial success directly.</remarks>
public sealed record FailedJobRetryResult(int RetriedCount, IReadOnlyList<string> FailedIds);