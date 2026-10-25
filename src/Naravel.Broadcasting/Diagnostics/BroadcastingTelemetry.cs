using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Naravel.Broadcasting.Diagnostics;

/// <summary>Meter and activity source <c>Naravel.Broadcasting</c>; tags are the driver name and the outcome only (never channel names, payloads or tokens).</summary>
internal static class BroadcastingTelemetry
{
    private static readonly Meter Meter = new("Naravel.Broadcasting");

    internal static ActivitySource ActivitySource { get; } = new("Naravel.Broadcasting");
    internal static Counter<long> Broadcasts { get; } = Meter.CreateCounter<long>("naravel.broadcasting.broadcasts");
}
