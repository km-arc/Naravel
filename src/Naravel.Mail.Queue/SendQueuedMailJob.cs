using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Jobs;

namespace Naravel.Mail.Queue;

/// <summary>
/// Queue job that delivers one already-built message through a named mail transport.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the <c>SendQueuedMailable</c> job.</para>
/// <para><b>Why it exists:</b> it stores only plain message data (addresses, subject, bodies) and an optional transport
/// name, so no CLR type name, delegate or <see cref="Mailable"/> is ever written to the queue (PDR-013a).</para>
/// <para><b>Delivery guarantee:</b> at-least-once. If the worker stops after the transport accepted the message but
/// before the job is acknowledged, the message is delivered again. Retries use the standard job backoff.</para>
/// <para><b>Not ported on purpose:</b> queueing the mailable object and restoring it by type name.</para>
/// </remarks>
public sealed class SendQueuedMailJob : Job
{
    /// <summary>Sender address.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Recipient addresses.</summary>
    public List<string> To { get; set; } = new();

    /// <summary>Carbon-copy addresses.</summary>
    public List<string> Cc { get; set; } = new();

    /// <summary>Blind carbon-copy addresses.</summary>
    public List<string> Bcc { get; set; } = new();

    /// <summary>Subject line.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Plain-text body, or <see langword="null"/>.</summary>
    public string? TextBody { get; set; }

    /// <summary>HTML body, or <see langword="null"/>.</summary>
    public string? HtmlBody { get; set; }

    /// <summary>Transport name to deliver with, or <see langword="null"/> for the default transport.</summary>
    public string? Transport { get; set; }

    /// <summary>Rebuilds the message (re-validating every address) and sends it through the named transport.</summary>
    public override async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        var message = new MailMessage(From, To, Subject, TextBody, HtmlBody, Cc, Bcc);
        var mailer = context.Services.GetRequiredService<MailManager>().Mailer(Transport);
        await mailer.SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    internal static SendQueuedMailJob FromMessage(MailMessage message, string? transport) => new()
    {
        From = message.From,
        To = message.To.ToList(),
        Cc = message.Cc.ToList(),
        Bcc = message.Bcc.ToList(),
        Subject = message.Subject,
        TextBody = message.TextBody,
        HtmlBody = message.HtmlBody,
        Transport = transport
    };
}
