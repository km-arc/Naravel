using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Naravel.Queue.Batching;
using Naravel.Queue.Drivers;
using StackExchange.Redis;

namespace Naravel.Queue.Redis;

/// <summary>Redis-backed persistent batch state and typed callback registrations.</summary>
/// <remarks><b>Laravel equivalent:</b> queue batch repository. It exists for cross-process state and stores callback aliases rather than delegates or CLR type names.</remarks>
public sealed class RedisBatchRepository : IBatchRepository
{
    private readonly IDatabase _database;
    private readonly RedisKey _prefix;
    private readonly RedisKey _index;
    private readonly BatchCallbackRegistry _callbacks;

    public RedisBatchRepository(IConnectionMultiplexer multiplexer, BatchCallbackRegistry callbacks, string keyPrefix = "netqueue:batches")
    {
        _database = multiplexer.GetDatabase();
        _prefix = keyPrefix;
        _index = $"{keyPrefix}:index";
        _callbacks = callbacks;
    }

    private RedisKey BatchKey(string id) => $"{_prefix}:{id}";

    private const string IncrementScript = """
        local total = tonumber(redis.call('HGET', KEYS[1], 'TotalJobs'))
        if not total then return 0 end
        local finished = tonumber(redis.call('HGET', KEYS[1], 'CompletedJobs'))
            + tonumber(redis.call('HGET', KEYS[1], 'FailedJobs'))
            + tonumber(redis.call('HGET', KEYS[1], 'CancelledJobs'))
        if finished >= total then return 0 end
        redis.call('HINCRBY', KEYS[1], ARGV[1], 1)
        if ARGV[1] == 'FailedJobs' and redis.call('HGET', KEYS[1], 'AllowFailures') ~= '1' then
            redis.call('HSET', KEYS[1], 'IsCancelled', '1')
        end
        return 1
        """;

    public async Task RegisterAsync(QueueBatch batch, BatchOptions options, CancellationToken cancellationToken)
    {
        EnsurePersistable(options);
        cancellationToken.ThrowIfCancellationRequested();
        batch.AllowFailures = options.AllowFailures;
        var key = BatchKey(batch.Id);
        await _database.HashSetAsync(key,
        [
            new HashEntry("TotalJobs", batch.TotalJobs),
            new HashEntry("CompletedJobs", 0),
            new HashEntry("FailedJobs", 0),
            new HashEntry("CancelledJobs", 0),
            new HashEntry("AllowFailures", options.AllowFailures ? "1" : "0"),
            new HashEntry("IsCancelled", "0"),
            new HashEntry("ThenCallbacks", JsonSerializer.Serialize(options.ThenCallbacks)),
            new HashEntry("CatchCallbacks", JsonSerializer.Serialize(options.CatchCallbacks)),
            new HashEntry("FinallyCallbacks", JsonSerializer.Serialize(options.FinallyCallbacks)),
            new HashEntry("ThenCallbacksCompleted", "0"),
            new HashEntry("CatchCallbacksCompleted", "0"),
            new HashEntry("FinallyCallbacksCompleted", "0")
        ]);
        await _database.SetAddAsync(_index, batch.Id);
    }

    public async Task<QueueBatch?> GetAsync(string batchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = await _database.HashGetAllAsync(BatchKey(batchId));
        if (entries.Length == 0) return null;
        var values = entries.ToDictionary(entry => (string)entry.Name!, entry => entry.Value);
        return new QueueBatch
        {
            Id = batchId,
            TotalJobs = ReadInt(values, "TotalJobs"),
            CompletedJobs = ReadInt(values, "CompletedJobs"),
            FailedJobs = ReadInt(values, "FailedJobs"),
            CancelledJobs = ReadInt(values, "CancelledJobs"),
            AllowFailures = ReadBool(values, "AllowFailures"),
            IsCancelled = ReadBool(values, "IsCancelled")
        };
    }

    public async Task<bool> IsCancelledAsync(string batchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await _database.HashGetAsync(BatchKey(batchId), "IsCancelled");
        return value == "1";
    }

    public async Task CancelAsync(string batchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _database.HashSetAsync(BatchKey(batchId), "IsCancelled", "1");
    }

    public Task MarkJobCompletedAsync(string batchId, CancellationToken cancellationToken)
        => IncrementAsync(batchId, "CompletedJobs", cancellationToken);

    public Task MarkJobFailedAsync(string batchId, Exception exception, CancellationToken cancellationToken)
        => IncrementAsync(batchId, "FailedJobs", cancellationToken);

    public Task MarkJobCancelledAsync(string batchId, CancellationToken cancellationToken)
        => IncrementAsync(batchId, "CancelledJobs", cancellationToken);

    public async Task ResumePendingCallbacksAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var batchIds = await _database.SetMembersAsync(_index);
        foreach (var batchId in batchIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = await _database.HashGetAllAsync(BatchKey((string)batchId!));
            if (entries.Length == 0) continue;
            var values = entries.ToDictionary(entry => (string)entry.Name!, entry => entry.Value);
            var finished = ReadInt(values, "CompletedJobs") + ReadInt(values, "FailedJobs") + ReadInt(values, "CancelledJobs") >= ReadInt(values, "TotalJobs");
            if (finished) await InvokeTerminalCallbacksAsync((string)batchId!, values, cancellationToken);
        }
    }

    private async Task IncrementAsync(string batchId, string field, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _database.ScriptEvaluateAsync(IncrementScript, [BatchKey(batchId)], [field]);
        if ((int)result != 1) return;
        var entries = await _database.HashGetAllAsync(BatchKey(batchId));
        if (entries.Length == 0) return;
        var values = entries.ToDictionary(entry => (string)entry.Name!, entry => entry.Value);
        var finished = ReadInt(values, "CompletedJobs") + ReadInt(values, "FailedJobs") + ReadInt(values, "CancelledJobs") >= ReadInt(values, "TotalJobs");
        if (finished) await InvokeTerminalCallbacksAsync(batchId, values, cancellationToken);
    }

    private async Task InvokeTerminalCallbacksAsync(string id, Dictionary<string, RedisValue> values, CancellationToken cancellationToken)
    {
        var batch = new QueueBatch
        {
            Id = id,
            TotalJobs = ReadInt(values, "TotalJobs"),
            CompletedJobs = ReadInt(values, "CompletedJobs"),
            FailedJobs = ReadInt(values, "FailedJobs"),
            CancelledJobs = ReadInt(values, "CancelledJobs"),
            AllowFailures = ReadBool(values, "AllowFailures"),
            IsCancelled = ReadBool(values, "IsCancelled")
        };

        if (batch.FailedJobs == 0 && !batch.IsCancelled && !ReadBool(values, "ThenCallbacksCompleted"))
        {
            await InvokeGroupAsync(id, values, "ThenCallbacks", "ThenCallbacksCompleted", batch, cancellationToken);
            values["ThenCallbacksCompleted"] = "1";
        }
        if (batch.FailedJobs > 0 && !ReadBool(values, "CatchCallbacksCompleted"))
        {
            await InvokeGroupAsync(id, values, "CatchCallbacks", "CatchCallbacksCompleted", batch, cancellationToken);
            values["CatchCallbacksCompleted"] = "1";
        }
        if (!ReadBool(values, "FinallyCallbacksCompleted"))
            await InvokeGroupAsync(id, values, "FinallyCallbacks", "FinallyCallbacksCompleted", batch, cancellationToken);
    }

    private async Task InvokeGroupAsync(
        string id,
        Dictionary<string, RedisValue> values,
        string callbacksField,
        string completedField,
        QueueBatch batch,
        CancellationToken cancellationToken)
    {
        var callbacks = JsonSerializer.Deserialize<List<BatchCallbackReference>>((string)values[callbacksField]!) ?? new();
        await _callbacks.InvokeAsync(callbacks, batch, cancellationToken);
        await _database.HashSetAsync(BatchKey(id), completedField, "1");
    }

    private static int ReadInt(IReadOnlyDictionary<string, RedisValue> values, string field)
        => int.Parse((string)values[field]!);

    private static bool ReadBool(IReadOnlyDictionary<string, RedisValue> values, string field)
        => values.TryGetValue(field, out var value) && value == "1";

    private static void EnsurePersistable(BatchOptions options)
    {
        if (options.OnCompleted is not null || options.OnJobFailed is not null)
            throw new InvalidOperationException("Persistent batch repositories require alias-registered Then, Catch, and Finally callbacks; delegates are not persisted.");
    }
}

public static class RedisBatchRepositoryServiceCollectionExtensions
{
    /// <summary>Uses the selected Redis queue connection for persistent batch state.</summary>
    public static IServiceCollection AddRedisBatchRepository(
        this IServiceCollection services,
        string? connectionName = null,
        string keyPrefix = "netqueue:batches")
    {
        services.Replace(ServiceDescriptor.Singleton<IBatchRepository>(sp =>
        {
            var driver = sp.GetRequiredService<QueueManager>().Connection(connectionName) as RedisQueueDriver
                ?? throw new InvalidOperationException("The selected queue connection does not use the Redis driver.");
            return new RedisBatchRepository(driver.Multiplexer, sp.GetRequiredService<BatchCallbackRegistry>(), keyPrefix);
        }));
        return services;
    }
}