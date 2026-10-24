namespace Naravel.Scheduling;

/// <summary>Settings of the scheduler.</summary>
public sealed class SchedulingOptions
{
    /// <summary>How long a <see cref="ScheduledTask.WithoutOverlapping"/> lock lasts when the run does not end (a crash). Default one hour. Laravel uses 24 hours.</summary>
    public TimeSpan OverlapTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long the per-minute <see cref="ScheduledTask.OnOneServer"/> lock lasts. It is never released early, so it only has to outlive the minute and any clock difference between servers. Default five minutes.</summary>
    public TimeSpan SlotLockTtl { get; set; } = TimeSpan.FromMinutes(5);
}
