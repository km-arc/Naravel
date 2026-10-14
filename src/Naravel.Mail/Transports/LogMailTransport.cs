using Microsoft.Extensions.Logging;

namespace Naravel.Mail.Transports;

/// <summary>
/// Development transport: writes the sender, recipients and subject of each message to <see cref="ILogger"/> and delivers nothing.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the <c>log</c> mailer.</para>
/// <para><b>Why it exists:</b> a safe default that cannot send real mail by accident.</para>
/// <para><b>Caution:</b> recipients appear in the log. Do not use it where logs are shared; telemetry (meter and activity) never carries them.
/// The body is not logged.</para>
/// </remarks>
public sealed class LogMailTransport : IMailTransport
{
    private readonly ILogger _logger;

    /// <summary>Creates the transport.</summary>
    /// <param name="logger">The logger that receives the entries.</param>
    public LogMailTransport(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task SendAsync(MailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation(
            "Naravel.Mail (log transport): from {From} to {To} cc {Cc} bcc {Bcc} subject '{Subject}'.",
            message.From, string.Join(", ", message.To), string.Join(", ", message.Cc), string.Join(", ", message.Bcc), message.Subject);
        return Task.CompletedTask;
    }
}
