using Naravel.Queue.Drivers;

namespace Naravel.Queue.Events;

/// <summary>
/// Observe the job lifecycle across the whole application - useful for metrics, logging dashboards,
/// alerting, etc. Equivalent to Laravel's Queue::before()/after()/failing() events.
/// Register any number of implementations in DI; all of them are invoked (exceptions from a listener
/// are logged and swallowed so one broken listener can't break job processing).
/// </summary>
public interface IQueueEventListener
{
    /// <summary>Called before a message starts processing.</summary>
    Task OnProcessingAsync(QueuedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>Called after a message has been processed successfully.</summary>
    Task OnProcessedAsync(QueuedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>Called when a message failed and is scheduled to be retried after <paramref name="delay"/>.</summary>
    Task OnRetryingAsync(QueuedMessage message, Exception exception, TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>Called when a message has failed permanently.</summary>
    Task OnFailedAsync(QueuedMessage message, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;
}
