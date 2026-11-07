using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Naravel.Routing;

internal enum ThrottleAttemptKind
{
    /// <summary>A store (or the degraded local limiter) decided; see <see cref="ThrottleAttempt.Decision"/>.</summary>
    Decided,

    /// <summary>The store could not answer and the policy fails closed: answer 503 after <see cref="ThrottleAttempt.RetryAfter"/>.</summary>
    Unavailable,

    /// <summary>The store could not answer and the policy fails open: admit with no limit and no rate-limit headers.</summary>
    Unlimited
}

internal readonly record struct ThrottleAttempt(ThrottleAttemptKind Kind, ThrottleDecision Decision, TimeSpan RetryAfter)
{
    public static ThrottleAttempt FromDecision(ThrottleDecision decision) => new(ThrottleAttemptKind.Decided, decision, TimeSpan.Zero);

    public static ThrottleAttempt FailedClosed(TimeSpan retryAfter) => new(ThrottleAttemptKind.Unavailable, default, retryAfter);

    public static ThrottleAttempt FailedOpen() => new(ThrottleAttemptKind.Unlimited, default, TimeSpan.Zero);
}

/// <summary>
/// Counts one throttle attempt through the registered <see cref="IThrottleStore"/> and decides what a store failure means
/// (PDR-024): a call that throws or exceeds <see cref="ThrottleOptions.StoreTimeout"/> is a failure and opens the breaker, and the
/// policy's <see cref="ThrottleFailureMode"/> handles the request. A client that went away (the request token is cancelled) is not a
/// failure, and a policy that fails open does not admit it either. The default in-process store cannot fail, so it is called directly: no timeout, no breaker, no extra allocation on the
/// path that exists for a single instance.
/// </summary>
internal sealed class ThrottleGuard : IDisposable
{
    private readonly IThrottleStore _store;
    private readonly bool _guarded;
    private readonly TimeSpan _timeout;
    private readonly ThrottleBreaker _breaker;
    private readonly InProcessThrottleStore _local = new();
    private readonly ILogger _logger;
    private readonly TimeSpan _breakerPeriod;

    public ThrottleGuard(
        IThrottleStore store,
        IOptions<RoutingOptions> options,
        ILogger<ThrottleGuard>? logger = null,
        TimeProvider? time = null)
    {
        _store = store;
        _guarded = store is not InProcessThrottleStore;
        var throttle = options.Value.Throttle;
        _timeout = throttle.StoreTimeout;
        _breakerPeriod = throttle.BreakerPeriod;
        _breaker = new ThrottleBreaker(time ?? TimeProvider.System, _breakerPeriod);
        _logger = logger ?? NullLogger<ThrottleGuard>.Instance;
    }

    public async ValueTask<ThrottleAttempt> AttemptAsync(
        string partitionKey,
        string policyName,
        RoutingOptions.ThrottlePolicy policy,
        CancellationToken cancellationToken)
    {
        if (!_guarded)
        {
            var direct = await _store
                .AttemptAsync(partitionKey, policy.PermitLimit, policy.Window, cancellationToken)
                .ConfigureAwait(false);
            Count(policyName, direct.Allowed ? "allowed" : "rejected");
            return ThrottleAttempt.FromDecision(direct);
        }

        var entry = _breaker.TryEnter();
        if (entry == ThrottleBreaker.Entry.Open)
        {
            return await HandleUnavailableAsync(partitionKey, policyName, policy, cancellationToken).ConfigureAwait(false);
        }

        var probe = entry == ThrottleBreaker.Entry.Probe;
        try
        {
            var decision = await CallStoreAsync(partitionKey, policy, cancellationToken).ConfigureAwait(false);
            _breaker.Succeeded(probe);
            Count(policyName, decision.Allowed ? "allowed" : "rejected");
            return ThrottleAttempt.FromDecision(decision);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client went away. That says nothing about the store.
            _breaker.Abandoned(probe);
            throw;
        }
        catch (Exception exception)
        {
            RoutingTelemetry.ThrottleStoreFailures.Add(
                1,
                new KeyValuePair<string, object?>("policy", policyName),
                new KeyValuePair<string, object?>("reason", exception is TimeoutException ? "timeout" : "error"));
            if (_breaker.Failed(probe))
            {
                _logger.LogWarning(
                    exception,
                    "Naravel.Routing: the throttle store did not answer; it is not called for {Period} and each request follows its policy's failure mode until a probe succeeds.",
                    _breakerPeriod);
            }

            return await HandleUnavailableAsync(partitionKey, policyName, policy, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<ThrottleDecision> CallStoreAsync(
        string partitionKey,
        RoutingOptions.ThrottlePolicy policy,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var attempt = _store.AttemptAsync(partitionKey, policy.PermitLimit, policy.Window, linked.Token).AsTask();
        try
        {
            // WaitAsync times out even for a store that ignores its token; the token is still cancelled so a well-behaved store stops.
            return await attempt.WaitAsync(_timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            // Stop a store that is still running (on a client abort the linked token is already cancelled) and make sure that a late
            // failure of the abandoned call is observed instead of surfacing as an unobserved task exception.
            linked.Cancel();
            _ = attempt.ContinueWith(
                static finished => _ = finished.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    private async ValueTask<ThrottleAttempt> HandleUnavailableAsync(
        string partitionKey,
        string policyName,
        RoutingOptions.ThrottlePolicy policy,
        CancellationToken cancellationToken)
    {
        switch (policy.FailureMode)
        {
            case ThrottleFailureMode.FailClosed:
                Count(policyName, "rejected");
                return ThrottleAttempt.FailedClosed(_breaker.RemainingOpen());
            case ThrottleFailureMode.FailOpen:
                // No store call is made on this path, so nothing here observes the request token (the local limiter that the default
                // mode uses does). A client that has gone is not admitted: the request ends with the cancellation, as it would in a
                // store call that observes the token, and it is not counted as admitted. A handler that does not watch the token
                // would otherwise keep working, and keep its side effects, for a client that is not there.
                cancellationToken.ThrowIfCancellationRequested();
                Count(policyName, "allowed");
                return ThrottleAttempt.FailedOpen();
            default:
                var local = await _local
                    .AttemptAsync(partitionKey, policy.PermitLimit, policy.Window, cancellationToken)
                    .ConfigureAwait(false);
                Count(policyName, "degraded");
                return ThrottleAttempt.FromDecision(local);
        }
    }

    private static void Count(string policyName, string outcome) =>
        RoutingTelemetry.Throttle.Add(
            1,
            new KeyValuePair<string, object?>("policy", policyName),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void Dispose() => _local.Dispose();
}
