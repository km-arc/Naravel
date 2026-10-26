using System.Collections.Concurrent;
using System.Text.Json;
using Naravel.Queue.Drivers;

namespace Naravel.Queue.Failed;

/// <summary>
/// Optional secondary record of failed jobs for inspection/retry tooling, independent of whatever
/// each driver itself does with failed messages (most drivers also keep their own failed store/queue).
/// Equivalent to Laravel's failed_jobs table.
/// </summary>
public interface IFailedJobStore
{
    /// <summary>Records a failed message together with the exception that caused the failure.</summary>
    Task RecordAsync(QueuedMessage message, Exception exception, CancellationToken cancellationToken);
    /// <summary>Lists all recorded failed messages.</summary>
    Task<IReadOnlyList<QueuedMessage>> ListAsync(CancellationToken cancellationToken);
    /// <summary>Removes the failed message with the given id and returns whether it existed.</summary>
    Task<bool> ForgetAsync(string id, CancellationToken cancellationToken);
    /// <summary>Removes all recorded failed messages and returns how many were removed.</summary>
    Task<int> FlushAsync(CancellationToken cancellationToken);
}

/// <summary>Default in-process implementation. Swap for a persistent one (DB/Redis) in production if you need
/// the failed-job list to survive restarts - the driver-level failed queues/tables already do, this is just a convenience mirror.</summary>
public class InMemoryFailedJobStore : IFailedJobStore
{
    private readonly ConcurrentDictionary<string, QueuedMessage> _failed = new();

    /// <inheritdoc />
    public Task RecordAsync(QueuedMessage message, Exception exception, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        message.Error = exception.ToString();
        _failed[message.Id] = Clone(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<QueuedMessage>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<QueuedMessage>>(_failed.Values.Select(Clone).ToList());
    }

    /// <inheritdoc />
    public Task<bool> ForgetAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_failed.TryRemove(id, out _));
    }

    /// <inheritdoc />
    public Task<int> FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = _failed.Count;
        _failed.Clear();
        return Task.FromResult(count);
    }

    private static QueuedMessage Clone(QueuedMessage message)
        => JsonSerializer.Deserialize<QueuedMessage>(JsonSerializer.Serialize(message))!;
}
