namespace Naravel.Broadcasting;

/// <summary>What a driver receives: the client method name, the channels and the payload.</summary>
public sealed class BroadcastMessage
{
    /// <summary>Creates a message.</summary>
    /// <param name="name">The client-side method name.</param>
    /// <param name="channels">The channels, already valid.</param>
    /// <param name="payload">The payload.</param>
    public BroadcastMessage(string name, IReadOnlyList<string> channels, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(payload);
        Name = name;
        Channels = channels;
        Payload = payload;
    }

    /// <summary>The client-side method name.</summary>
    public string Name { get; }

    /// <summary>The channels the message goes to.</summary>
    public IReadOnlyList<string> Channels { get; }

    /// <summary>The payload.</summary>
    public object Payload { get; }

    internal static BroadcastMessage From(IBroadcastable evt)
    {
        var channels = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var channel in evt.Channels() ?? [])
        {
            if (!ChannelNames.IsValid(channel))
            {
                // The name is not echoed: channel names often embed ids.
                throw new ArgumentException($"Channel number {index} of {evt.GetType().Name} is not a valid channel name.", nameof(evt));
            }

            if (seen.Add(channel)) channels.Add(channel);
            index++;
        }

        return new BroadcastMessage(evt.BroadcastAs(), channels, evt.BroadcastWith());
    }
}

internal static class ChannelNames
{
    internal const int MaxLength = 128;

    internal static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength) return false;
        foreach (var c in name)
        {
            var ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '.' or ':' or '-';
            if (!ok) return false;
        }

        return true;
    }

    internal static bool IsProtected(string name) => IsPrivate(name) || IsPresence(name);

    internal static bool IsPrivate(string name) => name.StartsWith("private-", StringComparison.Ordinal);

    internal static bool IsPresence(string name) => name.StartsWith("presence-", StringComparison.Ordinal);
}
