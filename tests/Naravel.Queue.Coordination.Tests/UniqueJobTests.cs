using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Testing;
using Naravel.Queue.Tests;
using Naravel.Testing;

namespace Naravel.Queue.Coordination.Tests;

/// <summary>
/// PDR-012b / R08.T02b: <see cref="IUniqueJob"/> through a real worker. Memory-backed Cache proves behavior inside one process;
/// the env-gated Redis test proves that a second worker process-equivalent is rejected too.
/// </summary>
public class UniqueJobTests
{
    private static string NewId() => Guid.NewGuid().ToString("N");

    public class UniqueJobUnderTest : Job, IUniqueJob
    {
        public string RunId { get; set; } = "";
        public string Key { get; set; } = "";

        /// <summary><c>ok</c>, <c>fail</c> (fails permanently) or <c>fail-once</c> (fails the first attempt, then succeeds after the backoff).</summary>
        public string Mode { get; set; } = "ok";
        public int RunMilliseconds { get; set; } = 200;
        public bool UntilProcessing { get; set; }

        public string UniqueKey => Key;
        public TimeSpan UniqueFor => TimeSpan.FromSeconds(30);
        public bool UniqueUntilProcessing => UntilProcessing;

        public override int MaxAttempts => Mode == "fail" ? 1 : 3;
        public override TimeSpan[] Backoff => new[] { TimeSpan.FromMilliseconds(400) };

        public override async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
        {
            Recorder.Add(RunId, "start");
            if (Mode == "fail" || (Mode == "fail-once" && context.Attempt == 1))
                throw new InvalidOperationException("boom");
            await Task.Delay(RunMilliseconds, cancellationToken);
            Recorder.Add(RunId, "run");
        }
    }

    private static void AddMemoryCoordination(IServiceCollection services)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Default"] = "memory",
        }).Build();
        services.AddNaravelCache(config);
        services.AddQueueCoordination();
        services.AddJob<UniqueJobUnderTest>();
    }

    private static QueueTestHost NewHost() => new(concurrency: 2, configureServices: AddMemoryCoordination);

    [Fact]
    public async Task A_duplicate_dispatch_is_rejected_with_null_while_the_first_is_pending_and_the_key_frees_afterwards()
    {
        await using var host = NewHost();
        var key = NewId();
        var first = NewId();
        var duplicate = NewId();

        (await host.TryDispatch(new UniqueJobUnderTest { RunId = first, Key = key })).Should().NotBeNull();
        (await host.TryDispatch(new UniqueJobUnderTest { RunId = duplicate, Key = key })).Should().BeNull("the first dispatch still holds the key");

        (await Wait.Until(() => Recorder.Count(first, "run") == 1)).Should().BeTrue();
        Recorder.Count(duplicate, "start").Should().Be(0, "a rejected dispatch never reaches the queue");

        var again = NewId();
        (await Wait.UntilAsync(async () => await host.TryDispatch(new UniqueJobUnderTest { RunId = again, Key = key }) is not null))
            .Should().BeTrue("the worker releases the key once the first job finished");
        (await Wait.Until(() => Recorder.Count(again, "run") == 1)).Should().BeTrue();
    }

    [Fact]
    public async Task Different_keys_do_not_block_each_other()
    {
        await using var host = NewHost();
        var a = NewId();
        var b = NewId();

        (await host.TryDispatch(new UniqueJobUnderTest { RunId = a, Key = NewId() })).Should().NotBeNull();
        (await host.TryDispatch(new UniqueJobUnderTest { RunId = b, Key = NewId() })).Should().NotBeNull();

        (await Wait.Until(() => Recorder.Count(a, "run") == 1 && Recorder.Count(b, "run") == 1)).Should().BeTrue();
    }

    [Fact]
    public async Task UntilProcessing_frees_the_key_as_soon_as_the_job_starts()
    {
        await using var host = NewHost();
        var key = NewId();
        var running = NewId();
        var next = NewId();

        await host.Dispatch(new UniqueJobUnderTest { RunId = running, Key = key, UntilProcessing = true, RunMilliseconds = 1500 });
        (await Wait.Until(() => Recorder.Count(running, "start") == 1)).Should().BeTrue();

        (await host.TryDispatch(new UniqueJobUnderTest { RunId = next, Key = key, UntilProcessing = true, RunMilliseconds = 10 }))
            .Should().NotBeNull("the first job is already running, so a new one may queue");
    }

    [Fact]
    public async Task The_key_stays_locked_while_a_failed_job_waits_for_its_retry()
    {
        await using var host = NewHost();
        var key = NewId();
        var id = NewId();

        await host.Dispatch(new UniqueJobUnderTest { RunId = id, Key = key, Mode = "fail-once", RunMilliseconds = 10 });
        (await Wait.Until(() => Recorder.Count(id, "start") == 1)).Should().BeTrue();

        (await host.TryDispatch(new UniqueJobUnderTest { RunId = NewId(), Key = key }))
            .Should().BeNull("the job is only waiting to be retried, it is not finished");

        (await Wait.Until(() => Recorder.Count(id, "run") == 1, timeoutMs: 5000)).Should().BeTrue("the retry succeeds after the backoff");
    }

    [Fact]
    public async Task The_key_is_freed_when_a_job_fails_permanently()
    {
        await using var host = NewHost();
        var key = NewId();
        var id = NewId();

        await host.Dispatch(new UniqueJobUnderTest { RunId = id, Key = key, Mode = "fail" });
        (await Wait.Until(() => Recorder.Count(id, "start") == 1)).Should().BeTrue();

        (await Wait.UntilAsync(async () => await host.TryDispatch(new UniqueJobUnderTest { RunId = NewId(), Key = key, RunMilliseconds = 10 }) is not null))
            .Should().BeTrue("a permanently failed job must not block its key forever");
    }

    [Fact]
    public async Task Dispatching_a_unique_job_without_a_gate_throws_instead_of_ignoring_uniqueness()
    {
        await using var host = new QueueTestHost(configureServices: services => services.AddJob<UniqueJobUnderTest>());

        var act = () => host.Dispatch(new UniqueJobUnderTest { RunId = NewId(), Key = NewId() });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddQueueCoordination*");
    }

    [Fact]
    public async Task Unique_jobs_cannot_join_a_chain_or_a_batch()
    {
        await using var host = NewHost();
        var unique = new UniqueJobUnderTest { RunId = NewId(), Key = NewId() };

        FluentActions.Invoking(() => host.Dispatcher.Chain(unique)).Should().Throw<ArgumentException>().WithMessage("*IUniqueJob*");
        await FluentActions.Awaiting(() => host.Dispatcher.BatchAsync(new[] { unique })).Should().ThrowAsync<ArgumentException>().WithMessage("*IUniqueJob*");
    }

    [Fact]
    public async Task QueueFake_mimics_the_rejection_and_supports_AssertNotDispatched()
    {
        var fake = new QueueFake();
        var key = NewId();

        (await fake.DispatchAsync(new UniqueJobUnderTest { Key = key })).Should().NotBeNull();
        (await fake.DispatchAsync(new UniqueJobUnderTest { Key = key })).Should().BeNull();

        fake.AssertDispatchedCount<UniqueJobUnderTest>(1);
        fake.AssertNotDispatched<OtherJob>();
        FluentActions.Invoking(() => fake.AssertNotDispatched<UniqueJobUnderTest>()).Should().Throw<InvalidOperationException>();

        fake.Reset();
        (await fake.DispatchAsync(new UniqueJobUnderTest { Key = key })).Should().NotBeNull("Reset clears the fake's unique keys");
    }

    private sealed class OtherJob : IJob
    {
        public Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task Redis_gate_rejects_a_duplicate_dispatched_from_another_worker()
    {
        var redis = Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!;
        void AddRedisCoordination(IServiceCollection services)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Default"] = "shared",
                ["Cache:Stores:shared:Driver"] = "redis",
                ["Cache:Stores:shared:ConnectionString"] = redis,
            }).Build();
            services.AddNaravelCache(config);
            services.AddNaravelRedisCache(config);
            services.AddQueueCoordination();
            services.AddJob<UniqueJobUnderTest>();
        }

        // Two hosts = two separate cache clients; only Redis connects them.
        await using var workerA = new QueueTestHost(configureServices: AddRedisCoordination);
        await using var workerB = new QueueTestHost(configureServices: AddRedisCoordination);
        var key = NewId();
        var a = NewId();

        (await workerA.TryDispatch(new UniqueJobUnderTest { RunId = a, Key = key, RunMilliseconds = 1500 })).Should().NotBeNull();
        (await workerB.TryDispatch(new UniqueJobUnderTest { RunId = NewId(), Key = key })).Should().BeNull("the key is held in Redis, visible to both");

        (await Wait.Until(() => Recorder.Count(a, "run") == 1, timeoutMs: 15000)).Should().BeTrue();
        (await Wait.UntilAsync(async () => await workerB.TryDispatch(new UniqueJobUnderTest { RunId = NewId(), Key = key, RunMilliseconds = 10 }) is not null, timeoutMs: 15000))
            .Should().BeTrue("the key is released in Redis after the job finished");
    }
}
