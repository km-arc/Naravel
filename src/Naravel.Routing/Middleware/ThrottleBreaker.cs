namespace Naravel.Routing;

/// <summary>
/// The circuit breaker around one throttle store (PDR-024). <b>Closed</b>: calls go to the store. After a failure it is
/// <b>open</b> for the break period: the store is not called. When the period has passed it is <b>half-open</b>: exactly one
/// request (the probe) calls the store while the others are still handled as in open; a successful probe closes it, a failed probe
/// opens it for another period. Time comes from the <see cref="TimeProvider"/>, so tests move it without waiting.
/// </summary>
internal sealed class ThrottleBreaker(TimeProvider time, TimeSpan period)
{
    internal enum Entry
    {
        /// <summary>Call the store.</summary>
        Closed,

        /// <summary>Call the store; this request is the single probe of a half-open breaker.</summary>
        Probe,

        /// <summary>Do not call the store.</summary>
        Open
    }

    private readonly object _gate = new();
    private bool _open;
    private bool _probing;
    private DateTimeOffset _openUntil;

    public Entry TryEnter()
    {
        lock (_gate)
        {
            if (!_open) return Entry.Closed;
            if (time.GetUtcNow() < _openUntil) return Entry.Open;
            if (_probing) return Entry.Open;
            _probing = true;
            return Entry.Probe;
        }
    }

    /// <summary>A store call answered. Only the probe closes the breaker; a late answer of a call made before it opened does not.</summary>
    public void Succeeded(bool probe)
    {
        if (!probe) return;
        lock (_gate)
        {
            _open = false;
            _probing = false;
        }
    }

    /// <summary>A store call failed. Returns true when this failure opened the breaker (so it is logged once per period).</summary>
    public bool Failed(bool probe)
    {
        lock (_gate)
        {
            if (probe)
            {
                _probing = false;
                Open();
                return true;
            }

            // Calls that were already running when another one failed do not extend the period or log again.
            if (_open) return false;
            Open();
            return true;
        }
    }

    /// <summary>The client went away during the probe: not a failure, so the next request may probe.</summary>
    public void Abandoned(bool probe)
    {
        if (!probe) return;
        lock (_gate) _probing = false;
    }

    /// <summary>How long until the breaker is no longer open; zero when closed, half-open or past the period.</summary>
    public TimeSpan RemainingOpen()
    {
        lock (_gate)
        {
            if (!_open) return TimeSpan.Zero;
            var left = _openUntil - time.GetUtcNow();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    private void Open()
    {
        _open = true;
        _openUntil = time.GetUtcNow() + period;
    }
}
