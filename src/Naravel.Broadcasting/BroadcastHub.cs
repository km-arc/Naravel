using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Naravel.Broadcasting;

/// <summary>
/// The one SignalR hub clients connect to. A client calls <c>Subscribe(channel)</c> and <c>Unsubscribe(channel)</c>.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the <c>/broadcasting/auth</c> endpoint plus the client's <c>Echo.channel()</c>, <c>Echo.private()</c> and <c>Echo.join()</c>.</para>
/// <para><b>Why it exists:</b> joining a SignalR group is not authorized by default. Public channels are open; <c>private-</c> and <c>presence-</c> channels need an authenticated user and a matching <see cref="ChannelRules"/> rule, and are refused when no rule matches or a rule throws.</para>
/// <para><b>Presence:</b> <c>Subscribe</c> on a <c>presence-</c> channel returns the current members (Echo's <c>here</c>) and the other members of the channel receive <c>MemberJoined</c> when a user first appears and <c>MemberLeft</c> when the last connection of a user leaves, each with <c>{ channel, member }</c>. The list is kept in the shared cache, so it is the same on every instance.</para>
/// </remarks>
public sealed class BroadcastHub(IOptionsMonitor<BroadcastingOptions> options, ILogger<BroadcastHub> logger, IServiceProvider services) : Hub
{
    /// <summary>Joins a channel.</summary>
    /// <param name="channel">The channel name.</param>
    /// <returns>The members of a <c>presence-</c> channel including the caller; an empty list for any other channel.</returns>
    /// <exception cref="HubException">The name is invalid or the subscription is not authorized.</exception>
    public async Task<IReadOnlyList<PresenceMember>> Subscribe(string channel)
    {
        if (!ChannelNames.IsValid(channel)) throw Denied();

        PresenceMember? member = null;
        if (ChannelNames.IsProtected(channel))
        {
            var user = Context.User;
            if (user?.Identity?.IsAuthenticated != true) throw Denied();

            ChannelAuthorization authorization;
            try
            {
                authorization = await options.CurrentValue.Channels.AuthorizeAsync(channel, user, Context.ConnectionAborted).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The channel and the claims are not logged: they identify people.
                logger.LogWarning(ex, "A channel authorization rule threw; the subscription was denied.");
                authorization = default;
            }

            if (!authorization.Allowed) throw Denied();
            member = authorization.Member;
            if (ChannelNames.IsPresence(channel) && member is null) throw Denied();
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, channel, Context.ConnectionAborted).ConfigureAwait(false);
        if (member is null) return [];

        try
        {
            var presence = services.GetRequiredService<PresenceService>();
            var (isNew, members) = await presence.JoinAsync(channel, Context.ConnectionId, member, Context.ConnectionAborted).ConfigureAwait(false);
            if (isNew)
            {
                await Clients.GroupExcept(channel, [Context.ConnectionId])
                    .SendAsync("MemberJoined", new { channel, member }, Context.ConnectionAborted).ConfigureAwait(false);
            }

            return members;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The presence list could not be updated; the subscription was undone.");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, channel, CancellationToken.None).ConfigureAwait(false);
            throw new HubException("Presence is unavailable.");
        }
    }

    /// <summary>Leaves a channel. Leaving a channel that was never joined is not an error.</summary>
    /// <param name="channel">The channel name.</param>
    public async Task Unsubscribe(string channel)
    {
        if (!ChannelNames.IsValid(channel)) throw Denied();

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, channel, Context.ConnectionAborted).ConfigureAwait(false);
        if (!ChannelNames.IsPresence(channel)) return;

        var left = await services.GetRequiredService<PresenceService>().LeaveAsync(channel, Context.ConnectionId, Context.ConnectionAborted).ConfigureAwait(false);
        if (left is not null)
        {
            await Clients.Group(channel).SendAsync("MemberLeft", new { channel, member = left }, Context.ConnectionAborted).ConfigureAwait(false);
        }
    }

    /// <summary>Removes the connection from every presence channel it joined.</summary>
    /// <param name="exception">Why the connection closed, if it did not close cleanly.</param>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var departures = await services.GetRequiredService<PresenceService>().LeaveAllAsync(Context.ConnectionId, CancellationToken.None).ConfigureAwait(false);
            foreach (var (channel, left) in departures)
            {
                if (left is null) continue;
                await Clients.Group(channel).SendAsync("MemberLeft", new { channel, member = left }).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // An entry that is not removed here expires on its own after PresenceEntryTtl.
            logger.LogWarning(ex, "Removing a closed connection from the presence lists failed.");
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    private static HubException Denied() => new("Subscription denied.");
}
