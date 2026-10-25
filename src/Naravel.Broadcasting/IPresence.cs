namespace Naravel.Broadcasting;

/// <summary>Reads who is on a <c>presence-</c> channel, from any instance.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the member list a presence channel gives Echo (<c>here()</c>), readable from server code.</para>
/// <para><b>Why it exists:</b> "who is online" is also a server-side question (a badge, an admin page).</para>
/// </remarks>
public interface IPresence
{
    /// <summary>The current members of a channel, one per user, oldest first. Entries that were not refreshed within <see cref="BroadcastingOptions.PresenceEntryTtl"/> are not included.</summary>
    /// <param name="channel">The channel name, for example <c>presence-room.1</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<PresenceMember>> MembersAsync(string channel, CancellationToken cancellationToken = default);
}
