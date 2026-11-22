using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Naravel.Queue.Batching;
using Naravel.Queue.Database;
using Naravel.Queue.Redis;
using Naravel.Testing;
using StackExchange.Redis;

namespace Naravel.Queue.Providers.Tests;

/// <summary>
/// Owner decision AD-019: a persistent batch repository can delete the records of finished batches on request, and never deletes a batch that is
/// still running or whose callbacks have not all been delivered (RQR-005). The same scenario runs against the Redis and the database repository.
/// </summary>
internal static class BatchPruneScenario
{
    public static async Task RunAsync(Func<BatchCallbackRegistry, IPrunableBatchRepository> create)
    {
        var delivered = new List<string>();
        var flakyShouldFail = true;
        var registry = new BatchCallbackRegistry();
        registry.Register<string>("note", (_, payload, _) =>
        {
            lock (delivered) delivered.Add(payload);
            return Task.CompletedTask;
        });
        registry.Register<string>("flaky", (_, payload, _) =>
        {
            if (flakyShouldFail) throw new InvalidOperationException("the callback failed");
            lock (delivered) delivered.Add(payload);
            return Task.CompletedTask;
        });
        var repository = create(registry);
        var token = CancellationToken.None;

        // A: still running (one of two jobs counted).
        var running = new QueueBatch { TotalJobs = 2 };
        await repository.RegisterAsync(running, new BatchOptions(), token);
        await repository.MarkJobCompletedAsync(running.Id, token);

        // B: finished, and its then and finally callbacks were delivered.
        var delivered1 = new QueueBatch { TotalJobs = 1 };
        await repository.RegisterAsync(delivered1, new BatchOptions().Then("note", "B-then").Finally("note", "B-finally"), token);
        await repository.MarkJobCompletedAsync(delivered1.Id, token);

        // C: finished, but its then callback throws, so neither it nor the finally callback was delivered.
        var pending = new QueueBatch { TotalJobs = 1 };
        await repository.RegisterAsync(pending, new BatchOptions().Then("flaky", "C-then").Finally("note", "C-finally"), token);
        var markPending = () => repository.MarkJobCompletedAsync(pending.Id, token);
        await markPending.Should().ThrowAsync<InvalidOperationException>().WithMessage("the callback failed");

        // D: a job failed, so the catch and finally callbacks apply (the then callbacks do not).
        var failed = new QueueBatch { TotalJobs = 1 };
        await repository.RegisterAsync(failed, new BatchOptions().Then("note", "D-then").Catch("note", "D-catch").Finally("note", "D-finally"), token);
        await repository.MarkJobFailedAsync(failed.Id, new InvalidOperationException("job failed"), token);

        // E: cancelled, so the then callbacks are skipped and only the finally callbacks apply.
        var cancelled = new QueueBatch { TotalJobs = 2 };
        await repository.RegisterAsync(cancelled, new BatchOptions().Then("note", "E-then").Finally("note", "E-finally"), token);
        await repository.CancelAsync(cancelled.Id, token);
        await repository.MarkJobCancelledAsync(cancelled.Id, token);
        await repository.MarkJobCompletedAsync(cancelled.Id, token);

        // F: the catch callback fails. Neither Catch nor Finally may be marked complete.
        var catchCallbackFailed = new QueueBatch { TotalJobs = 1 };
        await repository.RegisterAsync(catchCallbackFailed, new BatchOptions().Catch("flaky", "F-catch").Finally("note", "F-finally"), token);
        var markCatchFailed = () => repository.MarkJobFailedAsync(catchCallbackFailed.Id, new InvalidOperationException("job failed"), token);
        await markCatchFailed.Should().ThrowAsync<InvalidOperationException>().WithMessage("the callback failed");

        // G: Catch succeeds, but Finally fails. The batch must remain until Finally is retried.
        var finallyCallbackFailed = new QueueBatch { TotalJobs = 1 };
        await repository.RegisterAsync(finallyCallbackFailed, new BatchOptions().Catch("note", "G-catch").Finally("flaky", "G-finally"), token);
        var markFinallyFailed = () => repository.MarkJobFailedAsync(finallyCallbackFailed.Id, new InvalidOperationException("job failed"), token);
        await markFinallyFailed.Should().ThrowAsync<InvalidOperationException>().WithMessage("the callback failed");

        lock (delivered) delivered.Should().BeEquivalentTo(new[] { "B-then", "B-finally", "D-catch", "D-finally", "E-finally", "G-catch" });

        // The first call removes exactly the three batches that are finished with every callback delivered.
        (await repository.PruneFinishedAsync(token)).Should().Be(3);
        (await repository.GetAsync(delivered1.Id, token)).Should().BeNull();
        (await repository.GetAsync(failed.Id, token)).Should().BeNull();
        (await repository.GetAsync(cancelled.Id, token)).Should().BeNull();
        (await repository.GetAsync(running.Id, token)).Should().NotBeNull("a running batch is never deleted");
        (await repository.GetAsync(pending.Id, token)).Should().NotBeNull("a finished batch with undelivered callbacks is never deleted");
        (await repository.GetAsync(catchCallbackFailed.Id, token)).Should().NotBeNull("a failed Catch callback must remain retryable");
        (await repository.GetAsync(finallyCallbackFailed.Id, token)).Should().NotBeNull("a failed Finally callback must remain retryable");
        (await repository.PruneFinishedAsync(token)).Should().Be(0, "nothing else qualifies");

        // After a restart the undelivered callbacks of C are still found and delivered; only then does C qualify.
        flakyShouldFail = false;
        var restarted = create(registry);
        await restarted.ResumePendingCallbacksAsync(token);
        lock (delivered) delivered.Should().Contain("C-then").And.Contain("C-finally");
        (await restarted.PruneFinishedAsync(token)).Should().Be(3);
        (await restarted.GetAsync(pending.Id, token)).Should().BeNull();
        (await restarted.GetAsync(catchCallbackFailed.Id, token)).Should().BeNull();
        (await restarted.GetAsync(finallyCallbackFailed.Id, token)).Should().BeNull();
        (await restarted.GetAsync(running.Id, token)).Should().NotBeNull();

        // A is pruned only after it has finished.
        await restarted.MarkJobCompletedAsync(running.Id, token);
        (await restarted.PruneFinishedAsync(token)).Should().Be(1);
        (await restarted.GetAsync(running.Id, token)).Should().BeNull();

        // A token that is already cancelled is refused before any request is sent.
        using var source = new CancellationTokenSource();
        source.Cancel();
        var cancelledPrune = () => restarted.PruneFinishedAsync(source.Token);
        await cancelledPrune.Should().ThrowAsync<OperationCanceledException>();
    }
}

public sealed class DatabaseBatchPruneTests : IDisposable
{
    private readonly string _databasePath = System.IO.Path.Join(
        System.IO.Path.GetTempPath(),
        $"naravel-batch-prune-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<SqliteQueueContext> _options;

    public DatabaseBatchPruneTests()
    {
        _options = new DbContextOptionsBuilder<SqliteQueueContext>().UseSqlite($"Data Source={_databasePath}").Options;
        using var context = new SqliteQueueContext(_options);
        context.Database.EnsureCreated();
    }

    [Fact]
    public Task Prune_deletes_only_finished_batches_whose_callbacks_were_delivered()
        => BatchPruneScenario.RunAsync(registry => new DatabaseBatchRepository<SqliteQueueContext>(new SqliteQueueContextFactory(_options), registry));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { System.IO.File.Delete(_databasePath); } catch (IOException) { /* a leftover temp file is harmless */ }
    }
}

public sealed class RedisBatchPruneTests
{
    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task Prune_deletes_only_finished_batches_whose_callbacks_were_delivered()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        var keyPrefix = $"netqueue:tests:batch-prune:{Guid.NewGuid():N}";
        var database = multiplexer.GetDatabase();
        try
        {
            await BatchPruneScenario.RunAsync(registry => new RedisBatchRepository(multiplexer, registry, keyPrefix));
            (await database.SetMembersAsync($"{keyPrefix}:index")).Should().BeEmpty("every batch of the scenario was pruned, and so was its index entry");
        }
        finally
        {
            foreach (var id in await database.SetMembersAsync($"{keyPrefix}:index"))
                await database.KeyDeleteAsync($"{keyPrefix}:{id}");
            await database.KeyDeleteAsync($"{keyPrefix}:index");
        }
    }

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task Prune_removes_an_index_entry_whose_batch_hash_is_gone_and_does_not_count_it()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        var keyPrefix = $"netqueue:tests:batch-prune:{Guid.NewGuid():N}";
        var database = multiplexer.GetDatabase();
        var repository = new RedisBatchRepository(multiplexer, new BatchCallbackRegistry(), keyPrefix);
        try
        {
            await database.SetAddAsync($"{keyPrefix}:index", "orphan");

            (await repository.PruneFinishedAsync(CancellationToken.None)).Should().Be(0);

            (await database.SetMembersAsync($"{keyPrefix}:index")).Should().BeEmpty();
        }
        finally
        {
            await database.KeyDeleteAsync($"{keyPrefix}:index");
        }
    }
}
