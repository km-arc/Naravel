namespace Naravel.Notifications;

internal static class NotificationArguments
{
    public static void Validate(object recipient, object? notification, IReadOnlyList<string> channels)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(channels);

        if (channels.Count == 0)
        {
            throw new ArgumentException("At least one channel name is required.", nameof(channels));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in channels)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A channel name must not be blank.", nameof(channels));
            }

            if (!seen.Add(name))
            {
                throw new ArgumentException($"The channel '{name}' is listed more than once.", nameof(channels));
            }
        }
    }
}
