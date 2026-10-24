namespace Naravel.Scheduling;

/// <summary>A read-only line of the task list: what runs and when it runs next.</summary>
/// <remarks><b>Laravel equivalent:</b> a row of <c>schedule:list</c>.</remarks>
public sealed class ScheduledTaskInfo
{
    internal ScheduledTaskInfo(string name, string expression, DateTimeOffset? nextRun)
    {
        Name = name;
        Expression = expression;
        NextRun = nextRun;
    }

    /// <summary>The task name.</summary>
    public string Name { get; }

    /// <summary>The frequency as text: a cron expression, or <c>every N minutes</c>.</summary>
    public string Expression { get; }

    /// <summary>The next minute the frequency is due, or null when there is none. Filters such as <c>When</c> are not evaluated.</summary>
    public DateTimeOffset? NextRun { get; }
}
