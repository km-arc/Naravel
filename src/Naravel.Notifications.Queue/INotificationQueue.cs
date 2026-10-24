namespace Naravel.Notifications.Queue;

/// <summary>Queues a registered notification payload so a worker sends the notification later.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> a notification that implements <c>ShouldQueue</c>.</para>
/// <para><b>Why it exists:</b> the queue stores an explicit alias and a plain payload, never a CLR type name and never the notification or the
/// recipient object (PDR-014, OD-07). The handler registered under the alias rebuilds the recipient and the notification from the payload
/// when the worker runs it, so a stale payload can still be loaded fresh data.</para>
/// <para><b>Delivery guarantee:</b> at-least-once. If the worker stops after the channels delivered but before the job is acknowledged, the
/// notification is sent again; make the handler idempotent where that matters.</para>
/// <para><b>Not ported on purpose:</b> implicit queueing by interface and serializing the notification object.</para>
/// </remarks>
public interface INotificationQueue
{
    /// <summary>Stores <paramref name="payload"/> under <paramref name="alias"/> on the queue.</summary>
    /// <typeparam name="TPayload">The payload type registered for the alias.</typeparam>
    /// <param name="alias">An alias registered with <c>AddQueuedNotification</c>.</param>
    /// <param name="payload">Plain data the handler needs to rebuild the notification; it is serialized as JSON.</param>
    /// <param name="cancellationToken">Cancels queueing.</param>
    /// <exception cref="InvalidOperationException">The alias is not registered.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TPayload"/> is not the type registered for the alias.</exception>
    Task QueueAsync<TPayload>(string alias, TPayload payload, CancellationToken cancellationToken = default);
}
