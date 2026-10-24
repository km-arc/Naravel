using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Naravel.Notifications;

internal static class NotificationTelemetry
{
    private static readonly Meter Meter = new("Naravel.Notifications");

    public static ActivitySource ActivitySource { get; } = new("Naravel.Notifications");

    public static Counter<long> Dispatched { get; } = Meter.CreateCounter<long>("naravel.notifications.dispatched");

    // Tags are bounded: the channel name chosen by the application and an outcome of "sent" or "failed". No recipient or payload is ever tagged.
    public static Counter<long> ChannelDeliveries { get; } = Meter.CreateCounter<long>("naravel.notifications.channel.deliveries");

    public static Histogram<double> DispatchDuration { get; } = Meter.CreateHistogram<double>("naravel.notifications.dispatch.duration", "ms");
}
