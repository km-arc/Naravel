using Microsoft.AspNetCore.SignalR;

namespace Naravel.Broadcasting.Drivers;

/// <summary>Sends to the SignalR group named like the channel; clients join it through <see cref="BroadcastHub"/>.</summary>
internal sealed class SignalRBroadcastDriver(IHubContext<BroadcastHub> hub) : IBroadcastDriver
{
    public async Task SendAsync(BroadcastMessage message, CancellationToken cancellationToken = default)
    {
        foreach (var channel in message.Channels)
        {
            await hub.Clients.Group(channel).SendAsync(message.Name, message.Payload, cancellationToken).ConfigureAwait(false);
        }
    }
}
