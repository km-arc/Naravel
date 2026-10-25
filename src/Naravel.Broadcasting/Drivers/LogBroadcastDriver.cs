using Microsoft.Extensions.Logging;

namespace Naravel.Broadcasting.Drivers;

/// <summary>Logs the event name and the number of channels. Channel names and payloads are not logged: they often embed ids.</summary>
internal sealed class LogBroadcastDriver(ILogger logger) : IBroadcastDriver
{
    public Task SendAsync(BroadcastMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Broadcast {Event} to {ChannelCount} channel(s).", message.Name, message.Channels.Count);
        return Task.CompletedTask;
    }
}
