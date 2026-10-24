using Cronos;

namespace Naravel.Scheduling;

/// <summary>When a task is due, at minute resolution. Internal: callers use the fluent methods of <see cref="ScheduledTask"/>.</summary>
internal abstract class SchedulePattern
{
    /// <summary>A short text for listings: a cron expression or <c>every N minutes</c>.</summary>
    public abstract string Description { get; }

    /// <summary>Whether the task is due in the given minute.</summary>
    public abstract bool IsDue(DateTimeOffset minuteUtc, TimeZoneInfo zone);

    /// <summary>The first due minute strictly after the given time, or null when there is none.</summary>
    public abstract DateTimeOffset? NextAfter(DateTimeOffset fromUtc, TimeZoneInfo zone);

    /// <summary>Cuts a time down to the start of its minute, in UTC.</summary>
    public static DateTimeOffset Minute(DateTimeOffset time)
    {
        var ticks = time.UtcTicks;
        return new DateTimeOffset(ticks - (ticks % TimeSpan.TicksPerMinute), TimeSpan.Zero);
    }
}

/// <summary>A five-field cron expression, parsed by Cronos, which also handles time zones and daylight-saving changes.</summary>
internal sealed class CronPattern : SchedulePattern
{
    private readonly CronExpression _expression;

    public CronPattern(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        try
        {
            _expression = CronExpression.Parse(expression, CronFormat.Standard);
        }
        catch (CronFormatException exception)
        {
            throw new ArgumentException("Not a valid five-field cron expression (minute hour day-of-month month day-of-week): " + exception.Message, nameof(expression), exception);
        }

        Description = expression.Trim();
    }

    public override string Description { get; }

    public override bool IsDue(DateTimeOffset minuteUtc, TimeZoneInfo zone)
    {
        var minute = Minute(minuteUtc);
        var next = _expression.GetNextOccurrence(minute, zone, inclusive: true);
        return next is not null && next.Value.UtcTicks == minute.UtcTicks;
    }

    public override DateTimeOffset? NextAfter(DateTimeOffset fromUtc, TimeZoneInfo zone)
        => _expression.GetNextOccurrence(Minute(fromUtc), zone, inclusive: false);
}

/// <summary>Every N whole minutes, counted from the Unix epoch, so every instance agrees on the same minutes.</summary>
internal sealed class IntervalPattern : SchedulePattern
{
    private readonly long _minutes;

    public IntervalPattern(TimeSpan interval)
    {
        if (interval < TimeSpan.FromMinutes(1) || interval.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "The interval must be a whole number of minutes, at least one.");
        }

        _minutes = interval.Ticks / TimeSpan.TicksPerMinute;
        Description = _minutes == 1 ? "every minute" : $"every {_minutes} minutes";
    }

    public override string Description { get; }

    public override bool IsDue(DateTimeOffset minuteUtc, TimeZoneInfo zone) => MinuteIndex(Minute(minuteUtc)) % _minutes == 0;

    public override DateTimeOffset? NextAfter(DateTimeOffset fromUtc, TimeZoneInfo zone)
    {
        var next = (MinuteIndex(Minute(fromUtc)) / _minutes + 1) * _minutes;
        return DateTimeOffset.FromUnixTimeSeconds(next * 60);
    }

    private static long MinuteIndex(DateTimeOffset minute) => minute.ToUnixTimeSeconds() / 60;
}
