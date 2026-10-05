using System.Collections.Concurrent;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;

namespace Naravel.Queue.Kafka;

/// <summary>
/// Kafka-backed driver. Kafka is a log, not a queue, so semantics are adapted rather than 1:1:
///   - Each queue "q" maps to topic "netqueue-q" (immediate + retried jobs) and "netqueue-q-failed" (dead letters).
///   - PopAsync commits nothing until Ack/Release/Fail is called for that message (manual offset commit).
///   - A retried job (ReleaseAsync) is re-produced to the tail of the same topic and the original offset
///     is committed - i.e. retries go to the back of the queue rather than being retried in place. This is
///     the standard pattern for retries on Kafka and keeps consumption strictly sequential per partition.
///   - A delayed job (AvailableAt in the future) is left uncommitted and the consumer seeks back to it,
///     which means a long delay will block that partition's head-of-line until it is due. If you need many
///     independently-delayed jobs mixed with immediate ones at scale, dedicate a separate low-traffic queue
///     (topic) for delayed jobs.
///   - One consumer is shared per queue. Polling, seeking, and offset commits are serialized; worker concurrency
///     can execute jobs in parallel, but acknowledgements only commit past the oldest unacknowledged record.
///   - PopAsync drains up to 256 currently available records and returns the highest-priority job in that batch;
///     messages published after the poll are not reordered ahead of already returned jobs.
///   - SizeAsync is not cheaply available from the consumer API and returns -1; use Kafka's own consumer-lag
///     tooling (e.g. kafka-consumer-groups.sh) to monitor backlog.
/// </summary>
public class KafkaQueueDriver : IQueueDriver, IDisposable
{
    private readonly string _bootstrapServers;
    private readonly string _groupId;
    private readonly IProducer<string, string> _producer;
    private readonly ConcurrentDictionary<string, KafkaConsumerState> _consumers = new();
    private readonly ConcurrentDictionary<string, TopicPartitionOffset> _pendingOffsets = new();

    private sealed class KafkaConsumerState(IConsumer<string, string> consumer)
    {
        public IConsumer<string, string> Consumer { get; } = consumer;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Dictionary<TopicPartition, KafkaOffsetCommitTracker> OffsetTrackers { get; } = new();
        public List<BufferedKafkaMessage> BufferedMessages { get; } = new();
        public long NextSequence { get; set; }
    }

    private sealed record BufferedKafkaMessage(QueuedMessage Message, TopicPartitionOffset Offset, long Sequence);

    public KafkaQueueDriver(string bootstrapServers, string groupId)
    {
        _bootstrapServers = bootstrapServers;
        _groupId = groupId;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();
    }

    private static string TopicFor(string queue) => $"netqueue-{queue}";
    private static string FailedTopicFor(string queue) => $"netqueue-{queue}-failed";

    private KafkaConsumerState GetConsumer(string queue) => _consumers.GetOrAdd(queue, q =>
    {
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = _groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnablePartitionEof = false
        }).Build();
        consumer.Subscribe(TopicFor(q));
        return new KafkaConsumerState(consumer);
    });

    public async Task PushAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        await _producer.ProduceAsync(TopicFor(message.Queue),
            new Message<string, string> { Key = message.Id, Value = JsonSerializer.Serialize(message) },
            cancellationToken);
    }

    public async Task<QueuedMessage?> PopAsync(string queue, CancellationToken cancellationToken = default)
    {
        var state = GetConsumer(queue);
        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            if (state.BufferedMessages.Count == 0)
            {
                ConsumeResult<string, string>? result;
                try { result = state.Consumer.Consume(TimeSpan.FromMilliseconds(200)); }
                catch (ConsumeException) { return null; }
                if (result is null || result.IsPartitionEOF) return null;

                if (!BufferIfReady(state, result))
                {
                    state.Consumer.Seek(result.TopicPartitionOffset);
                    return null;
                }

                for (var index = 1; index < 256; index++)
                {
                    try { result = state.Consumer.Consume(TimeSpan.Zero); }
                    catch (ConsumeException) { break; }
                    if (result is null || result.IsPartitionEOF) break;
                    if (!BufferIfReady(state, result))
                    {
                        state.Consumer.Seek(result.TopicPartitionOffset);
                        break;
                    }
                }
            }

            var selected = state.BufferedMessages
                .OrderByDescending(item => item.Message.Priority)
                .ThenBy(item => item.Sequence)
                .First();
            state.BufferedMessages.Remove(selected);
            selected.Message.ReservedAt = DateTimeOffset.UtcNow;
            return selected.Message;
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private bool BufferIfReady(KafkaConsumerState state, ConsumeResult<string, string> result)
    {
        var message = JsonSerializer.Deserialize<QueuedMessage>(result.Message.Value)!;
        if (message.AvailableAt > DateTimeOffset.UtcNow) return false;

        if (!state.OffsetTrackers.TryGetValue(result.TopicPartition, out var tracker))
        {
            tracker = new KafkaOffsetCommitTracker();
            state.OffsetTrackers.Add(result.TopicPartition, tracker);
        }
        tracker.RecordDelivered(result.Offset.Value);
        _pendingOffsets[message.Id] = result.TopicPartitionOffset;
        state.BufferedMessages.Add(new BufferedKafkaMessage(message, result.TopicPartitionOffset, state.NextSequence++));
        return true;
    }

    public Task AckAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        return CommitOriginalOffsetAsync(message, cancellationToken);
    }

    public async Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        message.ReservedAt = null;
        message.AvailableAt = DateTimeOffset.UtcNow + delay;
        await _producer.ProduceAsync(TopicFor(message.Queue),
            new Message<string, string> { Key = message.Id, Value = JsonSerializer.Serialize(message) },
            cancellationToken);
        await CommitOriginalOffsetAsync(message, cancellationToken);
    }

    public async Task FailAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        await _producer.ProduceAsync(FailedTopicFor(message.Queue),
            new Message<string, string> { Key = message.Id, Value = JsonSerializer.Serialize(message) },
            cancellationToken);
        await CommitOriginalOffsetAsync(message, cancellationToken);
    }

    private async Task CommitOriginalOffsetAsync(QueuedMessage message, CancellationToken cancellationToken)
    {
        var state = GetConsumer(message.Queue);
        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!_pendingOffsets.TryRemove(message.Id, out var offset)) return;
            var tracker = state.OffsetTrackers[offset.TopicPartition];
            var nextOffset = tracker.MarkAcknowledged(offset.Offset.Value);
            if (nextOffset is null) return;

            state.Consumer.Commit(new[]
            {
                new TopicPartitionOffset(offset.Topic, offset.Partition, new Offset(nextOffset.Value))
            });
            tracker.MarkCommitted(nextOffset.Value);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    public Task<long> SizeAsync(string queue, CancellationToken cancellationToken = default)
        => Task.FromResult(-1L); // not cheaply available; use Kafka consumer-lag tooling instead

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
        foreach (var state in _consumers.Values)
        {
            state.Gate.Wait();
            try
            {
                try { state.Consumer.Close(); } catch { /* best effort */ }
                state.Consumer.Dispose();
            }
            finally
            {
                state.Gate.Release();
                state.Gate.Dispose();
            }
        }
    }
}

internal sealed class KafkaOffsetCommitTracker
{
    private readonly SortedDictionary<long, bool> _delivered = new();

    public void RecordDelivered(long offset) => _delivered.TryAdd(offset, false);

    public long? MarkAcknowledged(long offset)
    {
        if (!_delivered.ContainsKey(offset)) return null;
        _delivered[offset] = true;

        long? nextOffset = null;
        foreach (var (deliveredOffset, acknowledged) in _delivered)
        {
            if (!acknowledged) break;
            nextOffset = deliveredOffset + 1;
        }

        return nextOffset;
    }

    public void MarkCommitted(long nextOffset)
    {
        foreach (var offset in _delivered.Keys.TakeWhile(offset => offset < nextOffset).ToArray())
        {
            _delivered.Remove(offset);
        }
    }
}

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the "kafka" driver for every configured store whose "Driver" is "kafka".
    /// Reads that store's own "BootstrapServers" (required) and "GroupId" (default "netqueue-workers").
    /// See the driver's class-level docs for Kafka-specific semantics.
    /// </summary>
    public static IServiceCollection AddKafkaDriver(this IServiceCollection services, IConfiguration configuration, string sectionName = "NaravelQueue")
        => services.AddQueueDriver(configuration, "kafka", (_, store) =>
        {
            var bootstrapServers = store.GetRequired("BootstrapServers");
            var groupId = store.GetOrDefault("GroupId", "netqueue-workers");
            return new KafkaQueueDriver(bootstrapServers, groupId);
        }, sectionName);
}
