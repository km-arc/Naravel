using Microsoft.EntityFrameworkCore;
using Naravel.Queue.Drivers;

namespace Naravel.Queue.Database;

/// <summary>
/// Database-backed driver, works with any EF Core provider (SQL Server, PostgreSQL/Npgsql, SQLite,
/// MySQL, ...) - equivalent to Laravel's "database" queue driver.
///
/// Reservation uses a compare-and-swap update (ExecuteUpdateAsync, EF Core 7+): a candidate row is
/// only actually claimed if its ReservedAt was still null at update time, so two workers racing for
/// the same row cannot both succeed, without needing provider-specific locking hints.
/// </summary>
public class DatabaseQueueDriver<TContext> : IQueueDriver where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;

    private readonly TimeSpan _visibilityTimeout;
    public TimeSpan? VisibilityTimeout => _visibilityTimeout;

    public DatabaseQueueDriver(IDbContextFactory<TContext> contextFactory, TimeSpan? visibilityTimeout = null)
    {
        _contextFactory = contextFactory;
        _visibilityTimeout = visibilityTimeout ?? TimeSpan.FromMinutes(5);
    }

    public async Task PushAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        db.Set<JobRecord>().Add(ToRecord(message));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<QueuedMessage?> PopAsync(string queue, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // Jobs reserved by a worker that most likely crashed become available again after the visibility timeout.
        var staleBefore = now - _visibilityTimeout;
        await db.Set<JobRecord>()
            .Where(j => j.Queue == queue && j.FailedAt == null && j.ReservedAt != null && j.ReservedAt < staleBefore)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.ReservedAt, (DateTimeOffset?)null)
                .SetProperty(j => j.Attempts, j => j.Attempts + 1), cancellationToken);

        // Grab a batch of candidates in priority/availability order, then try to claim them one by one.
        var candidates = await db.Set<JobRecord>()
            .Where(j => j.Queue == queue && j.ReservedAt == null && j.FailedAt == null && j.AvailableAt <= now)
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => j.AvailableAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var affected = await db.Set<JobRecord>()
                .Where(j => j.Id == candidate.Id && j.ReservedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.ReservedAt, now), cancellationToken);

            if (affected == 1)
            {
                candidate.ReservedAt = now;
                return ToMessage(candidate);
            }
            // affected == 0 means another worker claimed it between our SELECT and UPDATE - try the next candidate.
        }

        return null;
    }

    public async Task AckAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Set<JobRecord>().Where(j => j.Id == message.Id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var availableAt = DateTimeOffset.UtcNow + delay;
        await db.Set<JobRecord>().Where(j => j.Id == message.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.ReservedAt, (DateTimeOffset?)null)
                .SetProperty(j => j.AvailableAt, availableAt)
                .SetProperty(j => j.Attempts, message.Attempts)
                .SetProperty(j => j.Error, message.Error), cancellationToken);
    }

    public async Task FailAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Set<JobRecord>().Where(j => j.Id == message.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.FailedAt, DateTimeOffset.UtcNow)
                .SetProperty(j => j.Attempts, message.Attempts)
                .SetProperty(j => j.Error, message.Error), cancellationToken);
    }

    public async Task<long> SizeAsync(string queue, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<JobRecord>().LongCountAsync(j => j.Queue == queue && j.FailedAt == null, cancellationToken);
    }

    private static JobRecord ToRecord(QueuedMessage m) => new()
    {
        Id = m.Id,
        Queue = m.Queue,
        Connection = m.Connection,
        JobType = m.JobType,
        Payload = m.Payload,
        Attempts = m.Attempts,
        MaxAttempts = m.MaxAttempts,
        Priority = m.Priority,
        CreatedAt = m.CreatedAt,
        AvailableAt = m.AvailableAt,
        ReservedAt = m.ReservedAt,
        Error = m.Error,
        ChainedJobPayload = m.ChainedJobPayload,
        BatchId = m.BatchId,
        TraceParent = m.TraceParent,
        TraceState = m.TraceState
    };

    private static QueuedMessage ToMessage(JobRecord r) => new()
    {
        Id = r.Id,
        Queue = r.Queue,
        Connection = r.Connection,
        JobType = r.JobType,
        Payload = r.Payload,
        Attempts = r.Attempts,
        MaxAttempts = r.MaxAttempts,
        Priority = r.Priority,
        CreatedAt = r.CreatedAt,
        AvailableAt = r.AvailableAt,
        ReservedAt = r.ReservedAt,
        Error = r.Error,
        ChainedJobPayload = r.ChainedJobPayload,
        BatchId = r.BatchId,
        TraceParent = r.TraceParent,
        TraceState = r.TraceState
    };
}
