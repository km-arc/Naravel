using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Naravel.Queue.Database;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Drivers;
using Naravel.Queue.Jobs;

namespace Naravel.Queue.Providers.Tests;

/// <summary>PDR-023: a job enqueued through <see cref="DatabaseJobEnqueuer"/> commits or rolls back with the caller's own changes.</summary>
public sealed class TransactionalEnqueueTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "naravel-outbox-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly DbContextOptions<SqliteQueueContext> _options;
    private readonly DatabaseJobEnqueuer _enqueuer = new(new StubFactory());

    public TransactionalEnqueueTests() =>
        _options = new DbContextOptionsBuilder<SqliteQueueContext>().UseSqlite($"Data Source={_path}").Options;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (System.IO.File.Exists(_path)) System.IO.File.Delete(_path);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class PlainJob : IJob
    {
        public Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UniquePlainJob : IUniqueJob
    {
        public string UniqueKey => "k";
        public Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubFactory : IJobMessageFactory
    {
        public QueuedMessage CreateMessage(IJob job, Action<DispatchOptions>? configure = null)
        {
            var options = new DispatchOptions();
            configure?.Invoke(options);
            return new QueuedMessage
            {
                JobType = job.GetType().Name,
                Payload = "{}",
                Connection = "database",
                Queue = options.Queue ?? "default",
                MaxAttempts = options.MaxAttempts ?? 3,
                AvailableAt = options.At ?? DateTimeOffset.UtcNow + (options.Delay ?? TimeSpan.Zero),
            };
        }
    }

    private async Task<SqliteQueueContext> OpenAsync()
    {
        var context = new SqliteQueueContext(_options);
        await QueueSchema.EnsureSqliteSchemaAsync(context, new TestEnvironment());
        return context;
    }

    private static Task<int> CountAsync(SqliteQueueContext context) => context.Set<JobRecord>().AsNoTracking().CountAsync();

    [Fact]
    public async Task Nothing_is_stored_until_the_caller_saves()
    {
        await using var db = await OpenAsync();

        var id = _enqueuer.Enqueue(db, new PlainJob());

        id.Should().NotBeNullOrEmpty();
        await using var other = new SqliteQueueContext(_options);
        (await CountAsync(other)).Should().Be(0, "Enqueue performs no I/O");

        await db.SaveChangesAsync();
        (await CountAsync(other)).Should().Be(1);
        (await other.Set<JobRecord>().AsNoTracking().SingleAsync()).Id.Should().Be(id);
    }

    [Fact]
    public async Task A_rolled_back_transaction_leaves_no_job()
    {
        await using var db = await OpenAsync();

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            _enqueuer.Enqueue(db, new PlainJob());
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var other = new SqliteQueueContext(_options);
        (await CountAsync(other)).Should().Be(0);
    }

    [Fact]
    public async Task A_committed_transaction_keeps_the_job_and_the_database_driver_can_pop_it()
    {
        await using var db = await OpenAsync();

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            _enqueuer.Enqueue(db, new PlainJob(), o => o.OnQueue("mail").WithMaxAttempts(7));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var driver = new DatabaseQueueDriver<SqliteQueueContext>(new SqliteQueueContextFactory(_options));
        var message = await driver.PopAsync("mail");

        message.Should().NotBeNull();
        message!.MaxAttempts.Should().Be(7);
        message.JobType.Should().Be(nameof(PlainJob));
    }

    [Fact]
    public async Task A_delayed_job_is_not_available_before_its_time()
    {
        await using var db = await OpenAsync();
        _enqueuer.Enqueue(db, new PlainJob(), o => o.DelayFor(TimeSpan.FromHours(1)));
        await db.SaveChangesAsync();

        var driver = new DatabaseQueueDriver<SqliteQueueContext>(new SqliteQueueContextFactory(_options));

        (await driver.PopAsync("default")).Should().BeNull();
    }

    [Fact]
    public async Task A_unique_job_is_rejected_because_its_gate_cannot_join_the_transaction()
    {
        await using var db = await OpenAsync();

        var act = () => _enqueuer.Enqueue(db, new UniquePlainJob());

        act.Should().Throw<ArgumentException>().WithMessage("*IUniqueJob*");
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        using var db = new SqliteQueueContext(_options);

        ((Action)(() => _enqueuer.Enqueue(null!, new PlainJob()))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _enqueuer.Enqueue(db, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new DatabaseJobEnqueuer(null!))).Should().Throw<ArgumentNullException>();
    }

    private sealed class UnmappedContext(DbContextOptions<UnmappedContext> options) : DbContext(options);

    [Fact]
    public void A_context_that_does_not_map_the_jobs_table_fails_clearly()
    {
        using var db = new UnmappedContext(new DbContextOptionsBuilder<UnmappedContext>().UseSqlite($"Data Source={_path}").Options);

        var act = () => _enqueuer.Enqueue(db, new PlainJob());

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConfigureQueueJobs*");
    }
}
