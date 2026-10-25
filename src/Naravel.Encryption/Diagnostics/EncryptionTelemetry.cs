using System.Diagnostics.Metrics;

namespace Naravel.Encryption;

internal static class EncryptionTelemetry
{
    private static readonly Meter Meter = new("Naravel.Encryption");

    // Tags are bounded: the operation ("encrypt" or "decrypt") and the outcome ("ok" or "invalid"). Never a value, a payload, a purpose or key data.
    private static readonly Counter<long> Operations = Meter.CreateCounter<long>("naravel.encryption.operations");

    public static void Record(string operation, string outcome) =>
        Operations.Add(1, new KeyValuePair<string, object?>("operation", operation), new KeyValuePair<string, object?>("outcome", outcome));
}
