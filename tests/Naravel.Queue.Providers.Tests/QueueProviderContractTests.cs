using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Naravel.Queue.Batching;
using Naravel.Queue.Database;
using Naravel.Queue.Drivers;
using Naravel.Queue.Failed;
using Naravel.Queue.Jobs;
using Naravel.Queue.Kafka;
using Naravel.Queue.Providers.Tests;
using Naravel.Queue.Redis;
using Naravel.Queue.RabbitMQ;
using Naravel.Queue.Tests;
using Naravel.Testing;
using RabbitMQ.Client;
using StackExchange.Redis;
using Xunit.Abstractions;

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

    [Fact]
    public async Task Reclaimed_reservation_increments_attempt_count()
    {
        var driver = new DatabaseQueueDriver<SqliteQueueContext>(
            new SqliteQueueContextFactory(_options), TimeSpan.FromMilliseconds(100));
        var message = new QueuedMessage { Queue = "reclaim", JobType = "job", Payload = "{}" };
        await driver.PushAsync(message);
        (await driver.PopAsync("reclaim"))!.Attempts.Should().Be(0);

        await Task.Delay(250);
        var reclaimed = await driver.PopAsync("reclaim");
        reclaimed!.Id.Should().Be(message.Id);
        reclaimed.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Stale_worker_cannot_ack_release_or_fail_a_reservation_that_another_worker_now_owns()
    {
        var driver = new DatabaseQueueDriver<SqliteQueueContext>(
            new SqliteQueueContextFactory(_options), TimeSpan.FromMilliseconds(600));
        var message = new QueuedMessage { Queue = "fencing", JobType = "job", Payload = "{}" };
        await driver.PushAsync(message);

        var stale = (await driver.PopAsync("fencing"))!;
        stale.ReservationId.Should().NotBeNullOrEmpty();

        await Task.Delay(900); // the first worker is "too slow": its reservation expires and is reclaimed
        var current = (await driver.PopAsync("fencing"))!;
        current.Id.Should().Be(message.Id);
        current.ReservationId.Should().NotBe(stale.ReservationId);

        await driver.AckAsync(stale);                              // must NOT delete the row the second worker owns
        (await driver.SizeAsync("fencing")).Should().Be(1);

        await driver.ReleaseAsync(stale, TimeSpan.Zero);           // must NOT make it available to a third worker
        (await driver.PopAsync("fencing")).Should().BeNull();

        await driver.FailAsync(stale);                             // must NOT mark the live job as failed
        (await driver.SizeAsync("fencing")).Should().Be(1);

        await driver.AckAsync(current);                            // the real owner still completes it
        (await driver.SizeAsync("fencing")).Should().Be(0);
    }

    [Fact]
    public async Task A_message_without_a_reservation_id_keeps_the_unfenced_behaviour()
    {
        var driver = new DatabaseQueueDriver<SqliteQueueContext>(new SqliteQueueContextFactory(_options));
        var message = new QueuedMessage { Queue = "legacy-ack", JobType = "job", Payload = "{}" };
        await driver.PushAsync(message);
        await driver.PopAsync("legacy-ack");

        await driver.AckAsync(new QueuedMessage { Id = message.Id, Queue = "legacy-ack", JobType = "job", Payload = "{}" });

        (await driver.SizeAsync("legacy-ack")).Should().Be(0);
    }

    [Fact]
    public async Task Database_driver_roundtrips_connection_and_trace_context()
    {
        var driver = new DatabaseQueueDriver<SqliteQueueContext>(new SqliteQueueContextFactory(_options));
        var message = new QueuedMessage
        {
            Queue = "trace",
            Connection = "sqlite-store",
            JobType = "job",
            Payload = "{}",
            TraceParent = "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
            TraceState = "vendor=naravel"
        };

        await driver.PushAsync(message);
        var popped = await driver.PopAsync("trace");

        popped!.Connection.Should().Be("sqlite-store");
        popped.TraceParent.Should().Be(message.TraceParent);
        popped.TraceState.Should().Be(message.TraceState);
    }

    [Fact]
    public async Task Batch_state_survives_repository_recreation_and_cancellation_skips_pending_jobs()
    {
        var factory = new SqliteQueueContextFactory(_options);
        var gate = new BatchGate();
        var blockingId = Guid.NewGuid().ToString("N");
        var skippedId = Guid.NewGuid().ToString("N");
        var callbackResult = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = new BatchCallbackRegistry();
        callbacks.Register<string>("batch.finished", (completedBatch, payload, _) =>
        {
            callbackResult.TrySetResult($"{payload}:{completedBatch.CompletedJobs}:{completedBatch.CancelledJobs}");
            return Task.CompletedTask;
        });
        BatchGates.Add(blockingId, gate);
        try
        {
            await using var host = new QueueTestHost(configureServices: services =>
            {
                services.AddSingleton<IDbContextFactory<SqliteQueueContext>>(factory);
                services.AddSingleton(callbacks);
                services.AddDatabaseBatchRepository<SqliteQueueContext>();
            });
            var batch = await host.Dispatcher.BatchAsync(
                new IJob[]
                {
                    new BlockingBatchJob { RunId = blockingId },
                    new RecordingJob { RunId = skippedId }
                },
                options => options.OnQueue("q"),
                options =>
                {
                    options.AllowFailures = true;
                    options.Finally("batch.finished", "persisted");
                });
            var repository = new DatabaseBatchRepository<SqliteQueueContext>(factory, new BatchCallbackRegistry());

            await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            (await repository.GetAsync(batch.Id, CancellationToken.None))!.PendingJobs.Should().Be(2);
            await repository.CancelAsync(batch.Id, CancellationToken.None);
            gate.Continue.TrySetResult(true);

            (await Wait.UntilAsync(async () => (await repository.GetAsync(batch.Id, CancellationToken.None))?.IsFinished == true)).Should().BeTrue();
            var persisted = await new DatabaseBatchRepository<SqliteQueueContext>(factory, new BatchCallbackRegistry())
                .GetAsync(batch.Id, CancellationToken.None);
            persisted!.CompletedJobs.Should().Be(1);
            persisted.CancelledJobs.Should().Be(1);
            persisted.AllowFailures.Should().BeTrue();
            Recorder.Count(skippedId, "run").Should().Be(0);
            (await callbackResult.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("persisted:1:1");
        }
        finally
        {
            BatchGates.Remove(blockingId);
        }
    }

    [Fact]
    public async Task Failed_job_can_be_retried_after_recreating_the_sqlite_store()
    {
        var factory = new SqliteQueueContextFactory(_options);
        await using var host = new QueueTestHost(configureServices: services =>
        {
            services.AddSingleton<IDbContextFactory<SqliteQueueContext>>(factory);
            services.AddDatabaseFailedJobStore<SqliteQueueContext>();
        });
        var failedJobs = host.Services.GetRequiredService<FailedJobManager>();
        var runId = Guid.NewGuid().ToString("N");
        var messageId = await host.Dispatch(new FailFirstExecutionsJob
        {
            RunId = runId,
            FailuresBeforeSuccess = 2
        });

        (await Wait.UntilAsync(async () => (await new DatabaseFailedJobStore<SqliteQueueContext>(factory)
            .ListAsync(CancellationToken.None)).Count == 1)).Should().BeTrue();
        (await failedJobs.RetryAsync(messageId)).Should().BeTrue();
        (await Wait.Until(() => Recorder.Count(runId, "succeeded") == 1)).Should().BeTrue();
        (await new DatabaseFailedJobStore<SqliteQueueContext>(factory)
            .ListAsync(CancellationToken.None)).Should().BeEmpty();
    }

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

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task Stale_worker_cannot_ack_release_or_fail_a_reservation_that_another_worker_now_owns()
    {
        _multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        try
        {
            var driver = new RedisQueueDriver(_multiplexer, TimeSpan.FromSeconds(1));
            var queue = QueueName("fencing");
            var message = Msg(queue);
            await driver.PushAsync(message);

            var stale = (await driver.PopAsync(queue))!;
            stale.ReservationId.Should().NotBeNullOrEmpty();

            await Task.Delay(1300); // reservation expires; the next Pop reclaims it for a second worker
            var current = (await PopEventuallyAsync(driver, queue))!;
            current.Id.Should().Be(message.Id);
            current.ReservationId.Should().NotBe(stale.ReservationId);
            current.Attempts.Should().Be(1);

            await driver.AckAsync(stale);
            await driver.ReleaseAsync(stale, TimeSpan.Zero);
            await driver.FailAsync(stale);
            (await driver.PopAsync(queue)).Should().BeNull("the live reservation must survive a stale worker's Ack/Release/Fail");

            await driver.AckAsync(current);
            (await driver.SizeAsync(queue)).Should().Be(0);
        }
        finally
        {
            await CleanupAsync();
        }
    }

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

public sealed class RedisFailedJobStoreTests
{
    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task Failed_records_survive_store_recreation_and_support_forget_and_flush()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        var key = $"netqueue:tests:failed:{Guid.NewGuid():N}";
        var store = new RedisFailedJobStore(multiplexer, key);
        var first = new QueuedMessage { Id = Guid.NewGuid().ToString("N"), Queue = "q", JobType = "recording", Payload = "{}" };
        var second = new QueuedMessage { Id = Guid.NewGuid().ToString("N"), Queue = "q", JobType = "recording", Payload = "{}" };
        await store.RecordAsync(first, new InvalidOperationException("first"), CancellationToken.None);
        await store.RecordAsync(second, new InvalidOperationException("second"), CancellationToken.None);

        var recreatedStore = new RedisFailedJobStore(multiplexer, key);
        (await recreatedStore.ListAsync(CancellationToken.None)).Should().HaveCount(2);
        (await recreatedStore.ForgetAsync(first.Id, CancellationToken.None)).Should().BeTrue();
        (await recreatedStore.FlushAsync(CancellationToken.None)).Should().Be(1);
        (await recreatedStore.ListAsync(CancellationToken.None)).Should().BeEmpty();
    }
}

public sealed class RedisBatchRepositoryTests
{
    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task Batch_state_survives_repository_recreation()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        var keyPrefix = $"netqueue:tests:batches:{Guid.NewGuid():N}";
        var callbacks = new BatchCallbackRegistry();
        var repository = new RedisBatchRepository(multiplexer, callbacks, keyPrefix);
        var batch = new QueueBatch { TotalJobs = 2 };
        await repository.RegisterAsync(batch, new BatchOptions { AllowFailures = true }, CancellationToken.None);
        await repository.MarkJobCompletedAsync(batch.Id, CancellationToken.None);

        var recreated = new RedisBatchRepository(multiplexer, callbacks, keyPrefix);
        (await recreated.GetAsync(batch.Id, CancellationToken.None))!.CompletedJobs.Should().Be(1);
        await recreated.CancelAsync(batch.Id, CancellationToken.None);
        await recreated.MarkJobCancelledAsync(batch.Id, CancellationToken.None);
        var persisted = await recreated.GetAsync(batch.Id, CancellationToken.None);
        persisted!.IsFinished.Should().BeTrue();
        persisted.IsCancelled.Should().BeTrue();

        var database = multiplexer.GetDatabase();
        await database.KeyDeleteAsync($"{keyPrefix}:{batch.Id}");
        await database.SetRemoveAsync($"{keyPrefix}:index", batch.Id);
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

    protected override async Task CleanupAsync()
    {
        var uri = new Uri(Environment.GetEnvironmentVariable("NARAVEL_TEST_RABBITMQ")!);
        var credentials = uri.UserInfo.Split(':', 2);
        var factory = new ConnectionFactory
        {
            HostName = uri.Host,
            Port = uri.Port,
            UserName = credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0]) : "guest",
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "guest",
            VirtualHost = string.IsNullOrEmpty(uri.AbsolutePath.TrimStart('/'))
                ? "/"
                : Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            ClientProvidedName = "naravel-tests-cleanup"
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        foreach (var queue in UsedQueueNames)
        {
            await channel.QueueDeleteAsync($"netqueue.ready.{queue}");
            await channel.QueueDeleteAsync($"netqueue.delay.{queue}");
            await channel.QueueDeleteAsync($"netqueue.failed.{queue}");
        }
    }
}

public sealed class KafkaQueueProviderTests : QueueDriverContractTests
{
    private readonly ITestOutputHelper _output;

    public KafkaQueueProviderTests(ITestOutputHelper output) => _output = output;

    [ServiceFact("NARAVEL_TEST_KAFKA")]
    public Task Shared_contract() => RunContractAsync();

    [ServiceFact("NARAVEL_TEST_KAFKA")]
    public async Task Idle_consumer_of_another_queue_does_not_stall_a_new_queue()
    {
        var driver = CreateDriver();
        try
        {
            for (var trial = 1; trial <= 5; trial++)
            {
                var queue = QueueName($"idle-consumer-{trial}-{Guid.NewGuid():N}");
                var probe = new QueuedMessage
                {
                    Queue = queue,
                    JobType = "idle-consumer-regression",
                    Payload = "{}"
                };

                await driver.PushAsync(probe);
                var pushedAt = Stopwatch.GetTimestamp();
                var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
                var nullPolls = 0;
                QueuedMessage? popped = null;

                while (DateTimeOffset.UtcNow < deadline)
                {
                    popped = await driver.PopAsync(queue);
                    if (popped is not null) break;

                    nullPolls++;
                    await Task.Delay(TimeSpan.FromMilliseconds(200));
                }

                var elapsedMilliseconds = Stopwatch.GetElapsedTime(pushedAt).TotalMilliseconds;
                _output.WriteLine(
                    $"idle-consumer trial={trial} queue={queue} latencyMs={elapsedMilliseconds:F1} nullPolls={nullPolls} received={popped is not null}");

                popped.Should().NotBeNull($"a new queue must not be stalled by the idle consumers of earlier queues (probe {probe.Id}, trial {trial})");
                popped!.Id.Should().Be(probe.Id);
                await driver.AckAsync(popped);
            }
        }
        finally
        {
            if (driver is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (driver is IDisposable disposable)
                disposable.Dispose();
            await CleanupAsync();
        }
    }

    // CHARACTERIZATION (AUDIT D-29): pins the documented limitation that a delayed record at the head of a partition
    // blocks ready records behind it until it is due. It PASSES today. When the delay strategy is changed (R07.T11),
    // this test is expected to fail: replace it with one asserting the ready message is delivered first, and update the
    // limitation text in docs/en|fa/queue.md. Assumes a single-partition topic (CI and docker-compose set
    // KAFKA_NUM_PARTITIONS=1); with several partitions the two records may land on different partitions.
    [ServiceFact("NARAVEL_TEST_KAFKA")]
    public async Task Delayed_record_at_the_partition_head_blocks_ready_records_behind_it_until_due()
    {
        var driver = CreateDriver();
        try
        {
            var queue = QueueName("hol-delayed-" + Guid.NewGuid().ToString("N"));
            await WarmUpQueueAsync(driver, queue);

            var delayed = Msg(queue, delay: TimeSpan.FromSeconds(4));
            var ready = Msg(queue);
            await driver.PushAsync(delayed);
            await driver.PushAsync(ready);
            var pushedAt = Stopwatch.GetTimestamp();

            await AssertNothingDeliveredForAsync(driver, queue, TimeSpan.FromSeconds(2));

            var first = await PopEventuallyAsync(driver, queue, TimeSpan.FromSeconds(20));
            first.Should().NotBeNull();
            first!.Id.Should().Be(delayed.Id, "log order wins: the ready record had to wait behind the delayed one");
            Stopwatch.GetElapsedTime(pushedAt).Should().BeGreaterThan(TimeSpan.FromSeconds(3));
            await driver.AckAsync(first);

            var second = await PopEventuallyAsync(driver, queue, TimeSpan.FromSeconds(20));
            second.Should().NotBeNull();
            second!.Id.Should().Be(ready.Id);
            await driver.AckAsync(second);
        }
        finally
        {
            if (driver is IDisposable disposable) disposable.Dispose();
            await CleanupAsync();
        }
    }

    // CHARACTERIZATION (AUDIT D-29): a worker retry is re-produced to the tail with AvailableAt = now + backoff, so one
    // retrying job holds back every record produced after it for the whole backoff (5-60 s by default). Same expectations
    // as the test above.
    [ServiceFact("NARAVEL_TEST_KAFKA")]
    public async Task A_retry_backoff_blocks_records_produced_after_it_until_the_backoff_elapses()
    {
        var driver = CreateDriver();
        try
        {
            var queue = QueueName("hol-retry-" + Guid.NewGuid().ToString("N"));
            await WarmUpQueueAsync(driver, queue);

            var job = Msg(queue);
            await driver.PushAsync(job);
            var attempt = (await PopEventuallyAsync(driver, queue, TimeSpan.FromSeconds(20)))!;
            attempt.Attempts = 1;
            await driver.ReleaseAsync(attempt, TimeSpan.FromSeconds(4)); // the worker's retry with backoff

            var other = Msg(queue);                                      // an unrelated job produced afterwards
            await driver.PushAsync(other);
            var releasedAt = Stopwatch.GetTimestamp();

            await AssertNothingDeliveredForAsync(driver, queue, TimeSpan.FromSeconds(2));

            var first = await PopEventuallyAsync(driver, queue, TimeSpan.FromSeconds(20));
            first.Should().NotBeNull();
            first!.Id.Should().Be(job.Id);
            first.Attempts.Should().Be(1);
            Stopwatch.GetElapsedTime(releasedAt).Should().BeGreaterThan(TimeSpan.FromSeconds(3));
            await driver.AckAsync(first);

            var second = await PopEventuallyAsync(driver, queue, TimeSpan.FromSeconds(20));
            second!.Id.Should().Be(other.Id);
            await driver.AckAsync(second);
        }
        finally
        {
            if (driver is IDisposable disposable) disposable.Dispose();
            await CleanupAsync();
        }
    }

    private static async Task AssertNothingDeliveredForAsync(IQueueDriver driver, string queue, TimeSpan window)
    {
        var until = DateTimeOffset.UtcNow + window;
        while (DateTimeOffset.UtcNow < until)
        {
            (await driver.PopAsync(queue)).Should().BeNull("a not-yet-due record at the partition head blocks everything behind it (D-29)");
            await Task.Delay(200);
        }
    }

    protected override bool SupportsSize => false;

    protected override Task WarmUpQueueAsync(IQueueDriver driver, string queue) => WarmUpWithProbeAsync(driver, queue);

    protected override Task SettleBeforePriorityPopAsync() => Task.Delay(TimeSpan.FromSeconds(1));

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