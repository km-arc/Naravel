namespace Naravel.Queue.Coordination;

/// <summary>Cache key names used by the coordination middleware. Every key is caller-supplied text under one prefix; nothing is derived from a CLR type name (OD-07).</summary>
internal static class CoordinationKeys
{
    private const string Prefix = "naravel:queue:";

    public static string Overlap(string key) => $"{Prefix}overlap:{key}";

    public static string Limiter(string limiter) => $"{Prefix}limiter:{limiter}";

    public static string ThrottleCount(string key) => $"{Prefix}throttle:{key}:count";

    public static string ThrottleOpen(string key) => $"{Prefix}throttle:{key}:open";
}
