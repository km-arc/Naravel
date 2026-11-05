using System.Diagnostics.Metrics;

namespace Naravel.Routing;

/// <summary>
/// Telemetry names: meter <c>Naravel.Routing</c>. Tags are the policy name (fixed by the application's configuration) and a small
/// fixed set of outcomes or reasons; never the subject, the address or the partition key.
/// </summary>
internal static class RoutingTelemetry
{
    private static readonly Meter Meter = new("Naravel.Routing");

    /// <summary>One per throttled request. Tags: <c>policy</c>, <c>outcome</c> = <c>allowed</c>, <c>rejected</c> or <c>degraded</c>.</summary>
    internal static Counter<long> Throttle { get; } = Meter.CreateCounter<long>("naravel.routing.throttle");

    /// <summary>One per failed store call. Tags: <c>policy</c>, <c>reason</c> = <c>error</c> or <c>timeout</c>.</summary>
    internal static Counter<long> ThrottleStoreFailures { get; } = Meter.CreateCounter<long>("naravel.routing.throttle.store_failures");
}
