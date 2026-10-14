using System.Collections.Concurrent;

namespace Naravel.Mail.Testing;

/// <summary>
/// In-memory <see cref="IMailer"/> that records sent and queued messages and asserts on them.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Mail::fake()</c> with <c>assertSent</c>, <c>assertQueued</c>, <c>assertNothingSent</c>.</para>
/// <para><b>Why it exists:</b> inject it where <see cref="IMailer"/> is expected and assert on the data, with no transport and no queue.</para>
/// <para><b>Not ported on purpose:</b> global fake state; the fake is an ordinary object you pass in.</para>
/// </remarks>
public sealed class MailFake : IMailer
{
    private readonly ConcurrentQueue<MailMessage> _sent = new();
    private readonly ConcurrentQueue<MailMessage> _queued = new();

    /// <summary>The sender <see cref="MailBuilder"/> uses when a mailable does not set one. Defaults to <c>noreply@example.test</c>.</summary>
    public string? DefaultFrom { get; set; } = "noreply@example.test";

    /// <summary>Messages recorded by <see cref="SendAsync"/>, oldest first.</summary>
    public IReadOnlyList<MailMessage> Sent => _sent.ToArray();

    /// <summary>Messages recorded by <see cref="QueueAsync"/>, oldest first.</summary>
    public IReadOnlyList<MailMessage> Queued => _queued.ToArray();

    /// <inheritdoc />
    public PendingMail To(string address) => new(this, address);

    /// <inheritdoc />
    public Task SendAsync(MailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task QueueAsync(MailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        _queued.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>Asserts that a message (matching <paramref name="predicate"/> when given) was sent, and returns the first match.</summary>
    /// <param name="predicate">Optional filter.</param>
    /// <exception cref="InvalidOperationException">No such message was sent.</exception>
    public MailMessage AssertSent(Func<MailMessage, bool>? predicate = null) =>
        _sent.FirstOrDefault(m => predicate is null || predicate(m))
        ?? throw new InvalidOperationException(NotFound("sent", _sent.Count, predicate is not null));

    /// <summary>Asserts that exactly <paramref name="expectedCount"/> messages were sent.</summary>
    /// <param name="expectedCount">The expected number of sent messages.</param>
    /// <exception cref="InvalidOperationException">The count differs.</exception>
    public IReadOnlyList<MailMessage> AssertSent(int expectedCount)
    {
        var sent = _sent.ToArray();
        return sent.Length == expectedCount
            ? sent
            : throw new InvalidOperationException($"Expected {expectedCount} sent mail message(s), but found {sent.Length}.");
    }

    /// <summary>Asserts that no message was sent.</summary>
    /// <exception cref="InvalidOperationException">At least one message was sent.</exception>
    public void AssertNothingSent()
    {
        if (!_sent.IsEmpty)
        {
            throw new InvalidOperationException($"Expected no mail to be sent, but {_sent.Count} message(s) were sent.");
        }
    }

    /// <summary>Asserts that a message (matching <paramref name="predicate"/> when given) was queued, and returns the first match.</summary>
    /// <param name="predicate">Optional filter.</param>
    /// <exception cref="InvalidOperationException">No such message was queued.</exception>
    public MailMessage AssertQueued(Func<MailMessage, bool>? predicate = null) =>
        _queued.FirstOrDefault(m => predicate is null || predicate(m))
        ?? throw new InvalidOperationException(NotFound("queued", _queued.Count, predicate is not null));

    /// <summary>Asserts that no message was queued.</summary>
    /// <exception cref="InvalidOperationException">At least one message was queued.</exception>
    public void AssertNothingQueued()
    {
        if (!_queued.IsEmpty)
        {
            throw new InvalidOperationException($"Expected no mail to be queued, but {_queued.Count} message(s) were queued.");
        }
    }

    /// <summary>Forgets every recorded message.</summary>
    public void Reset()
    {
        _sent.Clear();
        _queued.Clear();
    }

    private static string NotFound(string verb, int total, bool filtered) =>
        filtered
            ? $"No matching mail message was {verb} ({total} {verb} in total)."
            : $"No mail message was {verb}.";
}
