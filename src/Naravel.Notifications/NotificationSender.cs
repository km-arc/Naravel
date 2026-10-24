using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Naravel.Notifications;

internal sealed class NotificationSender(IServiceProvider provider) : INotificationSender
{
    public async Task SendAsync<TNotification>(object recipient, TNotification notification, IReadOnlyList<string> channels, CancellationToken cancellationToken = default)
    {
        NotificationArguments.Validate(recipient, notification, channels);
        cancellationToken.ThrowIfCancellationRequested();

        // Resolve every requested channel before the first delivery: a typo must not cause a partial delivery.
        var resolved = Resolve<TNotification>(channels);

        using var activity = NotificationTelemetry.ActivitySource.StartActivity("notifications.send", ActivityKind.Internal);
        var startedAt = Stopwatch.GetTimestamp();
        var delivered = new List<string>(resolved.Length);
        try
        {
            foreach (var channel in resolved)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await channel.SendAsync(new NotificationContext(recipient, channel.Name), notification, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // The failure is rethrown wrapped, together with what had already been delivered.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    NotificationTelemetry.ChannelDeliveries.Add(
                        1,
                        new KeyValuePair<string, object?>("channel", channel.Name),
                        new KeyValuePair<string, object?>("outcome", "failed"));
                    activity?.SetStatus(ActivityStatusCode.Error);
                    throw new NotificationDeliveryException(channel.Name, delivered.ToArray(), exception);
                }

                delivered.Add(channel.Name);
                NotificationTelemetry.ChannelDeliveries.Add(
                    1,
                    new KeyValuePair<string, object?>("channel", channel.Name),
                    new KeyValuePair<string, object?>("outcome", "sent"));
            }

            NotificationTelemetry.Dispatched.Add(1);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        finally
        {
            NotificationTelemetry.DispatchDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private INotificationChannel<TNotification>[] Resolve<TNotification>(IReadOnlyList<string> names)
    {
        var registered = provider.GetServices<INotificationChannel<TNotification>>().ToArray();
        var result = new INotificationChannel<TNotification>[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            INotificationChannel<TNotification>? found = null;
            foreach (var channel in registered)
            {
                if (!string.Equals(channel.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (found is not null)
                {
                    throw new InvalidOperationException(
                        $"The channel '{name}' is registered more than once for {typeof(TNotification).Name}; channel names must be unique per notification type.");
                }

                found = channel;
            }

            result[i] = found ?? throw new InvalidOperationException(
                $"No notification channel '{name}' is registered for {typeof(TNotification).Name}. Registered: "
                + (registered.Length == 0 ? "none" : string.Join(", ", registered.Select(c => c.Name))) + ".");
        }

        return result;
    }
}
