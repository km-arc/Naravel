using Naravel.Foundation;

namespace Naravel.Broadcasting;

/// <summary>
/// Configuration of the broadcasting module: the default driver, the named drivers and the channel rules.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>config/broadcasting.php</c> (<c>default</c>, <c>connections</c>) and <c>routes/channels.php</c>.</para>
/// <para><b>Why it exists:</b> binds <c>Broadcasting:Default</c> and <c>Broadcasting:Stores:&lt;name&gt;:Driver</c> for the shared <c>Manager</c>; <see cref="Channels"/> is set in code because rules are delegates.</para>
/// </remarks>
public sealed class BroadcastingOptions : ManagerOptions
{
    /// <summary>Creates options whose default driver is <c>signalr</c>.</summary>
    public BroadcastingOptions() => Default = "signalr";

    /// <summary>The authorization rules for <c>private-</c> and <c>presence-</c> channels.</summary>
    public ChannelRules Channels { get; } = new();

    /// <summary>
    /// How long a presence entry lives without being refreshed (default 90 seconds, at least 3). Each instance refreshes the entries of its own connections every third of this time, so the entry of a connection whose instance crashed disappears after at most this long.
    /// </summary>
    public TimeSpan PresenceEntryTtl { get; set; } = TimeSpan.FromSeconds(90);
}
