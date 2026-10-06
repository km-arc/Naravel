using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using RabbitMQ.Client;

namespace Naravel.Queue.RabbitMQ;

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
/// </summary>
public class RabbitMqQueueDriver : IQueueDriver, IAsyncDisposable
{
    private sealed record Delivery(IChannel Channel, ulong Tag);

    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Delivery> _deliveries = new();
    private IConnection? _connection;

    public RabbitMqQueueDriver(string hostName, int port, string? userName, string? password, string? virtualHost)
    {
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

    private async ValueTask<IChannel> OpenChannelAsync(CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    private static async Task EnsureTopologyAsync(IChannel channel, string queue, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync("netqueue.direct", ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync($"netqueue.ready.{queue}", durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-max-priority"] = 9 }, cancellationToken: cancellationToken);
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

    public async Task PushAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        await using var channel = await OpenChannelAsync(cancellationToken);
        await EnsureTopologyAsync(channel, message.Queue, cancellationToken);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
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

            var message = JsonSerializer.Deserialize<QueuedMessage>(Encoding.UTF8.GetString(result.Body.Span))!;
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

    public async Task AckAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_deliveries.TryRemove(message.Id, out var delivery)) return;
        try { await delivery.Channel.BasicAckAsync(delivery.Tag, multiple: false, cancellationToken); }
        finally { await delivery.Channel.DisposeAsync(); }
    }

    public async Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        // RabbitMQ has no "put this specific message back with a new delay" primitive - acknowledge the
        // original delivery and republish a fresh message carrying the updated attempt count/delay instead.
        if (_deliveries.TryRemove(message.Id, out var delivery))
        {
            try { await delivery.Channel.BasicAckAsync(delivery.Tag, multiple: false, cancellationToken); }
            finally { await delivery.Channel.DisposeAsync(); }
        }

        message.ReservedAt = null;
        message.AvailableAt = DateTimeOffset.UtcNow + delay;
        await PushAsync(message, cancellationToken);
    }

    public async Task FailAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        if (_deliveries.TryRemove(message.Id, out var delivery))
        {
            try { await delivery.Channel.BasicAckAsync(delivery.Tag, multiple: false, cancellationToken); }
            finally { await delivery.Channel.DisposeAsync(); }
        }

        await using var channel = await OpenChannelAsync(cancellationToken);
        await EnsureTopologyAsync(channel, message.Queue, cancellationToken);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        await channel.BasicPublishAsync("", $"netqueue.failed.{message.Queue}", false, new BasicProperties(), body, cancellationToken);
    }

    public async Task<long> SizeAsync(string queue, CancellationToken cancellationToken = default)
    {
        await using var channel = await OpenChannelAsync(cancellationToken);
        await EnsureTopologyAsync(channel, queue, cancellationToken);
        var status = await channel.QueueDeclarePassiveAsync($"netqueue.ready.{queue}", cancellationToken);
        return status.MessageCount;
    }

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

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the "rabbitmq" driver for every configured store whose "Driver" is "rabbitmq".
    /// Reads that store's own "HostName"/"Port"/"UserName"/"Password"/"VirtualHost".
    /// Note: this driver keeps a single shared channel per store (fine for moderate throughput
    /// with the worker's Concurrency option); for very high throughput, run one worker process per
    /// desired parallel channel.
    /// </summary>
    public static IServiceCollection AddRabbitMqDriver(this IServiceCollection services, IConfiguration configuration, string sectionName = "NaravelQueue")
        => services.AddQueueDriver(configuration, "rabbitmq", (_, store) =>
        {
            var host = store.GetOrDefault("HostName", "localhost");
            var port = int.Parse(store.GetOrDefault("Port", "5672"));
            var user = store["UserName"];
            var pass = store["Password"];
            var vhost = store["VirtualHost"];
            return new RabbitMqQueueDriver(host, port, user, pass, vhost);
        }, sectionName);
}
