using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Naravel.Broadcasting;

/// <summary>Refreshes the presence entries of this instance's connections every third of the entry lifetime.</summary>
internal sealed class PresenceHeartbeatService(
    PresenceService presence,
    IOptionsMonitor<BroadcastingOptions> options,
    TimeProvider time,
    ILogger<PresenceHeartbeatService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ttl = options.CurrentValue.PresenceEntryTtl;
        if (ttl < TimeSpan.FromSeconds(3)) ttl = TimeSpan.FromSeconds(3);
        using var timer = new PeriodicTimer(ttl / 3, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await presence.RefreshAllAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Refreshing the presence entries failed; the next tick tries again.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
