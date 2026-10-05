using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
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
/// Delivery uses manual ack via BasicGet/BasicAck (pull-based, matching IQueueDriver's PopAsync contract).
/// Delay is implemented with per-message TTL + dead-lettering, which does not require the RabbitMQ
/// delayed-message-exchange plugin. Caveat: because RabbitMQ only expires messages once they reach
/// the head of the queue, a very-long-delay message queued before a short-delay one can make the
/// short one wait slightly longer than requested - fine for typical retry/delay use cases.
/// </summary>
public class RabbitMqQueueDriver : IQueueDriver, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ConcurrentDictionary<string, ulong> _deliveryTags = new();
    private readonly ConcurrentDictionary<string, bool> _declaredQueues = new();
    private readonly object _channelLock = new();

    public RabbitMqQueueDriver(string hostName, int port, string? userName, string? password, string? virtualHost)
    {
        var factory = new ConnectionFactory
        {
            HostName = hostName,
            Port = port,
            DispatchConsumersAsync = false
        };
        if (!string.IsNullOrEmpty(userName)) factory.UserName = userName;
        if (!string.IsNullOrEmpty(password)) factory.Password = password;
        if (!string.IsNullOrEmpty(virtualHost)) factory.VirtualHost = virtualHost;

        _connection = factory.CreateConnection("netqueue");
        _channel = _connection.CreateModel();
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 20, global: false);
    }

    private void EnsureTopology(string queue)
    {
        if (!_declaredQueues.TryAdd(queue, true)) return;
        lock (_channelLock)
        {
            _channel.ExchangeDeclare("netqueue.direct", ExchangeType.Direct, durable: true);

            _channel.QueueDeclare($"netqueue.ready.{queue}", durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object> { ["x-max-priority"] = 9 });
            _channel.QueueBind($"netqueue.ready.{queue}", "netqueue.direct", queue);

            _channel.QueueDeclare($"netqueue.delay.{queue}", durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object>
                {
                    ["x-dead-letter-exchange"] = "netqueue.direct",
                    ["x-dead-letter-routing-key"] = queue
                });

            _channel.QueueDeclare($"netqueue.failed.{queue}", durable: true, exclusive: false, autoDelete: false);
        }
    }

    public Task PushAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        EnsureTopology(message.Queue);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        lock (_channelLock)
        {
            var props = _channel.CreateBasicProperties();
            props.Persistent = true;
            props.MessageId = message.Id;
            props.Priority = (byte)Math.Clamp(message.Priority, 0, 9);

            var delay = message.AvailableAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                props.Expiration = ((long)delay.TotalMilliseconds).ToString();
                _channel.BasicPublish(exchange: "", routingKey: $"netqueue.delay.{message.Queue}", basicProperties: props, body: body);
            }
            else
            {
                _channel.BasicPublish(exchange: "netqueue.direct", routingKey: message.Queue, basicProperties: props, body: body);
            }
        }

        return Task.CompletedTask;
    }

    public Task<QueuedMessage?> PopAsync(string queue, CancellationToken cancellationToken = default)
    {
        EnsureTopology(queue);

        BasicGetResult? result;
        lock (_channelLock) { result = _channel.BasicGet($"netqueue.ready.{queue}", autoAck: false); }
        if (result == null) return Task.FromResult<QueuedMessage?>(null);

        var json = Encoding.UTF8.GetString(result.Body.ToArray());
        var message = JsonSerializer.Deserialize<QueuedMessage>(json)!;
        message.ReservedAt = DateTimeOffset.UtcNow;
        _deliveryTags[message.Id] = result.DeliveryTag;
        return Task.FromResult<QueuedMessage?>(message);
    }

    public Task AckAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        if (_deliveryTags.TryRemove(message.Id, out var tag))
            lock (_channelLock) { _channel.BasicAck(tag, multiple: false); }
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        // RabbitMQ has no "put this specific message back with a new delay" primitive - acknowledge the
        // original delivery and republish a fresh message carrying the updated attempt count/delay instead.
        if (_deliveryTags.TryRemove(message.Id, out var tag))
            lock (_channelLock) { _channel.BasicAck(tag, multiple: false); }

        message.ReservedAt = null;
        message.AvailableAt = DateTimeOffset.UtcNow + delay;
        return PushAsync(message, cancellationToken);
    }

    public Task FailAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        if (_deliveryTags.TryRemove(message.Id, out var tag))
            lock (_channelLock) { _channel.BasicAck(tag, multiple: false); }

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        lock (_channelLock) { _channel.BasicPublish(exchange: "", routingKey: $"netqueue.failed.{message.Queue}", basicProperties: null, body: body); }
        return Task.CompletedTask;
    }

    public Task<long> SizeAsync(string queue, CancellationToken cancellationToken = default)
    {
        EnsureTopology(queue);
        lock (_channelLock) { return Task.FromResult((long)_channel.MessageCount($"netqueue.ready.{queue}")); }
    }

    public void Dispose()
    {
        try { _channel.Close(); } catch { /* best effort */ }
        try { _connection.Close(); } catch { /* best effort */ }
        _channel.Dispose();
        _connection.Dispose();
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
