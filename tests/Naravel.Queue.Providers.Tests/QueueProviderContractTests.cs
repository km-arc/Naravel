using Confluent.Kafka;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Naravel.Queue.Database;
using Naravel.Queue.Drivers;
using Naravel.Queue.Kafka;
using Naravel.Queue.Providers.Tests;
using Naravel.Queue.Redis;
using Naravel.Queue.RabbitMQ;
using Naravel.Queue.Tests;
using Naravel.Testing;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace Naravel.Queue.Providers.Tests;

public sealed class DatabaseQueueProviderTests : QueueDriverContractTests
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "naravel-queue-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly DbContextOptions<SqliteQueueContext> _options;

    public DatabaseQueueProviderTests()
    {
        _options = new DbContextOptionsBuilder<SqliteQueueContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;
        using var context = new SqliteQueueContext(_options);
        context.Database.EnsureCreated();
    }

    [Fact]
    public Task Shared_contract() => RunContractAsync();

    protected override IQueueDriver CreateDriver()
        => new DatabaseQueueDriver<SqliteQueueContext>(new SqliteQueueContextFactory(_options));

    protected override Task CleanupAsync()
    {
        SqliteConnection.ClearAllPools();
        if (System.IO.File.Exists(_databasePath)) System.IO.File.Delete(_databasePath);
        return Task.CompletedTask;
    }
}

public sealed class RedisQueueProviderTests : QueueDriverContractTests
{
    private IConnectionMultiplexer? _multiplexer;

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Shared_contract() => RunContractAsync();

    protected override IQueueDriver CreateDriver()
    {
        _multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        return new RedisQueueDriver(_multiplexer, TimeSpan.FromSeconds(30));
    }

    protected override Task CleanupAsync()
    {
        if (_multiplexer is null) return Task.CompletedTask;

        var database = _multiplexer.GetDatabase();
        foreach (var endpoint in _multiplexer.GetEndPoints())
        {
            var server = _multiplexer.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica) continue;
            var keys = server.Keys(database.Database, $"netqueue:{QueuePrefix}*").ToArray();
            if (keys.Length > 0) database.KeyDelete(keys);
        }

        _multiplexer.Dispose();
        return Task.CompletedTask;
    }
}

public sealed class RabbitMqQueueProviderTests : QueueDriverContractTests
{
    [ServiceFact("NARAVEL_TEST_RABBITMQ")]
    public Task Shared_contract() => RunContractAsync();

    protected override IQueueDriver CreateDriver()
    {
        var uri = new Uri(Environment.GetEnvironmentVariable("NARAVEL_TEST_RABBITMQ")!);
        var credentials = uri.UserInfo.Split(':', 2);
        var virtualHost = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        return new RabbitMqQueueDriver(
            uri.Host,
            uri.Port,
            credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0]) : null,
            credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,
            string.IsNullOrEmpty(virtualHost) ? "/" : virtualHost);
    }

    protected override Task CleanupAsync()
    {
        var uri = new Uri(Environment.GetEnvironmentVariable("NARAVEL_TEST_RABBITMQ")!);
        var credentials = uri.UserInfo.Split(':', 2);
        var factory = new ConnectionFactory
        {
            HostName = uri.Host,
            Port = uri.Port,
            UserName = credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0]) : "guest",
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "guest",
            VirtualHost = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))
        };

        using var connection = factory.CreateConnection("naravel-tests-cleanup");
        using var channel = connection.CreateModel();
        foreach (var queue in UsedQueueNames)
        {
            channel.QueueDelete($"netqueue.ready.{queue}");
            channel.QueueDelete($"netqueue.delay.{queue}");
            channel.QueueDelete($"netqueue.failed.{queue}");
        }
        return Task.CompletedTask;
    }
}

public sealed class KafkaQueueProviderTests : QueueDriverContractTests
{
    [ServiceFact("NARAVEL_TEST_KAFKA")]
    public Task Shared_contract() => RunContractAsync();

    protected override IQueueDriver CreateDriver()
        => new KafkaQueueDriver(
            Environment.GetEnvironmentVariable("NARAVEL_TEST_KAFKA")!,
            "naravel-tests-" + QueuePrefix);

    protected override async Task CleanupAsync()
    {
        var bootstrapServers = Environment.GetEnvironmentVariable("NARAVEL_TEST_KAFKA")!;
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        try
        {
            await admin.DeleteTopicsAsync(UsedQueueNames.Select(queue => $"netqueue-{queue}"));
            await admin.DeleteTopicsAsync(UsedQueueNames.Select(queue => $"netqueue-{queue}-failed"));
        }
        catch (KafkaException)
        {
        }
    }
}

public sealed class SqliteQueueContext(DbContextOptions<SqliteQueueContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureQueueJobs();
        var utcTicks = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
        modelBuilder.Entity<JobRecord>().Property(job => job.CreatedAt).HasConversion(utcTicks);
        modelBuilder.Entity<JobRecord>().Property(job => job.AvailableAt).HasConversion(utcTicks);
        modelBuilder.Entity<JobRecord>().Property(job => job.ReservedAt).HasConversion(utcTicks);
        modelBuilder.Entity<JobRecord>().Property(job => job.FailedAt).HasConversion(utcTicks);
    }
}

public sealed class SqliteQueueContextFactory(DbContextOptions<SqliteQueueContext> options)
    : IDbContextFactory<SqliteQueueContext>
{
    public SqliteQueueContext CreateDbContext() => new(options);

    public Task<SqliteQueueContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
}