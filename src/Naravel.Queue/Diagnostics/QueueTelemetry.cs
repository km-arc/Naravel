using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Naravel.Queue.Diagnostics;

internal static class QueueTelemetry
{
    internal static readonly Meter Meter = new("Naravel.Queue");
    internal static readonly ActivitySource ActivitySource = new("Naravel.Queue");
    internal static readonly Counter<long> Processed = Meter.CreateCounter<long>("naravel.queue.processed");
    internal static readonly Counter<long> Failed = Meter.CreateCounter<long>("naravel.queue.failed");
    internal static readonly Counter<long> Retried = Meter.CreateCounter<long>("naravel.queue.retried");
    internal static readonly Histogram<double> ProcessingDuration = Meter.CreateHistogram<double>("naravel.queue.processing.duration", "ms");
}