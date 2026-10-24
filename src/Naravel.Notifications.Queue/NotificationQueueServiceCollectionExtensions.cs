using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Naravel.Notifications;
using Naravel.Notifications.Queue;
using Naravel.Queue.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the optional queue adapter for <c>Naravel.Notifications</c>.</summary>
public static class NotificationQueueServiceCollectionExtensions
{
    /// <summary>The job alias stored in queued messages.</summary>
    internal const string JobAlias = "naravel.notifications.send";

    /// <summary>
    /// Makes <see cref="INotificationQueue"/> work: registers the <see cref="SendQueuedNotificationJob"/> under a stable alias. Call
    /// <c>AddNaravelNotifications</c>, <c>AddQueue</c>, a queue driver and (in worker processes) <c>AddQueueWorker</c> separately.
    /// </summary>
    /// <remarks>
    /// <para><b>Laravel equivalent:</b> <c>ShouldQueue</c> notifications.</para>
    /// <para><b>Why it exists:</b> core <c>Naravel.Notifications</c> does not reference the queue; this opt-in package connects them (PDR-014).</para>
    /// <para><b>Not ported on purpose:</b> implicit queueing and serialization of the notification object.</para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional connection and queue placement.</param>
    public static IServiceCollection AddNaravelNotificationQueue(this IServiceCollection services, Action<NotificationQueueOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<NotificationQueueOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.AddJob<SendQueuedNotificationJob>(JobAlias);
        services.TryAddSingleton<QueuedNotificationRegistry>();
        services.TryAddSingleton<INotificationQueue, QueuedNotificationDispatcher>();
        return services;
    }

    /// <summary>
    /// Registers the handler that a worker runs for <paramref name="alias"/>. The handler receives the deserialized payload and the job's
    /// service scope: it loads the recipient from your own services, builds the notification and sends it with the scope's
    /// <see cref="INotificationSender"/>.
    /// </summary>
    /// <typeparam name="TPayload">The plain payload type, serialized with System.Text.Json.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="alias">A stable alias that is stored in the queue; never rename it while jobs with it are queued.</param>
    /// <param name="send">Handles one payload; it gets the deserialized payload, the job's scope and the cancellation token.</param>
    public static IServiceCollection AddQueuedNotification<TPayload>(
        this IServiceCollection services,
        string alias,
        Func<TPayload, IServiceProvider, CancellationToken, Task> send)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentNullException.ThrowIfNull(send);

        services.AddSingleton(new QueuedNotificationEntry(alias, typeof(TPayload), (json, scope, cancellationToken) =>
        {
            var payload = JsonSerializer.Deserialize<TPayload>(json)
                ?? throw new InvalidOperationException($"The stored payload for '{alias}' is empty.");
            return send(payload, scope, cancellationToken);
        }));
        return services;
    }
}
