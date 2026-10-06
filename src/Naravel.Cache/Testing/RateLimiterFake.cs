using System.Collections.Concurrent;
using Naravel.Cache.RateLimiting;

namespace Naravel.Cache.Testing;

/// <summary>In-memory fixed-window rate-limiter fake for application tests.</summary>
/// <remarks><b>Laravel equivalent:</b> rate-limiter tests backed by Cache fakes. It provides deterministic state inspection without Cache, lock drivers, or a distributed backend.</remarks>
public sealed class RateLimiterFake : IRateLimiter
{
    private sealed record Window(long Count, DateTimeOffset ExpiresAt);
    private readonly object _gate = new();
    private readonly Dictionary<(string Limiter, string Subject), Window> _windows = new();
    private readonly ConcurrentQueue<(string Limiter, string Subject)> _attempts = new();
    private readonly TimeProvider _timeProvider;

    public RateLimiterFake(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;

    public IReadOnlyList<(string Limiter, string Subject)> Attempts => _attempts.ToArray();

    public Task<RateLimitDecision> AttemptAsync(
        string limiter,
        string subjectKey,
        long maxAttempts,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(limiter);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        _attempts.Enqueue((limiter, subjectKey));

        lock (_gate)
        {
            var key = (limiter, subjectKey);
            var now = _timeProvider.GetUtcNow();
            if (!_windows.TryGetValue(key, out var state) || state.ExpiresAt <= now)
                state = new Window(0, now + window);

            var retryAfter = state.ExpiresAt - now;
            if (state.Count >= maxAttempts)
            {
                _windows[key] = state;
                return Task.FromResult(new RateLimitDecision(false, 0, retryAfter));
            }

            state = state with { Count = state.Count + 1 };
            _windows[key] = state;
            return Task.FromResult(new RateLimitDecision(true, maxAttempts - state.Count, retryAfter));
        }
    }

    public Task ClearAsync(string limiter, string subjectKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) _windows.Remove((limiter, subjectKey));
        return Task.CompletedTask;
    }

    public long GetAttemptCount(string limiter, string subjectKey)
    {
        lock (_gate)
            return _windows.TryGetValue((limiter, subjectKey), out var state) && state.ExpiresAt > _timeProvider.GetUtcNow()
                ? state.Count
                : 0;
    }

    public void AssertAttempted(string limiter, string subjectKey)
    {
        if (!_attempts.Contains((limiter, subjectKey)))
            throw new InvalidOperationException($"No rate-limit attempt was recorded for '{limiter}'.");
    }

    public void Reset()
    {
        lock (_gate) _windows.Clear();
        while (_attempts.TryDequeue(out _)) { }
    }
}
