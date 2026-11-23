namespace Naravel.Cache;

/// <summary>How long a lock lease lasts, how often it is renewed, and the longest time it may be held.</summary>
/// <remarks>
/// <para>The maximum hold time is not optional. Renewal that never ends would turn a holder that hangs into a lock that never ends; with <see cref="MaxHold"/> the lock is lost at the latest after that time, and the lease is never renewed to
/// last beyond it, so it also ends by itself if the holder's process dies.</para>
/// <para>A driver that cannot refresh a lease (the Memcached lock) gets one fixed lease of <see cref="MaxHold"/> and no renewal; <see cref="CacheLockLease.Renews"/> tells which case applies.</para>
/// </remarks>
public sealed class CacheLockLeaseOptions
{
    private readonly TimeSpan? _renewInterval;

    /// <summary>Creates the options.</summary>
    /// <param name="ttl">The length of each lease. The lease is set to this length again at every renewal. It must be greater than zero.</param>
    /// <param name="maxHold">The longest time the lock may be held, counted from the start of the successful acquisition. It must not be less than <paramref name="ttl"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ttl"/> is zero or less, or <paramref name="maxHold"/> is less than <paramref name="ttl"/>.</exception>
    public CacheLockLeaseOptions(TimeSpan ttl, TimeSpan maxHold)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ttl.Ticks, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHold, ttl);
        Ttl = ttl;
        MaxHold = maxHold;
    }

    /// <summary>The length of each lease.</summary>
    public TimeSpan Ttl { get; }

    /// <summary>The longest time the lock may be held.</summary>
    public TimeSpan MaxHold { get; }

    /// <summary>The time between two renewals. The default is a third of <see cref="Ttl"/>. It must be greater than zero and less than <see cref="Ttl"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less, or not less than <see cref="Ttl"/>.</exception>
    public TimeSpan? RenewInterval
    {
        get => _renewInterval;
        init
        {
            if (value is { } interval)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(interval, Ttl);
            }

            _renewInterval = value;
        }
    }

    /// <summary>The clock that times the lease. The default is <c>TimeProvider.System</c>. A test passes its own so that the lease runs without waiting.</summary>
    /// <remarks>The lease measures its time with the monotonic timestamp of this clock (<c>GetTimestamp</c>), never with the wall clock, so a change of the system time cannot lengthen or shorten a lease.</remarks>
    public TimeProvider? TimeProvider { get; init; }

    internal TimeSpan ResolvedRenewInterval
    {
        get
        {
            // Keep the default strictly below Ttl, including sub-millisecond leases.
            var interval = _renewInterval ?? TimeSpan.FromTicks(Math.Max(1, Ttl.Ticks / 3));
            return interval;
        }
    }
}
