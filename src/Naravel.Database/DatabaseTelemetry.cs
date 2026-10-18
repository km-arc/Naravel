using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Naravel.Database;

/// <summary>Telemetry names: meter and activity source <c>Naravel.Database</c>. Tags are the connection name and operation kind only, never SQL text or values.</summary>
internal static class DatabaseTelemetry
{
    private static readonly Meter Meter = new("Naravel.Database");

    internal static ActivitySource ActivitySource { get; } = new("Naravel.Database");
    internal static Counter<long> Queries { get; } = Meter.CreateCounter<long>("naravel.database.queries");
    internal static Counter<long> Failures { get; } = Meter.CreateCounter<long>("naravel.database.failures");
    internal static Histogram<double> Duration { get; } = Meter.CreateHistogram<double>("naravel.database.query.duration", "ms");
}
