using Naravel.Mail;

//#if (namespace != "")
namespace ItemNamespace;
//#elseif (ProjectNamespace != "")
namespace ProjectRootNamespace.App.Mail;
//#else
#error The namespace is unknown: run dotnet build in the project, generate this file again in an existing folder of the project, or pass --namespace.
//#endif

/// <summary>
/// A mailable: the message as a class. The sender comes from <c>Mail:From</c> in config/mail.json and the transport from
/// <c>Mail:Default</c>. Send it with <c>IMailer.To(address).SendAsync(new NaravelItem(), cancellationToken)</c>.
/// </summary>
public sealed class NaravelItem : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.Subject("Subject")
            .Text("Write the message here.")
            .Build();
}
