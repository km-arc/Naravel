namespace Naravel.Mail;

/// <summary>
/// A typed factory for a <see cref="MailMessage"/>: subclass it, take your data in the constructor, and describe the
/// message in <see cref="Build"/>.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Mail\Mailable</c> (<c>build()</c>).</para>
/// <para><b>Why it exists:</b> a message "as a class" keeps mail content next to its data and is easy to test: call
/// <see cref="Build"/> and assert on the result.</para>
/// <para><b>Contract:</b> <see cref="Build"/> must be pure (no I/O, no clock, no randomness the caller cannot control) because
/// the queue adapter calls it before enqueueing and stores only the resulting data.</para>
/// <para><b>Not ported on purpose:</b> convention-based view lookup, attribute discovery and queueing the object itself.</para>
/// </remarks>
/// <example>
/// <code>
/// public class WelcomeMail(User user) : Mailable
/// {
///     public override MailMessage Build(MailBuilder mail) =>
///         mail.From("app@example.com").Subject("Welcome").Text($"Hi {user.Name}").Build();
/// }
/// await mailer.To(user.Email).SendAsync(new WelcomeMail(user), cancellationToken);
/// </code>
/// </example>
public abstract class Mailable
{
    /// <summary>Builds the message. The builder already holds the recipients given to <c>To(...)</c>.</summary>
    /// <param name="mail">A builder pre-filled with the recipients and the configured default sender.</param>
    /// <returns>The finished message.</returns>
    public abstract MailMessage Build(MailBuilder mail);
}
