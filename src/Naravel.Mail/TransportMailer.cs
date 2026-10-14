using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Naravel.Mail.Diagnostics;

namespace Naravel.Mail;

internal sealed class TransportMailer(
    MailManager manager,
    string? transportName,
    IOptionsMonitor<MailOptions> options,
    IServiceProvider provider) : IMailer
{
    public string? DefaultFrom => options.CurrentValue.From;

    public PendingMail To(string address) => new(this, address);

    public async Task SendAsync(MailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var name = transportName ?? manager.DefaultDriverName;
        using var activity = MailTelemetry.ActivitySource.StartActivity("mail.send", ActivityKind.Client);
        activity?.SetTag("naravel.mail.transport", name);
        try
        {
            var transport = await manager.DriverAsync(name, cancellationToken).ConfigureAwait(false);
            await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);
            MailTelemetry.Sent.Add(1, new KeyValuePair<string, object?>("transport", name));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            MailTelemetry.Failed.Add(1, new KeyValuePair<string, object?>("transport", name));
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }

    public async Task QueueAsync(MailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var queue = provider.GetService<IMailQueue>() ?? throw new InvalidOperationException(
            "No mail queue adapter is registered. Install Naravel.Mail.Queue and call services.AddNaravelMailQueue(), or use SendAsync.");
        await queue.EnqueueAsync(message, transportName, cancellationToken).ConfigureAwait(false);
    }
}
