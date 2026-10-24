namespace Naravel.Notifications;

/// <summary>
/// Delivers one notification type through one named channel (mail, a queue, a log, an application-specific provider).
/// </summary>
/// <typeparam name="TNotification">The notification type this channel can deliver.</typeparam>
/// <remarks>
/// <para><b>Laravel equivalent:</b> a notification channel class with a <c>send($notifiable, $notification)</c> method.</para>
/// <para><b>Why it exists:</b> a channel is an ordinary typed object resolved from DI, so applications add their own without changing the core.
/// The registration order is never channel selection; the caller's channel names are (PDR-014).</para>
/// <para><b>Not ported on purpose:</b> <c>toMail()</c>-style conventions found by reflection; a channel builds what it sends from the typed notification.</para>
/// </remarks>
public interface INotificationChannel<TNotification>
{
    /// <summary>The channel name callers pass to <see cref="INotificationSender"/>, for example <c>mail</c>.</summary>
    string Name { get; }

    /// <summary>Delivers the notification to the recipient in <paramref name="context"/>.</summary>
    /// <param name="context">The recipient and the channel being delivered to.</param>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    Task SendAsync(NotificationContext context, TNotification notification, CancellationToken cancellationToken);
}
