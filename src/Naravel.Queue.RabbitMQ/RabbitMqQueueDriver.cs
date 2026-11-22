using System.Text;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using Naravel.Queue.Serialization;
using RabbitMQ.Client;

namespace Naravel.Queue.RabbitMQ;

/// <summary>The type of queue the driver declares for each ready queue; fixed by RabbitMQ when the queue is first declared.</summary>
public enum RabbitMqQueueType
{
    /// <summary>A classic queue with message priorities (the default; nothing changes for existing queues).</summary>
    Classic,

    /// <summary>A quorum queue: replicated, with an optional broker-side delivery limit, and no priorities.</summary>
    Quorum
}

/// <summary>
/// RabbitMQ-backed driver. Topology per queue name "q":
///   exchange "netqueue.direct" (direct)         - routes by queue name
///   queue    "netqueue.ready.q"    bound to the exchange with routing key "q"   - consumed by workers
///   queue    "netqueue.delay.q"    no consumers, dead-letters back to the exchange on TTL expiry - used for delayed/retried jobs
///   queue    "netqueue.failed.q"                                                - permanently failed jobs
///
/// Delivery uses manual ack via BasicGetAsync/BasicAckAsync (pull-based, matching IQueueDriver's PopAsync contract).
/// Delay is implemented with per-message TTL + dead-lettering, which does not require the RabbitMQ
/// delayed-message-exchange plugin. Caveat: because RabbitMQ only expires messages once they reach
/// the head of the queue, a very-long-delay message queued before a short-delay one can make the
/// short one wait slightly longer than requested - fine for typical retry/delay use cases.
///
/// Attempts: a worker-initiated retry publishes the message again with the updated attempt count, waits for the
/// broker to confirm it, and only then acknowledges the original delivery; a failed job is moved to
/// "netqueue.failed.q" in the same order. If the process stops between the two steps the broker redelivers the
/// original, so a job can run twice but is not lost. Crash redelivery (unacknowledged deliveries that the broker requeues when the channel or
/// connection closes) is at-least-once and is NOT counted by the driver. By default the ready queue is a classic queue.
/// With <see cref="RabbitMqQueueType.Quorum"/> it is a quorum queue, and an optional delivery limit lets the broker
/// dead-letter a message that keeps being redelivered into "netqueue.failed.q" (see docs/en/queue.md,
/// "Attempt accounting by driver").
/// </summary>
public class RabbitMqQueueDriver : IQueueDriver, IAsyncDisposable
{
    private sealed record Delivery(IChannel Channel, ulong Tag);

    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Delivery> _deliveries = new();
    private readonly RabbitMqQueueType _queueType;
    private readonly int? _deliveryLimit;
    private IConnection? _connection;

    /// <summary>Creates a driver that uses classic ready queues.</summary>
    public RabbitMqQueueDriver(string hostName, int port, string? userName, string? password, string? virtualHost)
        : this(hostName, port, userName, password, virtualHost, RabbitMqQueueType.Classic, null)
    {
    }

    /// <summary>Creates the driver with an explicit ready-queue type. The delivery limit applies to quorum queues only.</summary>
    public RabbitMqQueueDriver(string hostName, int port, string? userName, string? password, string? virtualHost, RabbitMqQueueType queueType, int? deliveryLimit)
    {
        if (deliveryLimit is < 1) throw new ArgumentOutOfRangeException(nameof(deliveryLimit), "The delivery limit must be at least 1.");
        if (deliveryLimit is not null && queueType != RabbitMqQueueType.Quorum)
            throw new ArgumentException("A delivery limit needs the quorum queue type.", nameof(deliveryLimit));
        _queueType = queueType;
        _deliveryLimit = deliveryLimit;
        _factory = new ConnectionFactory
        {
            HostName = hostName,
            Port = port,
            ClientProvidedName = "netqueue"
        };
        if (!string.IsNullOrEmpty(userName)) _factory.UserName = userName;
        if (!string.IsNullOrEmpty(password)) _factory.Password = password;
        if (!string.IsNullOrEmpty(virtualHost)) _factory.VirtualHost = virtualHost;
    }

    private async ValueTask<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } connection) return connection;

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not { IsOpen: true })
                _connection = await _factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally { _connectionGate.Release(); }
    }

    /// <summary>
    /// Opens a channel. With <paramref name="confirmed"/> the channel has publisher confirmations enabled and tracked, so
    /// <c>BasicPublishAsync</c> completes only when the broker has accepted the message and throws if it nacks or returns it.
    /// </summary>
    private async ValueTask<IChannel> OpenChannelAsync(CancellationToken cancellationToken, bool confirmed = false)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return confirmed
            ? await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                cancellationToken)
            : await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Test seam: runs before a retry or failed copy is published. A test that throws here proves the original delivery
    /// is still unacknowledged, so the broker redelivers it. Never set in production.
    /// </summary>
    internal Func<CancellationToken, Task>? BeforeReplacementPublished { get; set; }

    /// <summary>
    /// Test seam: runs after the broker confirmed a retry or failed copy and before the original delivery is acknowledged.
    /// A test that throws here stands for a process that stops in that window. Never set in production.
    /// </summary>
    internal Func<CancellationToken, Task>? AfterReplacementConfirmed { get; set; }

    /// <summary>The arguments the ready queue is declared with. The queue type is fixed at declaration time.</summary>
    internal static Dictionary<string, object?> ReadyQueueArguments(string queue, RabbitMqQueueType queueType, int? deliveryLimit)
    {
        if (queueType == RabbitMqQueueType.Classic) return new Dictionary<string, object?> { ["x-max-priority"] = 9 };

        // Quorum queues ignore x-max-priority. A message that exceeds the delivery limit is dead-lettered
        // (default exchange, routing key = the failed queue) so it is kept instead of being dropped.
        var arguments = new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-dead-letter-exchange"] = "",
            ["x-dead-letter-routing-key"] = $"netqueue.failed.{queue}"
        };
        if (deliveryLimit is { } limit) arguments["x-delivery-limit"] = limit;
        return arguments;
    }

    private async Task EnsureTopologyAsync(IChannel channel, string queue, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync("netqueue.direct", ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync($"netqueue.ready.{queue}", durable: true, exclusive: false, autoDelete: false,
            arguments: ReadyQueueArguments(queue, _queueType, _deliveryLimit), cancellationToken: cancellationToken);
        await channel.QueueBindAsync($"netqueue.ready.{queue}", "netqueue.direct", queue, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync($"netqueue.delay.{queue}", durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = "netqueue.direct",
                ["x-dead-letter-routing-key"] = queue
            }, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync($"netqueue.failed.{queue}", durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task PushAsync(QueuedMessage message, CancellationToken cancellationToken = default)
        => PublishAsync(message, confirmed: false, cancellationToken);

    private async Task PublishAsync(QueuedMessage message, bool confirmed, CancellationToken cancellationToken)
    {
        await using var channel = await OpenChannelAsync(cancellationToken, confirmed);
        await EnsureTopologyAsync(channel, message.Queue, cancellationToken);
        var body = Encoding.UTF8.GetBytes(QueueEnvelopeJson.Serialize(message));
        var properties = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.Id,
            Priority = (byte)Math.Clamp(message.Priority, 0, 9)
        };

        var delay = message.AvailableAt - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero)
            properties.Expiration = ((long)delay.TotalMilliseconds).ToString();

        await channel.BasicPublishAsync(
            exchange: delay > TimeSpan.Zero ? "" : "netqueue.direct",
            routingKey: delay > TimeSpan.Zero ? $"netqueue.delay.{message.Queue}" : message.Queue,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<QueuedMessage?> PopAsync(string queue, CancellationToken cancellationToken = default)
    {
        var channel = await OpenChannelAsync(cancellationToken);
        try
        {
            await EnsureTopologyAsync(channel, queue, cancellationToken);
            var result = await channel.BasicGetAsync($"netqueue.ready.{queue}", autoAck: false, cancellationToken);
            if (result is null)
            {
                await channel.DisposeAsync();
                return null;
            }

            var message = QueueEnvelopeJson.Deserialize(Encoding.UTF8.GetString(result.Body.Span))!;
            message.ReservedAt = DateTimeOffset.UtcNow;
            _deliveries[message.Id] = new Delivery(channel, result.DeliveryTag);
            return message;
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task AckAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_deliveries.TryRemove(message.Id, out var delivery)) return;
        await AcknowledgeAsync(delivery, cancellationToken);
    }

    /// <summary>Acknowledges a delivery on the channel that fetched it, then closes that channel.</summary>
    private static async Task AcknowledgeAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        try { await delivery.Channel.BasicAckAsync(delivery.Tag, multiple: false, cancellationToken); }
        finally { await delivery.Channel.DisposeAsync(); }
    }

    /// <summary>
    /// Publishes the replacement of a delivery (a retry copy or a failed copy) and only then acknowledges the original.
    /// The replacement keeps the message id, so the original delivery is taken out of <c>_deliveries</c> first: a worker of
    /// this process can pop the replacement as soon as the broker holds it, and that pop must not overwrite or settle the
    /// original. If anything fails before the acknowledgement, the original stays unacknowledged and is put back, so the
    /// broker redelivers it and a later call can still settle it (RQM-001).
    /// </summary>
    private async Task ReplaceDeliveryAsync(QueuedMessage message, Func<Task> publishReplacement, CancellationToken cancellationToken)
    {
        _deliveries.TryRemove(message.Id, out var delivery);
        try
        {
            if (BeforeReplacementPublished is { } before) await before(cancellationToken);
            await publishReplacement();
            if (AfterReplacementConfirmed is { } after) await after(cancellationToken);
        }
        catch
        {
            if (delivery is not null) _deliveries.TryAdd(message.Id, delivery);
            throw;
        }

        if (delivery is not null) await AcknowledgeAsync(delivery, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        // RabbitMQ has no "put this specific message back with a new delay" primitive - publish a fresh message
        // carrying the updated attempt count/delay, and only after the broker has confirmed it acknowledge the
        // original delivery. If the process stops, or the publish fails, before the acknowledgement, the broker
        // requeues the original: the job can then run twice, but it is never lost.
        message.ReservedAt = null;
        message.AvailableAt = DateTimeOffset.UtcNow + delay;
        await ReplaceDeliveryAsync(message, () => PublishAsync(message, confirmed: true, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task FailAsync(QueuedMessage message, CancellationToken cancellationToken = default)
        // Same order as ReleaseAsync: the failed copy is confirmed by the broker (and persistent, so it is on disk)
        // before the original delivery is acknowledged.
        => ReplaceDeliveryAsync(message, async () =>
        {
            await using var channel = await OpenChannelAsync(cancellationToken, confirmed: true);
            await EnsureTopologyAsync(channel, message.Queue, cancellationToken);
            var body = Encoding.UTF8.GetBytes(QueueEnvelopeJson.Serialize(message));
            var properties = new BasicProperties { DeliveryMode = DeliveryModes.Persistent };
            await channel.BasicPublishAsync("", $"netqueue.failed.{message.Queue}", false, properties, body, cancellationToken);
        }, cancellationToken);

    /// <inheritdoc />
    public async Task<long> SizeAsync(string queue, CancellationToken cancellationToken = default)
    {
        await using var channel = await OpenChannelAsync(cancellationToken);
        await EnsureTopologyAsync(channel, queue, cancellationToken);
        var status = await channel.QueueDeclarePassiveAsync($"netqueue.ready.{queue}", cancellationToken);
        return status.MessageCount;
    }

    /// <summary>Closes every in-flight channel and the shared connection.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var delivery in _deliveries.Values)
            await delivery.Channel.DisposeAsync();
        _deliveries.Clear();
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }
        _connectionGate.Dispose();
    }
}

/// <summary>Registration extension methods for the RabbitMQ queue driver.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the "rabbitmq" driver for every configured store whose "Driver" is "rabbitmq".
    /// Reads that store's own "HostName"/"Port"/"UserName"/"Password"/"VirtualHost", plus "QueueType" ("Classic" by
    /// default, or "Quorum") and, for quorum queues, "DeliveryLimit".
    /// Note: the connection is shared per store, and each in-flight delivery holds its own channel so
    /// its acknowledgement stays on the channel that fetched it.
    /// </summary>
    public static IServiceCollection AddRabbitMqDriver(this IServiceCollection services, IConfiguration configuration, string sectionName = "NaravelQueue")
        => services.AddQueueDriver(configuration, "rabbitmq", (_, store) =>
        {
            var host = store.GetOrDefault("HostName", "localhost");
            var port = int.Parse(store.GetOrDefault("Port", "5672"));
            var user = store["UserName"];
            var pass = store["Password"];
            var vhost = store["VirtualHost"];
            var type = Enum.Parse<RabbitMqQueueType>(store.GetOrDefault("QueueType", "Classic"), ignoreCase: true);
            int? limit = int.TryParse(store["DeliveryLimit"], out var parsed) ? parsed : null;
            return new RabbitMqQueueDriver(host, port, user, pass, vhost, type, limit);
        }, sectionName);
}
