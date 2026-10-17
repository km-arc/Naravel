using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Naravel.Mail.Smtp;

/// <summary>Delivers a <see cref="MailMessage"/> through an SMTP server using MailKit.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the <c>smtp</c> mail transport.</para>
/// <para><b>Why it exists:</b> it is the only package that depends on MailKit; core <c>Naravel.Mail</c> stays free of a
/// network dependency (PDR-013).</para>
/// <para><b>Behavior:</b> every send opens its own connection (a MailKit client is not thread-safe), authenticates when a
/// user name is configured, sends, and disconnects. Any failure is thrown to the caller unchanged; nothing is retried here
/// because a retry after an ambiguous failure can deliver twice. Queued mail is retried by the queue job.</para>
/// <para><b>Not ported on purpose:</b> connection pooling, DKIM signing and provider-specific APIs.</para>
/// </remarks>
public sealed class SmtpMailTransport : IMailTransport
{
    private readonly SmtpMailOptions _options;

    /// <summary>Creates the transport.</summary>
    /// <param name="options">Server settings. <see cref="SmtpMailOptions.Host"/> is required.</param>
    /// <exception cref="InvalidOperationException">The options are incomplete or out of range.</exception>
    public SmtpMailTransport(SmtpMailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = new SmtpMailOptions
        {
            Host = options.Host,
            Port = options.Port,
            Security = options.Security,
            Username = options.Username,
            Password = options.Password,
            Timeout = options.Timeout
        };
    }

    /// <inheritdoc />
    public async Task SendAsync(MailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var mime = ToMimeMessage(message);
        using var client = new SmtpClient { Timeout = (int)Math.Min(_options.Timeout.TotalMilliseconds, int.MaxValue) };
        await client.ConnectAsync(_options.Host, _options.Port, ToSocketOptions(_options.Security), cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }

        await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
    }

    internal static MimeMessage ToMimeMessage(MailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(message.From));
        foreach (var address in message.To) mime.To.Add(MailboxAddress.Parse(address));
        foreach (var address in message.Cc) mime.Cc.Add(MailboxAddress.Parse(address));
        foreach (var address in message.Bcc) mime.Bcc.Add(MailboxAddress.Parse(address));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();
        return mime;
    }

    internal static SecureSocketOptions ToSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.None => SecureSocketOptions.None,
        _ => throw new ArgumentOutOfRangeException(nameof(security), security, "Unknown SMTP security mode.")
    };
}
