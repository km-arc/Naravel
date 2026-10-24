namespace Naravel.Scheduling;

/// <summary>One named task of a <see cref="Schedule"/>: what to run, when it is due and which guards apply.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the <c>Event</c> returned by <c>$schedule-&gt;call()</c>, with its frequency, filter and guard methods.</para>
/// <para>A task runs every minute until a frequency is chosen (as in Laravel). Calling a frequency method again replaces the earlier one. Times are read in UTC unless <see cref="InTimeZone"/> is set.</para>
/// </remarks>
public sealed class ScheduledTask
{
    private readonly List<Func<bool>> _when = [];
    private readonly List<Func<bool>> _skip = [];
    private readonly List<Func<Exception, Task>> _onFailure = [];
    private readonly List<Func<Task>> _onSuccess = [];
    private bool _weekdays;
    private (TimeOnly Start, TimeOnly End)? _between;

    internal ScheduledTask(string name, Func<IServiceProvider, CancellationToken, Task> handler)
    {
        Name = name;
        Handler = handler;
    }

    /// <summary>The unique name: the lock key and the metric tag of the task.</summary>
    public string Name { get; }

    /// <summary>The frequency as text: a cron expression, or <c>every N minutes</c>.</summary>
    public string Expression => Pattern.Description;

    internal Func<IServiceProvider, CancellationToken, Task> Handler { get; }
    internal SchedulePattern Pattern { get; private set; } = new IntervalPattern(TimeSpan.FromMinutes(1));
    internal TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.Utc;
    internal bool PreventOverlap { get; private set; }
    internal TimeSpan? OverlapTtl { get; private set; }
    internal bool SingleServer { get; private set; }
    internal IReadOnlyList<Func<Exception, Task>> FailureHooks => _onFailure;
    internal IReadOnlyList<Func<Task>> SuccessHooks => _onSuccess;

    /// <summary>Every minute. Laravel: <c>everyMinute</c>.</summary>
    public ScheduledTask EveryMinute() => Use(new IntervalPattern(TimeSpan.FromMinutes(1)));

    /// <summary>Every five minutes. Laravel: <c>everyFiveMinutes</c>.</summary>
    public ScheduledTask EveryFiveMinutes() => Use(new IntervalPattern(TimeSpan.FromMinutes(5)));

    /// <summary>Every ten minutes. Laravel: <c>everyTenMinutes</c>.</summary>
    public ScheduledTask EveryTenMinutes() => Use(new IntervalPattern(TimeSpan.FromMinutes(10)));

    /// <summary>Every fifteen minutes. Laravel: <c>everyFifteenMinutes</c>.</summary>
    public ScheduledTask EveryFifteenMinutes() => Use(new IntervalPattern(TimeSpan.FromMinutes(15)));

    /// <summary>Every thirty minutes. Laravel: <c>everyThirtyMinutes</c>.</summary>
    public ScheduledTask EveryThirtyMinutes() => Use(new IntervalPattern(TimeSpan.FromMinutes(30)));

    /// <summary>Every whole number of minutes (at least one), counted from the Unix epoch so every instance agrees. Laravel has no direct equivalent.</summary>
    public ScheduledTask Every(TimeSpan interval) => Use(new IntervalPattern(interval));

    /// <summary>At the start of every hour. Laravel: <c>hourly</c>.</summary>
    public ScheduledTask Hourly() => Use(new CronPattern("0 * * * *"));

    /// <summary>At the given minute of every hour. Laravel: <c>hourlyAt</c>.</summary>
    public ScheduledTask HourlyAt(int minute) => Use(new CronPattern($"{CheckMinute(minute)} * * * *"));

    /// <summary>At midnight every day. Laravel: <c>daily</c>.</summary>
    public ScheduledTask Daily() => Use(new CronPattern("0 0 * * *"));

    /// <summary>At the given time every day. Laravel: <c>dailyAt</c>.</summary>
    public ScheduledTask DailyAt(int hour, int minute) => Use(new CronPattern($"{CheckMinute(minute)} {CheckHour(hour)} * * *"));

    /// <summary>At midnight every Sunday. Laravel: <c>weekly</c>.</summary>
    public ScheduledTask Weekly() => Use(new CronPattern("0 0 * * 0"));

    /// <summary>At the given time on the given day of the week. Laravel: <c>weeklyOn</c>.</summary>
    public ScheduledTask WeeklyOn(DayOfWeek day, int hour, int minute)
    {
        if (!Enum.IsDefined(day)) throw new ArgumentOutOfRangeException(nameof(day), day, "Not a day of the week.");
        return Use(new CronPattern($"{CheckMinute(minute)} {CheckHour(hour)} * * {(int)day}"));
    }

    /// <summary>At midnight on the first day of every month. Laravel: <c>monthly</c>.</summary>
    public ScheduledTask Monthly() => Use(new CronPattern("0 0 1 * *"));

    /// <summary>A five-field cron expression: minute, hour, day of month, month, day of week. Seconds are not supported. Laravel: <c>cron</c>.</summary>
    /// <exception cref="ArgumentException">The expression is not a valid five-field cron expression.</exception>
    public ScheduledTask Cron(string expression) => Use(new CronPattern(expression));

    /// <summary>Reads the frequency, <see cref="Weekdays"/> and <see cref="Between"/> in this time zone instead of UTC (daylight-saving changes follow the zone). Laravel: <c>timezone</c>.</summary>
    public ScheduledTask InTimeZone(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        Zone = zone;
        return this;
    }

    /// <summary>Runs only from Monday to Friday. Laravel: <c>weekdays</c>.</summary>
    public ScheduledTask Weekdays()
    {
        _weekdays = true;
        return this;
    }

    /// <summary>Runs only between two times of day, ends included; when the start is later than the end the window crosses midnight. Laravel: <c>between</c>.</summary>
    public ScheduledTask Between(TimeOnly start, TimeOnly end)
    {
        _between = (start, end);
        return this;
    }

    /// <summary>Runs only while the predicate returns true; several calls must all return true. Laravel: <c>when</c>.</summary>
    public ScheduledTask When(Func<bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _when.Add(predicate);
        return this;
    }

    /// <summary>Does not run while the predicate returns true. Laravel: <c>skip</c>.</summary>
    public ScheduledTask Skip(Func<bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _skip.Add(predicate);
        return this;
    }

    /// <summary>
    /// Skips a run while the previous run of this task is still going, on this or any other instance that shares the lock driver.
    /// The lock expires after <paramref name="expiresAfter"/> (default: <see cref="SchedulingOptions.OverlapTtl"/>, one hour) so a crash cannot block the task for good. Laravel: <c>withoutOverlapping</c>.
    /// </summary>
    public ScheduledTask WithoutOverlapping(TimeSpan? expiresAfter = null)
    {
        if (expiresAfter is { } ttl) ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        PreventOverlap = true;
        OverlapTtl = expiresAfter;
        return this;
    }

    /// <summary>
    /// Runs the task on one instance only for each due minute. It needs a lock driver shared by all instances (Redis or Memcached): startup fails with a clear message when the driver is not shared. Laravel: <c>onOneServer</c>.
    /// </summary>
    public ScheduledTask OnOneServer()
    {
        SingleServer = true;
        return this;
    }

    /// <summary>Calls the handler after a run that did not throw. An exception in a hook is logged and ignored. Laravel: <c>onSuccess</c>.</summary>
    public ScheduledTask OnSuccess(Func<Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _onSuccess.Add(handler);
        return this;
    }

    /// <summary>Calls the handler with the exception when a run throws. An exception in a hook is logged and ignored. Laravel: <c>onFailure</c>.</summary>
    public ScheduledTask OnFailure(Func<Exception, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _onFailure.Add(handler);
        return this;
    }

    /// <summary>Whether the task is due in the given minute: the frequency matches and every filter passes.</summary>
    internal bool IsDue(DateTimeOffset minuteUtc)
    {
        if (!Pattern.IsDue(minuteUtc, Zone)) return false;
        var local = TimeZoneInfo.ConvertTime(minuteUtc, Zone);
        if (_weekdays && local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        if (_between is { } window)
        {
            var time = TimeOnly.FromDateTime(local.DateTime);
            var inside = window.Start <= window.End
                ? time >= window.Start && time <= window.End
                : time >= window.Start || time <= window.End;
            if (!inside) return false;
        }

        foreach (var predicate in _when) if (!predicate()) return false;
        foreach (var predicate in _skip) if (predicate()) return false;
        return true;
    }

    private ScheduledTask Use(SchedulePattern pattern)
    {
        Pattern = pattern;
        return this;
    }

    private static int CheckMinute(int minute)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minute);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minute, 59);
        return minute;
    }

    private static int CheckHour(int hour)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hour);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hour, 23);
        return hour;
    }
}
