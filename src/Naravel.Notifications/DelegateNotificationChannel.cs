namespace Naravel.Notifications;

internal sealed class DelegateNotificationChannel<TNotification>(
    string name,
    Func<NotificationContext, TNotification, CancellationToken, Task> send) : INotificationChannel<TNotification>
{
    public string Name => name;

    public Task SendAsync(NotificationContext context, TNotification notification, CancellationToken cancellationToken) =>
        send(context, notification, cancellationToken);
}
