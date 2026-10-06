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
    Task RecordAsync(QueuedMessage message, Exception exception, CancellationToken cancellationToken);
    Task<IReadOnlyList<QueuedMessage>> ListAsync(CancellationToken cancellationToken);
    Task<bool> ForgetAsync(string id, CancellationToken cancellationToken);
    Task<int> FlushAsync(CancellationToken cancellationToken);
}

/// <summary>Default in-process implementation. Swap for a persistent one (DB/Redis) in production if you need
/// the failed-job list to survive restarts - the driver-level failed queues/tables already do, this is just a convenience mirror.</summary>
public class InMemoryFailedJobStore : IFailedJobStore
{
    private readonly ConcurrentDictionary<string, QueuedMessage> _failed = new();

    public Task RecordAsync(QueuedMessage message, Exception exception, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        message.Error = exception.ToString();
        _failed[message.Id] = Clone(message);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QueuedMessage>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<QueuedMessage>>(_failed.Values.Select(Clone).ToList());
    }

    public Task<bool> ForgetAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_failed.TryRemove(id, out _));
    }

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
