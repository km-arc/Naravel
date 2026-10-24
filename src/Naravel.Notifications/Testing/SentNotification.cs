namespace Naravel.Notifications.Testing;

/// <summary>One notification recorded by <see cref="NotificationFake"/>.</summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
/// <remarks>
/// <b>Laravel equivalent:</b> an entry of <c>NotificationFake::sent()</c>. Plain data on purpose: nothing here is sent to telemetry.
/// </remarks>
public sealed class SentNotification<TNotification>
{
    internal SentNotification(object recipient, TNotification notification, IReadOnlyList<string> channels)
    {
        Recipient = recipient;
        Notification = notification;
        Channels = channels;
    }

    /// <summary>The recipient passed to the sender.</summary>
    public object Recipient { get; }

    /// <summary>The notification passed to the sender.</summary>
    public TNotification Notification { get; }

    /// <summary>The requested channel names, in the order requested.</summary>
    public IReadOnlyList<string> Channels { get; }
}
