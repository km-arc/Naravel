using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Naravel.Mail.Diagnostics;

/// <summary>Telemetry names: meter and activity source <c>Naravel.Mail</c>; tags are the transport name only (never recipients or content).</summary>
internal static class MailTelemetry
{
    private static readonly Meter Meter = new("Naravel.Mail");

    internal static ActivitySource ActivitySource { get; } = new("Naravel.Mail");
    internal static Counter<long> Sent { get; } = Meter.CreateCounter<long>("naravel.mail.sent");
    internal static Counter<long> Failed { get; } = Meter.CreateCounter<long>("naravel.mail.failed");
}
