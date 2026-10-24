namespace Naravel.Notifications;

/// <summary>
/// Sends one typed notification to a recipient through explicitly named channels.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Contracts\Notifications\Dispatcher</c> (<c>Notification::send</c>).</para>
/// <para><b>Why it exists:</b> when one notification must reach several channels in a known order, one call replaces repeated
/// provider calls and can be replaced by <c>NotificationFake</c> in a test (PDR-014).</para>
/// <para><b>Not ported on purpose:</b> <c>Notifiable</c> traits, <c>via()</c> channel inference and magic <c>routeNotificationFor</c>
/// properties: the caller names the recipient and the channels.</para>
/// </remarks>
public interface INotificationSender
{
    /// <summary>
    /// Delivers <paramref name="notification"/> to every named channel, in the order given. Every channel name is resolved before the
    /// first delivery, so a missing channel fails the call without a partial delivery. A channel that throws stops the dispatch.
    /// </summary>
    /// <typeparam name="TNotification">The notification type; channels are registered per notification type.</typeparam>
    /// <param name="recipient">The explicit recipient handed to every channel; it must not be <see langword="null"/>.</param>
    /// <param name="notification">The notification.</param>
    /// <param name="channels">The channel names, at least one and without duplicates (compared ignoring case).</param>
    /// <param name="cancellationToken">Cancels the dispatch; cancellation is not wrapped in <see cref="NotificationDeliveryException"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="channels"/> is empty, has a blank name or repeats a name.</exception>
    /// <exception cref="InvalidOperationException">A requested channel is not registered for <typeparamref name="TNotification"/>.</exception>
    /// <exception cref="NotificationDeliveryException">A channel failed; it names the failed channel and the channels already delivered.</exception>
    Task SendAsync<TNotification>(object recipient, TNotification notification, IReadOnlyList<string> channels, CancellationToken cancellationToken = default);
}
