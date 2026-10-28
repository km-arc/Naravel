using Naravel.Queue.Drivers;
using Naravel.Queue.Jobs;

namespace Naravel.Queue.Dispatch;

/// <summary>
/// Builds the <see cref="QueuedMessage"/> envelope for a job exactly as <see cref="IJobDispatcher.DispatchAsync"/> does, without sending it.
/// Storage integrations (for example the transactional enqueue of <c>Naravel.Queue.Database</c>) use it so the envelope is never built twice.
/// </summary>
public interface IJobMessageFactory
{
    /// <summary>Serializes <paramref name="job"/> and applies the job's own and the <paramref name="configure"/> options (queue, attempts, delay, priority, trace context). Nothing is pushed.</summary>
    /// <param name="job">The job to wrap.</param>
    /// <param name="configure">Optional per-dispatch options.</param>
    /// <returns>A new, unsent message with a fresh id.</returns>
    QueuedMessage CreateMessage(IJob job, Action<DispatchOptions>? configure = null);
}
