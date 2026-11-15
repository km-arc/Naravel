using Naravel.Mail;
using Naravel.Queue.Jobs;
using NaravelApp.App.Mail;

namespace NaravelApp.App.Jobs;

/// <summary>
/// A queued job. Its public properties are stored as JSON and rebuilt in the queue worker, so keep them small and serializable.
/// A job must be registered with <c>AddJob&lt;SendWelcomeEmail&gt;("mail.welcome")</c> in Program.cs: the alias is what is stored in the
/// queue, and only registered jobs can be rebuilt.
/// </summary>
public sealed class SendWelcomeEmail : Job
{
    public string Email { get; set; } = default!;
    public string Name { get; set; } = default!;

    public SendWelcomeEmail() { } // the worker needs it to rebuild the job from JSON

    public SendWelcomeEmail(string email, string name)
    {
        Email = email;
        Name = name;
    }

    public override async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        // Resolve what the job needs from the services of the worker, as a controller would from its constructor.
        var mailer = context.Services.GetRequiredService<IMailer>();
        await mailer.To(Email).SendAsync(new WelcomeMail(Name), cancellationToken);
    }
}
