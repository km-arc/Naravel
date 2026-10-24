using Microsoft.Extensions.Logging;

namespace Naravel.Notifications;

internal sealed class LogNotificationChannel<TNotification>(string name, ILogger logger) : INotificationChannel<TNotification>
{
    public string Name => name;

    public Task SendAsync(NotificationContext context, TNotification notification, CancellationToken cancellationToken)
    {
        // Only type names are logged: the notification and the recipient may hold personal data.
        logger.LogInformation(
            "Notification {Notification} delivered to channel {Channel} for a recipient of type {Recipient}.",
            typeof(TNotification).Name,
            context.Channel,
            context.Recipient.GetType().Name);
        return Task.CompletedTask;
    }
}
