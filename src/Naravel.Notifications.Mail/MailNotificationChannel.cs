using Naravel.Mail;

namespace Naravel.Notifications.Mail;

internal sealed class MailNotificationChannel<TNotification>(
    string name,
    Func<NotificationContext, string> address,
    Func<TNotification, Mailable> mailable,
    string? mailer,
    MailManager manager) : INotificationChannel<TNotification>
{
    public string Name => name;

    public async Task SendAsync(NotificationContext context, TNotification notification, CancellationToken cancellationToken)
    {
        // Address and Mailable come from the caller's delegates; PendingMail validates the address and fills the mailer's default From.
        var to = address(context);
        var content = mailable(notification) ?? throw new InvalidOperationException(
            $"The mailable factory for {typeof(TNotification).Name} returned null.");
        await manager.Mailer(mailer).To(to).SendAsync(content, cancellationToken).ConfigureAwait(false);
    }
}
