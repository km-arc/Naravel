namespace Naravel.Notifications;

/// <summary>A channel failed while a notification was being delivered; earlier channels may already have delivered.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> none; Laravel lets a channel exception escape without saying which channels already sent.</para>
/// <para><b>Why it exists:</b> partial delivery must not look like total success, and a caller that retries needs to know what already went out (PDR-014).
/// The original exception is <see cref="Exception.InnerException"/>.</para>
/// </remarks>
public sealed class NotificationDeliveryException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="channel">The channel that failed.</param>
    /// <param name="deliveredChannels">The channels that had already delivered, in order.</param>
    /// <param name="innerException">The channel's exception.</param>
    public NotificationDeliveryException(string channel, IReadOnlyList<string> deliveredChannels, Exception innerException)
        : base(Describe(channel, deliveredChannels), innerException)
    {
        Channel = channel;
        DeliveredChannels = deliveredChannels;
    }

    /// <summary>The channel that failed; channels after it were not attempted.</summary>
    public string Channel { get; }

    /// <summary>The channels that had already delivered before the failure, in order.</summary>
    public IReadOnlyList<string> DeliveredChannels { get; }

    private static string Describe(string channel, IReadOnlyList<string> deliveredChannels) =>
        deliveredChannels.Count == 0
            ? $"Notification channel '{channel}' failed; no channel had delivered yet."
            : $"Notification channel '{channel}' failed after delivering to: {string.Join(", ", deliveredChannels)}.";
}
