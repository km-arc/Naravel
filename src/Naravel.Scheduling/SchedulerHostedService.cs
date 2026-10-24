using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Naravel.Scheduling;

/// <summary>Wakes at every minute boundary and starts the tasks that are due; waits for running tasks when the host stops.</summary>
internal sealed class SchedulerHostedService(Scheduler scheduler, TimeProvider time, ILogger<SchedulerHostedService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Task, byte> _running = new();

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        scheduler.Validate();
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var last = SchedulePattern.Minute(time.GetUtcNow());
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = time.GetUtcNow();
                var next = SchedulePattern.Minute(now).AddMinutes(1);
                await Task.Delay(next - now, time, stoppingToken).ConfigureAwait(false);

                // Use the minute the clock shows now, not the planned one, so a late wake-up never runs a minute twice or invents one.
                var minute = SchedulePattern.Minute(time.GetUtcNow());
                if (minute <= last) continue;
                last = minute;
                foreach (var task in scheduler.StartDue(minute, stoppingToken))
                {
                    _running[task] = 0;
                    _ = task.ContinueWith(done => _running.TryRemove(done, out _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping; StopAsync waits for the running tasks.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        var running = _running.Keys.ToArray();
        if (running.Length == 0) return;
        try
        {
            await Task.WhenAll(running).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("The host stopped before {Count} scheduled task(s) finished.", running.Length);
        }
    }
}
