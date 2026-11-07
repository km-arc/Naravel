namespace Naravel.Routing;

/// <summary>What the <c>throttle</c> middleware does for a policy when its <see cref="IThrottleStore"/> cannot answer.</summary>
/// <remarks>
/// <b>Laravel equivalent:</b> none; Laravel lets the cache exception escape, which surfaces as a 500 (PDR-024). <b>Why it exists:</b>
/// a throttle sits on the request path, so an outage of the shared backend must not take the application down, and must not
/// silently remove the limit either. The mode is chosen per policy (<see cref="RoutingOptions.ConfigureThrottlePolicy(string, int, TimeSpan, ThrottleFailureMode)"/>).
/// It applies only to a store you registered: the default in-process store cannot fail.
/// </remarks>
public enum ThrottleFailureMode
{
    /// <summary>
    /// Apply this instance's in-process limiter with the policy's numbers for that request (the default). A backend outage keeps the
    /// per-instance protection the product had before a shared store, and is never unlimited.
    /// </summary>
    DegradeToLocal = 0,

    /// <summary>
    /// Answer 503 with <c>Retry-After</c> set to the time the breaker stays open (at least one second) and no <c>X-RateLimit-*</c>
    /// headers. Opt in for credential endpoints, where admitting more attempts than the shared limit is worse than refusing.
    /// </summary>
    FailClosed = 1,

    /// <summary>
    /// Admit the request without any limit. An explicit opt-in only; it removes the limit for as long as the store is down. A request
    /// whose client has already gone (its token is cancelled) is not admitted: it ends with the cancellation.
    /// </summary>
    FailOpen = 2
}

/// <summary>Timing of the guard around a registered <see cref="IThrottleStore"/>: how long one call may take and how long a failure is remembered.</summary>
/// <remarks>
/// <b>Laravel equivalent:</b> none. <b>Why it exists:</b> a throttle must not make a slow backend slow for everyone (PDR-024). A call
/// that throws or exceeds <see cref="StoreTimeout"/> is a store failure and opens one breaker shared by every policy using the store.
/// A request whose client went away is not a failure. Read once at startup; there is no hot reload (like the rest of
/// <see cref="RoutingOptions"/>).
/// </remarks>
public sealed class ThrottleOptions
{
    private TimeSpan _storeTimeout = TimeSpan.FromMilliseconds(250);
    private TimeSpan _breakerPeriod = TimeSpan.FromSeconds(5);

    /// <summary>The longest one store call may take before it counts as a failure. Default 250 milliseconds; must be positive.</summary>
    public TimeSpan StoreTimeout
    {
        get => _storeTimeout;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _storeTimeout = value;
        }
    }

    /// <summary>
    /// How long the store is not called after a failure; every request in that time is handled by its policy's
    /// <see cref="ThrottleFailureMode"/>. Then one request probes the store. Default 5 seconds; must be positive.
    /// </summary>
    public TimeSpan BreakerPeriod
    {
        get => _breakerPeriod;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _breakerPeriod = value;
        }
    }
}
