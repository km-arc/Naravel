namespace Naravel.Notifications;

/// <summary>What a channel knows about one delivery: the explicit recipient and the channel name.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the <c>$notifiable</c> argument of a channel's <c>send</c>.</para>
/// <para><b>Why it exists:</b> the recipient is an explicit <see cref="object"/> chosen by the caller, so no model trait or magic property is needed.</para>
/// <para><b>Not ported on purpose:</b> notifiable routing methods.</para>
/// </remarks>
public sealed class NotificationContext
{
    /// <summary>Creates a context.</summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="channel">The channel name.</param>
    public NotificationContext(object recipient, string channel)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        Recipient = recipient;
        Channel = channel;
    }

    /// <summary>The recipient the caller passed to <see cref="INotificationSender"/>.</summary>
    public object Recipient { get; }

    /// <summary>The channel name being delivered to.</summary>
    public string Channel { get; }

    /// <summary>Returns the recipient as <typeparamref name="TRecipient"/>.</summary>
    /// <typeparam name="TRecipient">The type the channel expects.</typeparam>
    /// <exception cref="InvalidOperationException">The recipient is not a <typeparamref name="TRecipient"/>.</exception>
    public TRecipient GetRecipient<TRecipient>() =>
        Recipient is TRecipient typed
            ? typed
            : throw new InvalidOperationException(
                $"Channel '{Channel}' expects a recipient of type {typeof(TRecipient).Name} but got {Recipient.GetType().Name}.");
}
