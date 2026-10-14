namespace Naravel.Mail;

/// <summary>
/// A started fluent send: collects recipients, then sends or queues a <see cref="Mailable"/>.
/// </summary>
/// <remarks>
/// <b>Laravel equivalent:</b> <c>PendingMail</c> returned by <c>Mail::to()</c>.
/// <b>Why it exists:</b> keeps the common call at one line: <c>await mailer.To(user.Email).SendAsync(new WelcomeMail(user))</c>.
/// <b>Not ported:</b> <c>cc()/bcc()</c> on the pending object (use <see cref="MailBuilder"/> inside the mailable) and
/// <c>later()</c> (use the queue adapter).
/// </remarks>
public sealed class PendingMail
{
    private readonly IMailer _mailer;
    private readonly List<string> _to = new();

    internal PendingMail(IMailer mailer, string address)
    {
        ArgumentNullException.ThrowIfNull(mailer);
        ArgumentNullException.ThrowIfNull(address);
        _mailer = mailer;
        _to.Add(address);
    }

    /// <summary>Adds another recipient.</summary>
    /// <param name="address">Recipient address.</param>
    public PendingMail To(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        _to.Add(address);
        return this;
    }

    /// <summary>Builds the mailable and sends it now.</summary>
    /// <param name="mailable">The mailable to build.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    public async Task SendAsync(Mailable mailable, CancellationToken cancellationToken = default) =>
        await _mailer.SendAsync(Build(mailable), cancellationToken).ConfigureAwait(false);

    /// <summary>Builds the mailable and hands the resulting message to the queue adapter.</summary>
    /// <param name="mailable">The mailable to build.</param>
    /// <param name="cancellationToken">Cancels the enqueue.</param>
    public async Task QueueAsync(Mailable mailable, CancellationToken cancellationToken = default) =>
        await _mailer.QueueAsync(Build(mailable), cancellationToken).ConfigureAwait(false);

    private MailMessage Build(Mailable mailable)
    {
        ArgumentNullException.ThrowIfNull(mailable);
        var message = mailable.Build(new MailBuilder(_to, _mailer.DefaultFrom));
        return message ?? throw new InvalidOperationException(
            $"{mailable.GetType().Name}.Build returned null. Return mail.Build().");
    }
}
