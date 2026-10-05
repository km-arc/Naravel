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

    /// <summary>How long to sleep between polls when every configured queue was empty.</summary>
    public TimeSpan SleepWhenEmpty { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Optional per-job execution timeout. If a job runs longer than this, it is cancelled and treated as failed for that attempt.</summary>
    public TimeSpan? JobTimeout { get; set; }
}
