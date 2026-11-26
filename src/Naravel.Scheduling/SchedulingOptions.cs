namespace Naravel.Scheduling;

/// <summary>Settings of the scheduler.</summary>
public sealed class SchedulingOptions
{
    /// <summary>The longest a <see cref="ScheduledTask.WithoutOverlapping"/> lock may be held. The lock keeps a lease of at most a minute that is renewed while the task runs, so a crash frees it within about a minute (a driver that cannot renew, Memcached, keeps one fixed lease of this length). Default one hour. Laravel uses 24 hours.</summary>
    public TimeSpan OverlapTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long the per-minute <see cref="ScheduledTask.OnOneServer"/> lock lasts. It is never released early, so it only has to outlive the minute and any clock difference between servers. Default five minutes.</summary>
    public TimeSpan SlotLockTtl { get; set; } = TimeSpan.FromMinutes(5);
}
