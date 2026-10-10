using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Naravel.Events;

internal static class EventTelemetry
{
    private static readonly Meter Meter = new("Naravel.Events");

    public static ActivitySource ActivitySource { get; } = new("Naravel.Events");
    public static Counter<long> Dispatched { get; } = Meter.CreateCounter<long>("naravel.events.dispatched");
    public static Counter<long> Failed { get; } = Meter.CreateCounter<long>("naravel.events.failed");
    public static Histogram<double> DispatchDuration { get; } = Meter.CreateHistogram<double>("naravel.events.dispatch.duration", "ms");
}
