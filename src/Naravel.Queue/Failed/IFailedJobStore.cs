using System.Collections.Concurrent;
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
}

/// <summary>Default in-process implementation. Swap for a persistent one (DB/Redis) in production if you need
/// the failed-job list to survive restarts - the driver-level failed queues/tables already do, this is just a convenience mirror.</summary>
public class InMemoryFailedJobStore : IFailedJobStore
{
    private readonly ConcurrentDictionary<string, QueuedMessage> _failed = new();

    public Task RecordAsync(QueuedMessage message, Exception exception, CancellationToken cancellationToken)
    {
        message.Error = exception.ToString();
        _failed[message.Id] = message;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QueuedMessage>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<QueuedMessage>>(_failed.Values.ToList());

    public Task<bool> ForgetAsync(string id, CancellationToken cancellationToken)
        => Task.FromResult(_failed.TryRemove(id, out _));
}
