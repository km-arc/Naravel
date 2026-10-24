using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Naravel.Cache.Abstractions;

namespace Naravel.Scheduling;

/// <summary>Decides which tasks of a <see cref="Schedule"/> are due in a minute and runs them, with the overlap and one-server guards.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>schedule:run</c> (one pass for one minute). The hosted service calls it once per minute; tests and the fake call it with a chosen minute.</para>
/// <para>A failing task never stops the others. Guards use the lock driver: the lock key is built from the task name, never from user data.</para>
/// </remarks>
public sealed class Scheduler
{
    private readonly Schedule _schedule;
    private readonly ICacheLock _locks;
    private readonly IServiceProvider _services;
    private readonly SchedulingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Action<string, string>? _observer;

    internal Scheduler(
        Schedule schedule,
        ICacheLock locks,
        IServiceProvider services,
        SchedulingOptions options,
        TimeProvider time,
        ILogger logger,
        Action<string, string>? observer = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.OverlapTtl, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.SlotLockTtl, TimeSpan.Zero);
        _schedule = schedule;
        _locks = locks;
        _services = services;
        _options = options;
        _time = time;
        _logger = logger;
        _observer = observer;
    }

    /// <summary>The task list with each task's next due minute, counted from now. Laravel: <c>schedule:list</c>.</summary>
    public IReadOnlyList<ScheduledTaskInfo> List()
    {
        var now = _time.GetUtcNow();
        return _schedule.Tasks.Select(task => new ScheduledTaskInfo(task.Name, task.Expression, task.Pattern.NextAfter(now, task.Zone))).ToArray();
    }

    /// <summary>Whether the named task is due in the minute that contains <paramref name="when"/>: the frequency matches and every filter passes.</summary>
    /// <exception cref="ArgumentException">No task has that name.</exception>
    public bool IsDue(string name, DateTimeOffset when)
    {
        var task = _schedule.Tasks.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal))
            ?? throw new ArgumentException($"No task named '{name}' is scheduled.", nameof(name));
        return task.IsDue(SchedulePattern.Minute(when));
    }

    /// <summary>Fails fast on a setup that would misbehave: <c>OnOneServer</c> with a lock driver that is not shared between instances.</summary>
    /// <exception cref="InvalidOperationException">A task uses <c>OnOneServer</c> and the lock driver is process-local.</exception>
    public void Validate()
    {
        if (_locks.IsShared) return;
        var names = _schedule.Tasks.Where(t => t.SingleServer).Select(t => t.Name).ToArray();
        if (names.Length > 0)
        {
            throw new InvalidOperationException(
                $"OnOneServer is used by {string.Join(", ", names)}, but the cache lock driver '{_locks.GetType().Name}' is not shared between processes, so every instance would run the task. Configure a shared lock driver (Redis or Memcached).");
        }
    }

    /// <summary>Runs every task due in the minute that contains <paramref name="now"/> and waits for them all.</summary>
    public Task RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        => Task.WhenAll(StartDue(now, cancellationToken));

    /// <summary>Starts every due task without waiting; the caller tracks the returned tasks.</summary>
    internal IReadOnlyList<Task> StartDue(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var minute = SchedulePattern.Minute(now);
        var started = new List<Task>();
        foreach (var task in _schedule.Tasks)
        {
            bool due;
            try
            {
                due = task.IsDue(minute);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Evaluating when scheduled task {Task} is due failed; the task is skipped for this minute.", task.Name);
                continue;
            }

            if (due) started.Add(Task.Run(() => RunAsync(task, minute, cancellationToken), CancellationToken.None));
        }

        return started;
    }

    private async Task RunAsync(ScheduledTask task, DateTimeOffset minute, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = SchedulingTelemetry.ActivitySource.StartActivity("naravel.scheduling.task");
        activity?.SetTag("task", task.Name);
        string? overlapToken = null;
        var outcome = SchedulingTelemetry.Succeeded;
        try
        {
            if (task.SingleServer)
            {
                var slot = await _locks.AcquireAsync($"schedule:slot:{task.Name}:{minute:yyyyMMddHHmm}", _options.SlotLockTtl, cancellationToken).ConfigureAwait(false);
                if (slot is null)
                {
                    outcome = SchedulingTelemetry.SkippedOtherServer;
                    return;
                }
            }

            if (task.PreventOverlap)
            {
                overlapToken = await _locks.AcquireAsync($"schedule:overlap:{task.Name}", task.OverlapTtl ?? _options.OverlapTtl, cancellationToken).ConfigureAwait(false);
                if (overlapToken is null)
                {
                    outcome = SchedulingTelemetry.SkippedOverlap;
                    return;
                }
            }

            await using var scope = _services.CreateAsyncScope();
            await task.Handler(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
            await RunSuccessHooksAsync(task).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = SchedulingTelemetry.Cancelled;
        }
        catch (Exception exception)
        {
            outcome = SchedulingTelemetry.Failed;
            _logger.LogError(exception, "Scheduled task {Task} failed.", task.Name);
            await RunFailureHooksAsync(task, exception).ConfigureAwait(false);
        }
        finally
        {
            if (overlapToken is not null)
            {
                try
                {
                    await _locks.ReleaseAsync($"schedule:overlap:{task.Name}", overlapToken, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Releasing the overlap lock of scheduled task {Task} failed; it expires on its own.", task.Name);
                }
            }

            var tags = new KeyValuePair<string, object?>[] { new("task", task.Name), new("outcome", outcome) };
            SchedulingTelemetry.Runs.Add(1, tags);
            SchedulingTelemetry.Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
            activity?.SetTag("outcome", outcome);
            _observer?.Invoke(task.Name, outcome);
        }
    }

    private async Task RunSuccessHooksAsync(ScheduledTask task)
    {
        foreach (var hook in task.SuccessHooks)
        {
            try
            {
                await hook().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "A hook of scheduled task {Task} failed.", task.Name);
            }
        }
    }

    private async Task RunFailureHooksAsync(ScheduledTask task, Exception failure)
    {
        foreach (var hook in task.FailureHooks)
        {
            try
            {
                await hook(failure).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "A hook of scheduled task {Task} failed.", task.Name);
            }
        }
    }
}
