using Microsoft.EntityFrameworkCore;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Jobs;

namespace Naravel.Queue.Database;

/// <summary>
/// Adds a job to the caller's own <see cref="DbContext"/> so it commits (or rolls back) together with the business changes in the same
/// <c>SaveChanges</c> or transaction (PDR-023). The existing database queue worker is the relay: it already polls <c>QueueJobs</c>.
/// <para>
/// <b>Contract:</b> the context must map the jobs table (<c>ConfigureQueueJobs</c>); nothing is sent until the caller saves; delivery stays
/// at-least-once, so jobs stay idempotent. <see cref="IUniqueJob"/> is rejected (its gate lives in the cache and cannot join a database
/// transaction); chains and batches are not supported here; <c>DispatchOptions.Connection</c> is ignored because the job is stored in this
/// database and must be consumed by a worker on the <c>database</c> connection.
/// </para>
/// </summary>
public sealed class DatabaseJobEnqueuer
{
    private readonly IJobMessageFactory _factory;

    /// <summary>Initializes the enqueuer with the message factory registered by <c>AddQueue</c>.</summary>
    /// <param name="factory">Builds the message envelope exactly as the dispatcher does.</param>
    public DatabaseJobEnqueuer(IJobMessageFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>Tracks the job in <paramref name="context"/>; it is sent when the caller commits. Performs no I/O.</summary>
    /// <param name="context">The caller's context, which maps the jobs table.</param>
    /// <param name="job">The job to enqueue.</param>
    /// <param name="configure">Optional per-dispatch options (queue, attempts, delay, priority).</param>
    /// <returns>The job id.</returns>
    /// <exception cref="ArgumentException">The job is an <see cref="IUniqueJob"/>.</exception>
    /// <exception cref="InvalidOperationException">The context does not map the jobs table.</exception>
    public string Enqueue(DbContext context, IJob job, Action<DispatchOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(job);
        if (job is IUniqueJob)
            throw new ArgumentException($"{job.GetType().Name} implements IUniqueJob, whose gate cannot join a database transaction. Dispatch it with IJobDispatcher after the commit.", nameof(job));
        if (context.Model.FindEntityType(typeof(JobRecord)) is null)
            throw new InvalidOperationException($"{context.GetType().Name} does not map the queue jobs table. Call modelBuilder.ConfigureQueueJobs() in OnModelCreating.");

        var message = _factory.CreateMessage(job, configure);
        context.Set<JobRecord>().Add(DatabaseQueueDriver<DbContext>.ToRecord(message));
        return message.Id;
    }
}
