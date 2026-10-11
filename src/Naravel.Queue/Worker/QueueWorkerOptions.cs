namespace Naravel.Queue.Worker;

/// <summary>Configures the background worker that polls a connection and executes jobs. Equivalent to `php artisan queue:work`.</summary>
public class QueueWorkerOptions
{
    /// <summary>Which connection to consume from. Null = default connection.</summary>
    public string? Connection { get; set; }

    /// <summary>Queues to consume from, checked in the given order every poll (earlier = higher priority). Defaults to ["default"].</summary>
    public string[] Queues { get; set; } = { "default" };

    /// <summary>How many jobs can run concurrently within this worker.</summary>
    public int Concurrency { get; set; } = 1;

    /// <summary>Whether this worker exits after a poll finds every configured queue empty.</summary>
    public bool StopWhenEmpty { get; set; }

    /// <summary>Maximum number of messages this worker reserves for processing before it exits.</summary>
    public int? MaxJobs { get; set; }

    /// <summary>Maximum time this worker accepts new work. An active job is allowed to finish.</summary>
    public TimeSpan? MaxRuntime { get; set; }

    /// <summary>How long to wait before polling again when every configured queue was empty.</summary>
    public TimeSpan Rest { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Compatibility alias for <see cref="Rest"/>.</summary>
    public TimeSpan SleepWhenEmpty { get => Rest; set => Rest = value; }

    /// <summary>Optional per-job execution timeout. If a job runs longer than this, it is cancelled and treated as failed for that attempt.</summary>
    public TimeSpan? JobTimeout { get; set; }

    /// <summary>
    /// Optional upper bound on the timeout any job handled by this worker may declare (a job's own
    /// <c>Job.Timeout</c>). When set, startup logs a warning if the driver's visibility timeout is below
    /// <c>MaxJobTimeout + <see cref="VisibilityMargin"/></c>. Null = no bound declared, no such warning.
    /// </summary>
    public TimeSpan? MaxJobTimeout { get; set; }

    /// <summary>Safety margin added to <see cref="MaxJobTimeout"/> (and to a job's own timeout) when comparing it with the driver's visibility timeout.</summary>
    public TimeSpan VisibilityMargin { get; set; } = TimeSpan.FromSeconds(10);
}
