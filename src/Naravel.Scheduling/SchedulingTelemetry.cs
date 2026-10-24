using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Naravel.Scheduling;

internal static class SchedulingTelemetry
{
    internal static readonly Meter Meter = new("Naravel.Scheduling");
    internal static readonly ActivitySource ActivitySource = new("Naravel.Scheduling");

    /// <summary>Runs by outcome. Tags: <c>task</c> (a name from code) and <c>outcome</c> (one of the constants below).</summary>
    internal static readonly Counter<long> Runs = Meter.CreateCounter<long>("naravel.scheduling.task.runs");

    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("naravel.scheduling.task.duration", "ms");

    internal const string Succeeded = "succeeded";
    internal const string Failed = "failed";
    internal const string Cancelled = "cancelled";
    internal const string SkippedOverlap = "skipped_overlap";
    internal const string SkippedOtherServer = "skipped_other_server";
}
