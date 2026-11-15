using Naravel.Mail;

namespace NaravelApp.App.Mail;

/// <summary>
/// A mailable: the message as a class. The sender comes from <c>Mail:From</c> in config/mail.json and the transport from
/// <c>Mail:Default</c>; with <c>log</c> the message is written to the application log and nothing is delivered.
/// </summary>
public sealed class WelcomeMail(string name) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.Subject("Welcome")
            .Text($"Hi {name}, welcome aboard.")
            .Html($"<p>Hi {System.Net.WebUtility.HtmlEncode(name)}, welcome aboard.</p>")
            .Build();
}
