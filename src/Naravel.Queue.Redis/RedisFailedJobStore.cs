using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Naravel.Queue.Drivers;
using Naravel.Queue.Failed;
using StackExchange.Redis;

namespace Naravel.Queue.Redis;

/// <summary>Persistent failed-job storage in a Redis hash.</summary>
/// <remarks><b>Laravel equivalent:</b> failed-jobs provider. It provides restart-safe retry records without resolving types from stored payloads.</remarks>
public sealed class RedisFailedJobStore : IFailedJobStore
{
    private readonly IDatabase _database;
    private readonly RedisKey _key;

    public RedisFailedJobStore(IConnectionMultiplexer multiplexer, string key = "netqueue:failed-jobs")
    {
        _database = multiplexer.GetDatabase();
        _key = key;
    }

    public async Task RecordAsync(QueuedMessage message, Exception exception, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        message.Error = exception.ToString();
        await _database.HashSetAsync(_key, message.Id, JsonSerializer.Serialize(message));
    }

    public async Task<IReadOnlyList<QueuedMessage>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var records = await _database.HashGetAllAsync(_key);
        return records.Select(record => JsonSerializer.Deserialize<QueuedMessage>((string)record.Value!)!).ToList();
    }

    public async Task<bool> ForgetAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _database.HashDeleteAsync(_key, id);
    }

    public async Task<int> FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = await _database.HashLengthAsync(_key);
        await _database.KeyDeleteAsync(_key);
        return checked((int)count);
    }
}

public static class FailedJobStoreServiceCollectionExtensions
{
    /// <summary>Uses the selected Redis queue connection for persistent failed-job records.</summary>
    public static IServiceCollection AddRedisFailedJobStore(
        this IServiceCollection services,
        string? connectionName = null,
        string key = "netqueue:failed-jobs")
    {
        services.Replace(ServiceDescriptor.Singleton<IFailedJobStore>(sp =>
        {
            var driver = sp.GetRequiredService<QueueManager>().Connection(connectionName) as RedisQueueDriver
                ?? throw new InvalidOperationException("The selected queue connection does not use the Redis driver.");
            return new RedisFailedJobStore(driver.Multiplexer, key);
        }));
        return services;
    }
}