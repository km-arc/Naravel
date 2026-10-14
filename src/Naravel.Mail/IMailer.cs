namespace Naravel.Mail;

/// <summary>
/// The application-facing mail API: send a message or a <see cref="Mailable"/>, now or through the queue adapter.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Contracts\Mail\Mailer</c> and the <c>Mail</c> facade.</para>
/// <para><b>Why it exists:</b> a stable, injectable and fakeable entry point (<c>MailFake</c>) in front of the named transports.</para>
/// <para><b>Not ported on purpose:</b> <c>Mail::raw</c>, <c>Mail::html</c>, global static access and view rendering.</para>
/// </remarks>
public interface IMailer
{
    /// <summary>Sends a message through the transport of this mailer.</summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    Task SendAsync(MailMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hands a message to the queue adapter (<c>Naravel.Mail.Queue</c>), which sends it later from a worker.
    /// Throws <see cref="InvalidOperationException"/> when no queue adapter is registered.
    /// </summary>
    /// <param name="message">The message to queue.</param>
    /// <param name="cancellationToken">Cancels the enqueue.</param>
    Task QueueAsync(MailMessage message, CancellationToken cancellationToken = default);

    /// <summary>Starts a fluent send to one recipient: <c>await mailer.To(address).SendAsync(new WelcomeMail(user))</c>.</summary>
    /// <param name="address">The first recipient.</param>
    PendingMail To(string address);

    /// <summary>The default sender configured as <c>Mail:From</c>, or <see langword="null"/>. Used by <see cref="MailBuilder"/>.</summary>
    string? DefaultFrom { get; }
}
