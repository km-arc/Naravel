namespace Naravel.Routing;

/// <summary>Where the route <c>throttle</c> middleware counts attempts for one partition.</summary>
/// <remarks>
/// <b>Laravel equivalent:</b> the cache store behind <c>RateLimiter</c> and the <c>throttle</c> middleware. <b>Why it exists:</b>
/// the default store counts in the memory of one process (<c>System.Threading.RateLimiting</c>), so N application instances
/// allow N times the limit; a shared store (for example the Cache-backed package) lets every instance count against one quota
/// without Routing depending on Cache (PDR-024). <b>Not ported:</b> Laravel's <c>RateLimiter::for</c> closures, decay-minute
/// helpers and per-key clearing; a policy here is a name, a permit limit and a window.
/// <para>
/// An implementation must be thread-safe and must count one attempt per call, including the attempt that is rejected. A store
/// that cannot answer should throw: the middleware decides what a failure means for the policy (PDR-024).
/// </para>
/// </remarks>
public interface IThrottleStore
{
    /// <summary>Counts one attempt for <paramref name="partitionKey"/> and says whether it is within the limit.</summary>
    /// <param name="partitionKey">The partition, <c>policy|endpoint|subject</c>; the subject is the authenticated user or the remote address.</param>
    /// <param name="permitLimit">The number of attempts allowed in one <paramref name="window"/>.</param>
    /// <param name="window">The length of the fixed window.</param>
    /// <param name="cancellationToken">Cancelled when the client goes away.</param>
    ValueTask<ThrottleDecision> AttemptAsync(string partitionKey, int permitLimit, TimeSpan window, CancellationToken cancellationToken);
}

/// <summary>The result of counting one throttle attempt.</summary>
/// <param name="Allowed">Whether the attempt is within the limit and the request may continue.</param>
/// <param name="Remaining">The attempts still available in the current window, never negative (the <c>X-RateLimit-Remaining</c> header).</param>
/// <param name="RetryAfter">How long until a rejected caller may try again (the <c>Retry-After</c> header); zero when allowed.</param>
public readonly record struct ThrottleDecision(bool Allowed, int Remaining, TimeSpan RetryAfter);
