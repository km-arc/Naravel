using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Naravel.Queue.Database;
using Naravel.Queue.Drivers;

namespace Naravel.Queue.Providers.Tests;

/// <summary>
/// R07.T08: the shipped SQLite script and the opt-in schema helper. These tests never call
/// <c>Database.EnsureCreated()</c>: every table comes from <see cref="QueueSchema"/>.
/// </summary>
public sealed class DatabaseSchemaTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "naravel-schema-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly DbContextOptions<SqliteQueueContext> _options;

    public DatabaseSchemaTests()
    {
        _options = new DbContextOptionsBuilder<SqliteQueueContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (System.IO.File.Exists(_databasePath)) System.IO.File.Delete(_databasePath);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Naravel.Queue.Providers.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static async Task<List<string>> TableNamesAsync(SqliteQueueContext context)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    private static async Task<HashSet<string>> ColumnNamesAsync(SqliteQueueContext context, string table)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(1));
        return names;
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Testing")]
    public async Task Helper_creates_the_schema_in_development_and_test_and_is_idempotent(string environmentName)
    {
        await using var context = new SqliteQueueContext(_options);
        var environment = new TestEnvironment(environmentName);

        await QueueSchema.EnsureSqliteSchemaAsync(context, environment);
        await QueueSchema.EnsureSqliteSchemaAsync(context, environment);

        (await TableNamesAsync(context)).Should().BeEquivalentTo("QueueJobs", "QueueFailedJobs", "QueueBatches");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    public async Task Helper_refuses_every_other_environment_and_creates_nothing(string environmentName)
    {
        await using var context = new SqliteQueueContext(_options);

        var act = () => QueueSchema.EnsureSqliteSchemaAsync(context, new TestEnvironment(environmentName));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Development or Test*");
        (await TableNamesAsync(context)).Should().BeEmpty();
    }

    [Fact]
    public async Task Schema_is_not_created_unless_the_application_asks_for_it()
    {
        // Building the context and the driver must never create tables on their own.
        var driver = new DatabaseQueueDriver<SqliteQueueContext>(new SqliteQueueContextFactory(_options));

        var act = () => driver.PushAsync(new QueuedMessage { Queue = "q", JobType = "job", Payload = "{}" });

        (await act.Should().ThrowAsync<SqliteException>()).Which.Message.Should().Contain("no such table");
    }

    [Fact]
    public async Task Script_columns_match_what_the_model_maps()
    {
        await using var context = new SqliteQueueContext(_options);
        await QueueSchema.EnsureSqliteSchemaAsync(context, new TestEnvironment("Development"));

        foreach (var entityType in new[] { typeof(JobRecord), typeof(FailedJobRecord), typeof(QueueBatchRecord) })
        {
            var entity = context.Model.FindEntityType(entityType)!;
            var table = entity.GetTableName()!;
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            var mapped = entity.GetProperties().Select(property => property.GetColumnName(store)!).ToHashSet(StringComparer.Ordinal);

            (await ColumnNamesAsync(context, table)).Should().BeEquivalentTo(mapped, $"the script must create exactly the columns mapped for {table}");
        }
    }

    [Fact]
    public async Task Script_supports_the_driver_and_the_failed_and_batch_tables()
    {
        await using (var context = new SqliteQueueContext(_options))
            await QueueSchema.EnsureSqliteSchemaAsync(context, new TestEnvironment("Development"));

        var driver = new DatabaseQueueDriver<SqliteQueueContext>(new SqliteQueueContextFactory(_options));
        var message = new QueuedMessage { Queue = "schema", JobType = "job", Payload = "{}", MaxAttempts = 2 };
        await driver.PushAsync(message);
        var popped = await driver.PopAsync("schema");
        popped!.Id.Should().Be(message.Id);
        await driver.AckAsync(popped);
        (await driver.PopAsync("schema")).Should().BeNull();

        var failing = new QueuedMessage { Queue = "schema", JobType = "job", Payload = "{}" };
        await driver.PushAsync(failing);
        await driver.FailAsync((await driver.PopAsync("schema"))!);

        await using var verify = new SqliteQueueContext(_options);
        verify.Set<FailedJobRecord>().Add(new FailedJobRecord
        {
            Id = "failed-1", Envelope = "{}", Error = "boom", FailedAt = DateTimeOffset.UtcNow
        });
        verify.Set<QueueBatchRecord>().Add(new QueueBatchRecord
        {
            Id = "batch-1", TotalJobs = 3, AllowFailures = true,
            ThenCallbacks = "[]", CatchCallbacks = "[]", FinallyCallbacks = "[]"
        });
        await verify.SaveChangesAsync();

        (await verify.Set<FailedJobRecord>().SingleAsync()).Error.Should().Be("boom");
        (await verify.Set<QueueBatchRecord>().SingleAsync()).AllowFailures.Should().BeTrue();
        (await verify.Set<JobRecord>().SingleAsync(job => job.Id == failing.Id)).FailedAt.Should().NotBeNull();
    }

    [Fact]
    public void Script_is_also_available_as_a_plain_file_next_to_the_source()
    {
        QueueSchema.SqliteScript.Should().Contain("CREATE TABLE IF NOT EXISTS \"QueueJobs\"")
            .And.Contain("CREATE TABLE IF NOT EXISTS \"QueueFailedJobs\"")
            .And.Contain("CREATE TABLE IF NOT EXISTS \"QueueBatches\"");
    }
}
