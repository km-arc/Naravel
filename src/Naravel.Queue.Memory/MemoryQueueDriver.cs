using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;

namespace Naravel.Queue.Memory;

/// <summary>
/// Pure in-process queue driver - equivalent to Laravel's "sync"/array driver, but actually
/// asynchronous and queued (jobs run on the worker, not inline). Great for tests and local dev.
/// State does NOT survive process restarts and is NOT shared across processes/machines.
/// </summary>
public class MemoryQueueDriver : IQueueDriver
{
    // Keyed by "connectionName::queueName" so multiple connections configured with the memory
    // driver don't accidentally share state.
    private static readonly ConcurrentDictionary<string, List<QueuedMessage>> Queues = new();
    private static readonly ConcurrentDictionary<string, object> Locks = new();
    private static readonly ConcurrentDictionary<string, QueuedMessage> Failed = new();

    private readonly string _connectionName;

    public MemoryQueueDriver(string connectionName) => _connectionName = connectionName;

    private string Key(string queue) => $"{_connectionName}::{queue}";
    private List<QueuedMessage> GetQueue(string queue) => Queues.GetOrAdd(Key(queue), _ => new List<QueuedMessage>());
    private object GetLock(string queue) => Locks.GetOrAdd(Key(queue), _ => new object());

    public Task PushAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        lock (GetLock(message.Queue)) { GetQueue(message.Queue).Add(message); }
        return Task.CompletedTask;
    }

    public Task<QueuedMessage?> PopAsync(string queue, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        lock (GetLock(queue))
        {
            var candidate = GetQueue(queue)
                .Where(m => m.ReservedAt == null && m.AvailableAt <= now)
                .OrderByDescending(m => m.Priority)
                .ThenBy(m => m.AvailableAt)
                .FirstOrDefault();

            if (candidate != null) candidate.ReservedAt = now;
            return Task.FromResult(candidate);
        }
    }

    public Task AckAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        lock (GetLock(message.Queue)) { GetQueue(message.Queue).RemoveAll(m => m.Id == message.Id); }
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        lock (GetLock(message.Queue))
        {
            var existing = GetQueue(message.Queue).FirstOrDefault(m => m.Id == message.Id);
            if (existing == null) return Task.CompletedTask;
            existing.Attempts = message.Attempts;
            existing.Error = message.Error;
            existing.ReservedAt = null;
            existing.AvailableAt = DateTimeOffset.UtcNow + delay;
        }
        return Task.CompletedTask;
    }

    public Task FailAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        lock (GetLock(message.Queue)) { GetQueue(message.Queue).RemoveAll(m => m.Id == message.Id); }
        Failed[message.Id] = message;
        return Task.CompletedTask;
    }

    public Task<long> SizeAsync(string queue, CancellationToken cancellationToken = default)
    {
        lock (GetLock(queue)) { return Task.FromResult((long)GetQueue(queue).Count); }
    }
}

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-process "memory" driver for every configured store whose "Driver" is "memory".
    /// No settings are read beyond the store's own name (used to keep each store's state separate).
    /// </summary>
    public static IServiceCollection AddMemoryDriver(this IServiceCollection services, IConfiguration configuration, string sectionName = "NaravelQueue")
        => services.AddQueueDriver(configuration, "memory", (_, store) => new MemoryQueueDriver(store.Key), sectionName);
}
