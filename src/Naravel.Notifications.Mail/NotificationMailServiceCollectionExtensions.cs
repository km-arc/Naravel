using Naravel.Mail;
using Naravel.Notifications;
using Naravel.Notifications.Mail;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the optional mail channel for <c>Naravel.Notifications</c>.</summary>
public static class NotificationMailServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <c>mail</c> channel for <typeparamref name="TNotification"/> that sends a <see cref="Mailable"/> through Naravel.Mail.
    /// Call <c>AddNaravelMail</c> and <c>AddNaravelNotifications</c> separately.
    /// </summary>
    /// <typeparam name="TNotification">The notification type the channel delivers.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="address">Reads the recipient's email address from the explicit recipient, for example <c>c =&gt; c.GetRecipient&lt;User&gt;().Email</c>.</param>
    /// <param name="mailable">Builds the mailable from the notification.</param>
    /// <param name="mailer">The named mailer to send with, or <see langword="null"/> for the default mailer.</param>
    /// <param name="name">The channel name callers pass to the sender, <c>mail</c> by default.</param>
    /// <remarks>
    /// <para><b>Laravel equivalent:</b> the <c>mail</c> channel with <c>toMail()</c> and <c>routeNotificationForMail()</c>.</para>
    /// <para><b>Why it exists:</b> the notification package stays free of Mail; this opt-in package connects them (PDR-014). Two plain delegates
    /// replace the conventions Laravel finds by reflection.</para>
    /// <para><b>Not ported on purpose:</b> <c>toMail()</c> discovery, notifiable routing methods and queueing by interface: queue through the
    /// Queue adapter or Naravel.Mail's own <c>QueueAsync</c>.</para>
    /// </remarks>
    public static IServiceCollection AddMailNotificationChannel<TNotification>(
        this IServiceCollection services,
        Func<NotificationContext, string> address,
        Func<TNotification, Mailable> mailable,
        string? mailer = null,
        string name = "mail")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(mailable);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        services.AddSingleton<INotificationChannel<TNotification>>(provider => new MailNotificationChannel<TNotification>(
            name,
            address,
            mailable,
            mailer,
            provider.GetRequiredService<MailManager>()));
        return services;
    }
}
