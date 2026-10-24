namespace Naravel.Notifications.Testing;

/// <summary>An in-memory <see cref="INotificationSender"/> that records what was sent and delivers nothing.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Notification::fake()</c> with <c>assertSentTo</c> / <c>assertNothingSent</c>.</para>
/// <para><b>Why it exists:</b> tests assert "this notification went to this recipient through these channels" without any channel running.
/// It applies the same argument checks as the real sender, so a bad call fails in the test too.</para>
/// <para><b>Not ported on purpose:</b> faking only some notification types, and global test state.</para>
/// </remarks>
public sealed class NotificationFake : INotificationSender
{
    private readonly object _gate = new();
    private readonly List<(object Recipient, object Notification, string[] Channels)> _sent = new();

    /// <summary>How many notifications were recorded.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _sent.Count;
            }
        }
    }

    /// <inheritdoc />
    public Task SendAsync<TNotification>(object recipient, TNotification notification, IReadOnlyList<string> channels, CancellationToken cancellationToken = default)
    {
        NotificationArguments.Validate(recipient, notification, channels);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _sent.Add((recipient, notification!, channels.ToArray()));
        }

        return Task.CompletedTask;
    }

    /// <summary>Asserts that at least one <typeparamref name="TNotification"/> was sent and returns the first.</summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <exception cref="InvalidOperationException">None was sent.</exception>
    public SentNotification<TNotification> AssertSent<TNotification>()
    {
        var all = Snapshot<TNotification>();
        return all.Count == 0
            ? throw new InvalidOperationException($"No {typeof(TNotification).Name} notification was sent.")
            : all[0];
    }

    /// <summary>Asserts that exactly <paramref name="expectedCount"/> notifications of the type were sent and returns them in order.</summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="expectedCount">The exact number expected.</param>
    /// <exception cref="InvalidOperationException">The count differs.</exception>
    public IReadOnlyList<SentNotification<TNotification>> AssertSent<TNotification>(int expectedCount)
    {
        var all = Snapshot<TNotification>();
        return all.Count == expectedCount
            ? all
            : throw new InvalidOperationException(
                $"Expected {expectedCount} {typeof(TNotification).Name} notifications to be sent, but found {all.Count}.");
    }

    /// <summary>Asserts that no <typeparamref name="TNotification"/> was sent.</summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <exception cref="InvalidOperationException">One or more were sent.</exception>
    public void AssertNotSent<TNotification>()
    {
        var count = Snapshot<TNotification>().Count;
        if (count != 0)
        {
            throw new InvalidOperationException($"Expected no {typeof(TNotification).Name} notification, but {count} were sent.");
        }
    }

    /// <summary>Asserts that nothing at all was sent.</summary>
    /// <exception cref="InvalidOperationException">Something was sent.</exception>
    public void AssertNothingSent()
    {
        var count = Count;
        if (count != 0)
        {
            throw new InvalidOperationException($"Expected no notifications, but {count} were sent.");
        }
    }

    /// <summary>Removes every recorded notification.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _sent.Clear();
        }
    }

    private List<SentNotification<TNotification>> Snapshot<TNotification>()
    {
        lock (_gate)
        {
            var result = new List<SentNotification<TNotification>>();
            foreach (var entry in _sent)
            {
                if (entry.Notification is TNotification typed)
                {
                    result.Add(new SentNotification<TNotification>(entry.Recipient, typed, entry.Channels));
                }
            }

            return result;
        }
    }
}
