namespace Naravel.Notifications.Queue;

internal sealed record QueuedNotificationEntry(
    string Alias,
    Type PayloadType,
    Func<string, IServiceProvider, CancellationToken, Task> Run);

/// <summary>The explicit alias registry (OD-07): a stored alias maps to a handler registered in code, never to a CLR type found by name.</summary>
internal sealed class QueuedNotificationRegistry
{
    private readonly Dictionary<string, QueuedNotificationEntry> _entries = new(StringComparer.Ordinal);

    public QueuedNotificationRegistry(IEnumerable<QueuedNotificationEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (!_entries.TryAdd(entry.Alias, entry))
            {
                throw new InvalidOperationException($"The queued notification alias '{entry.Alias}' is registered more than once.");
            }
        }
    }

    public QueuedNotificationEntry Get(string alias) =>
        _entries.TryGetValue(alias, out var entry)
            ? entry
            : throw new InvalidOperationException(
                $"No queued notification is registered under the alias '{alias}'. Register it with AddQueuedNotification.");
}
