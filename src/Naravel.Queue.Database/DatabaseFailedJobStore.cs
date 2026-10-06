using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Naravel.Queue.Drivers;
using Naravel.Queue.Failed;

namespace Naravel.Queue.Database;

/// <summary>Persistent failed-job storage using the application's EF Core context.</summary>
/// <remarks><b>Laravel equivalent:</b> failed-jobs provider. It exists to retain retryable envelopes in the application's database without owning its DbContext or migrations.</remarks>
public sealed class DatabaseFailedJobStore<TContext> : IFailedJobStore where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;

    public DatabaseFailedJobStore(IDbContextFactory<TContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task RecordAsync(QueuedMessage message, Exception exception, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.Set<FailedJobRecord>().SingleOrDefaultAsync(item => item.Id == message.Id, cancellationToken);
        if (record is null)
        {
            record = new FailedJobRecord { Id = message.Id };
            db.Set<FailedJobRecord>().Add(record);
        }

        message.Error = exception.ToString();
        record.Envelope = JsonSerializer.Serialize(message);
        record.Error = message.Error;
        record.FailedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QueuedMessage>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var records = await db.Set<FailedJobRecord>().AsNoTracking().ToListAsync(cancellationToken);
        return records.Select(record =>
        {
            var message = JsonSerializer.Deserialize<QueuedMessage>(record.Envelope)!;
            message.Error = record.Error;
            return message;
        }).ToList();
    }

    public async Task<bool> ForgetAsync(string id, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<FailedJobRecord>().Where(record => record.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task<int> FlushAsync(CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<FailedJobRecord>().ExecuteDeleteAsync(cancellationToken);
    }
}