using System.Text.Json;
using Microsoft.Extensions.Options;
using Naravel.Queue.Dispatch;

namespace Naravel.Notifications.Queue;

internal sealed class QueuedNotificationDispatcher(
    IJobDispatcher dispatcher,
    QueuedNotificationRegistry registry,
    IOptions<NotificationQueueOptions> options) : INotificationQueue
{
    public Task QueueAsync<TPayload>(string alias, TPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentNullException.ThrowIfNull(payload);

        var entry = registry.Get(alias);
        if (entry.PayloadType != typeof(TPayload))
        {
            throw new ArgumentException(
                $"The alias '{alias}' was registered for payload type {entry.PayloadType.Name}, not {typeof(TPayload).Name}.", nameof(payload));
        }

        var job = new SendQueuedNotificationJob { Alias = alias, Payload = JsonSerializer.Serialize(payload) };
        var placement = options.Value;
        return dispatcher.DispatchAsync(job, dispatch =>
        {
            if (placement.Connection is not null) dispatch.OnConnection(placement.Connection);
            if (placement.Queue is not null) dispatch.OnQueue(placement.Queue);
        }, cancellationToken);
    }
}
