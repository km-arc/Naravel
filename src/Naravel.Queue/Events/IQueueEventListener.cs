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
    Task OnProcessingAsync(QueuedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    Task OnProcessedAsync(QueuedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    Task OnRetryingAsync(QueuedMessage message, Exception exception, TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    Task OnFailedAsync(QueuedMessage message, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;
}
