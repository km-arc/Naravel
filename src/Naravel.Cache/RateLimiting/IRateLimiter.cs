namespace Naravel.Cache.RateLimiting;

/// <summary>Applies named fixed-window limits to explicit subject keys.</summary>
/// <remarks><b>Laravel equivalent:</b> `Illuminate\Cache\RateLimiter`. It exists for shared Cache-backed quotas; process-local policies should use `System.Threading.RateLimiting`.</remarks>
public interface IRateLimiter
{
    /// <summary>Atomically attempts one use of a named limit for the subject.</summary>
    Task<RateLimitDecision> AttemptAsync(
        string limiter,
        string subjectKey,
        long maxAttempts,
        TimeSpan window,
        CancellationToken cancellationToken = default);

    /// <summary>Clears the current window for one subject and named limit.</summary>
    Task ClearAsync(string limiter, string subjectKey, CancellationToken cancellationToken = default);
}

/// <summary>Result of evaluating one fixed-window rate-limit attempt.</summary>
/// <param name="Allowed">Whether this request was admitted.</param>
/// <param name="Remaining">Number of additional attempts available in the current window.</param>
/// <param name="RetryAfter">Time remaining in the current window.</param>
public readonly record struct RateLimitDecision(bool Allowed, long Remaining, TimeSpan RetryAfter);
