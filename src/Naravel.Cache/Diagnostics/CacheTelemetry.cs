using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Naravel.Cache.Diagnostics;

internal static class CacheTelemetry
{
    internal static readonly Meter Meter = new("Naravel.Cache");
    internal static readonly ActivitySource ActivitySource = new("Naravel.Cache");
    internal static readonly Counter<long> Allowed = Meter.CreateCounter<long>("naravel.cache.ratelimiter.allowed");
    internal static readonly Counter<long> Rejected = Meter.CreateCounter<long>("naravel.cache.ratelimiter.rejected");
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("naravel.cache.ratelimiter.duration", "ms");
}
