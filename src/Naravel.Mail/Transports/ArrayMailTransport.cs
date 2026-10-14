using System.Collections.Concurrent;

namespace Naravel.Mail.Transports;

/// <summary>
/// Keeps every sent message in memory instead of delivering it, for tests and local runs.
/// </summary>
/// <remarks>
/// <b>Laravel equivalent:</b> the <c>array</c> mailer. <b>Why it exists:</b> inspect real <see cref="MailManager"/> sends
/// without a network; use <c>MailFake</c> instead when you only need assertions on an injected <see cref="IMailer"/>.
/// Thread-safe.
/// </remarks>
public sealed class ArrayMailTransport : IMailTransport
{
    private readonly ConcurrentQueue<MailMessage> _sent = new();

    /// <summary>The messages sent so far, oldest first.</summary>
    public IReadOnlyList<MailMessage> Sent => _sent.ToArray();

    /// <summary>Forgets every recorded message.</summary>
    public void Clear() => _sent.Clear();

    /// <inheritdoc />
    public Task SendAsync(MailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
