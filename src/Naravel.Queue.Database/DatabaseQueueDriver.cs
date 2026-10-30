using System.Diagnostics.CodeAnalysis;
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
public class DatabaseQueueDriver<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TContext> : IQueueDriver where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;

    private readonly TimeSpan _visibilityTimeout;
    /// <inheritdoc />
    public TimeSpan? VisibilityTimeout => _visibilityTimeout;

    /// <summary>Initializes the driver with a context factory and an optional visibility timeout (default five minutes).</summary>
    public DatabaseQueueDriver(IDbContextFactory<TContext> contextFactory, TimeSpan? visibilityTimeout = null)
    {
        _contextFactory = contextFactory;
        _visibilityTimeout = visibilityTimeout ?? TimeSpan.FromMinutes(5);
    }

    /// <inheritdoc />
    public async Task PushAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        db.Set<JobRecord>().Add(ToRecord(message));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
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
                .SetProperty(j => j.ReservationId, (string?)null)
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
            var reservationId = Guid.NewGuid().ToString("N");
            var affected = await db.Set<JobRecord>()
                .Where(j => j.Id == candidate.Id && j.ReservedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.ReservedAt, now)
                    .SetProperty(j => j.ReservationId, reservationId), cancellationToken);

            if (affected == 1)
            {
                candidate.ReservedAt = now;
                candidate.ReservationId = reservationId;
                return ToMessage(candidate);
            }
            // affected == 0 means another worker claimed it between our SELECT and UPDATE - try the next candidate.
        }

        return null;
    }

    /// <inheritdoc />
    public async Task AckAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await Owned(db, message).ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var availableAt = DateTimeOffset.UtcNow + delay;
        await Owned(db, message)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.ReservedAt, (DateTimeOffset?)null)
                .SetProperty(j => j.ReservationId, (string?)null)
                .SetProperty(j => j.AvailableAt, availableAt)
                .SetProperty(j => j.Attempts, message.Attempts)
                .SetProperty(j => j.Error, message.Error), cancellationToken);
    }

    /// <inheritdoc />
    public async Task FailAsync(QueuedMessage message, CancellationToken cancellationToken = default)
        => await TryFailAsync(message, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryFailAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await Owned(db, message)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.FailedAt, DateTimeOffset.UtcNow)
                .SetProperty(j => j.ReservationId, (string?)null)
                .SetProperty(j => j.Attempts, message.Attempts)
                .SetProperty(j => j.Error, message.Error), cancellationToken);
        return rows > 0;
    }

    // Fencing: a message that carries a ReservationId only affects the row while that reservation is still current.
    // After the visibility timeout reclaimed the row (token cleared) or another worker re-reserved it (new token), a
    // late Ack/Release/Fail from the original worker matches zero rows instead of deleting or re-queuing the new copy.
    // Messages without a token (envelopes created before this field existed) keep the previous unfenced behaviour.
    private static IQueryable<JobRecord> Owned(TContext db, QueuedMessage message)
    {
        var id = message.Id;
        var query = db.Set<JobRecord>().Where(j => j.Id == id);
        var reservationId = message.ReservationId;
        return reservationId is null ? query : query.Where(j => j.ReservationId == reservationId);
    }

    /// <inheritdoc />
    public async Task<long> SizeAsync(string queue, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<JobRecord>().LongCountAsync(j => j.Queue == queue && j.FailedAt == null, cancellationToken);
    }

    internal static JobRecord ToRecord(QueuedMessage m) => new()
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
        ReservationId = m.ReservationId,
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
        ReservationId = r.ReservationId,
        Error = r.Error,
        ChainedJobPayload = r.ChainedJobPayload,
        BatchId = r.BatchId,
        TraceParent = r.TraceParent,
        TraceState = r.TraceState
    };
}
