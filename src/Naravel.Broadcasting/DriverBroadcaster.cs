using System.Diagnostics;
using Naravel.Broadcasting.Diagnostics;

namespace Naravel.Broadcasting;

internal sealed class DriverBroadcaster(BroadcastManager manager, string? name) : IBroadcaster
{
    public async Task BroadcastAsync(IBroadcastable evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        cancellationToken.ThrowIfCancellationRequested();

        var message = BroadcastMessage.From(evt);
        if (message.Channels.Count == 0) return;

        var driverName = name ?? manager.DefaultDriverName;
        using var activity = BroadcastingTelemetry.ActivitySource.StartActivity("naravel.broadcasting.broadcast");
        activity?.SetTag("driver", driverName);
        var outcome = "succeeded";
        try
        {
            var driver = await manager.DriverAsync(name, cancellationToken).ConfigureAwait(false);
            await driver.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            throw;
        }
        catch
        {
            outcome = "failed";
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            BroadcastingTelemetry.Broadcasts.Add(
                1,
                new KeyValuePair<string, object?>("driver", driverName),
                new KeyValuePair<string, object?>("outcome", outcome));
        }
    }
}
