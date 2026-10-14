namespace Naravel.Mail;

/// <summary>
/// The boundary that actually delivers (or records) a <see cref="MailMessage"/>: SMTP, a log, an in-memory list.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> a mail transport behind <c>Mail::mailer('smtp')</c>.</para>
/// <para><b>Why it exists:</b> it is the single seam between application code and a delivery mechanism. Pick one by name in
/// configuration (<c>Mail:Default</c>) or add one at runtime with <c>MailManager.Extend</c>.</para>
/// <para><b>Contract:</b> return normally only when the transport accepted the message; throw on failure. Never retry
/// silently - a retry of a non-idempotent send can deliver twice.</para>
/// </remarks>
public interface IMailTransport
{
    /// <summary>Delivers the message.</summary>
    /// <param name="message">The validated message.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    Task SendAsync(MailMessage message, CancellationToken cancellationToken = default);
}
