using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Redis;
using Naravel.Queue.Coordination;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Middleware;
using Naravel.Queue.Tests;
using Naravel.Testing;

namespace Naravel.Queue.Coordination.Tests;

/// <summary>
/// PDR-012b / R08.T02: coordination middleware run through a real worker. Memory-backed Cache stores prove behavior inside one
/// process; <see cref="Redis_lock_stops_two_workers_from_running_the_same_key_at_once"/> proves it across two workers.
/// </summary>
public class CoordinationMiddlewareTests
{
    private static string NewId() => Guid.NewGuid().ToString("N");

    private sealed class Counter
    {
        public int Running;
        public int Max;
    }

    private static class Concurrency
    {
        private static readonly ConcurrentDictionary<string, Counter> Counters = new();

        public static Counter For(string group) => Counters.GetOrAdd(group, _ => new Counter());

        public static async Task RunAsync(string group, TimeSpan duration, CancellationToken cancellationToken)
        {
            var counter = For(group);
            var running = Interlocked.Increment(ref counter.Running);
            lock (counter) counter.Max = Math.Max(counter.Max, running);
            try { await Task.Delay(duration, cancellationToken); }
            finally { Interlocked.Decrement(ref counter.Running); }
        }
    }

    public class OverlapJob : Job, IHasJobMiddleware
    {
        public string RunId { get; set; } = "";
        public string Key { get; set; } = "";

        /// <summary>The name of the concurrency counter; defaults to <see cref="Key"/> so jobs with different keys can share one counter.</summary>
        public string Group { get; set; } = "";
        public string Mode { get; set; } = "release";
        public int RunMilliseconds { get; set; } = 150;
        public override int MaxAttempts => 3;
        public override TimeSpan[] Backoff => new[] { TimeSpan.FromMilliseconds(10) };

        public IEnumerable<IJobMiddleware> Middleware(JobContext context)
        {
            var middleware = new WithoutOverlapping(Key).ReleaseAfter(TimeSpan.FromMilliseconds(40));
            if (Mode == "skip") middleware.DontRelease();
            return new IJobMiddleware[] { middleware };
        }

        public override async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
        {
            Recorder.Add(RunId, "start");
            Recorder.Add(RunId, $"attempt-{context.Attempt}");
            if (Mode == "throw-once" && context.Attempt == 1) throw new InvalidOperationException("boom");
            await Concurrency.RunAsync(string.IsNullOrEmpty(Group) ? Key : Group, TimeSpan.FromMilliseconds(RunMilliseconds), cancellationToken);
            Recorder.Add(RunId, "run");
        }
    }

    public class LimitedJob : Job, IHasJobMiddleware
    {
        public string RunId { get; set; } = "";
        public string Limiter { get; set; } = "";
        public override int MaxAttempts => 1;

        public IEnumerable<IJobMiddleware> Middleware(JobContext context)
            => new IJobMiddleware[] { new RateLimited(Limiter, maxAttempts: 2, window: TimeSpan.FromSeconds(2)) };

        public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
        {
            Recorder.Add(RunId, "run");
            return Task.CompletedTask;
        }
    }

    public class UnstableJob : Job, IHasJobMiddleware
    {
        public string RunId { get; set; } = "";
        public override int MaxAttempts => 20;
        public override TimeSpan[] Backoff => new[] { TimeSpan.FromMilliseconds(10) };

        public IEnumerable<IJobMiddleware> Middleware(JobContext context)
            => new IJobMiddleware[] { new ThrottlesExceptions(2, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(700), key: RunId) };

        public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
        {
            Recorder.Add(RunId, "run");
            throw new InvalidOperationException("downstream is down");
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
        services.AddJob<OverlapJob>();
        services.AddJob<LimitedJob>();
        services.AddJob<UnstableJob>();
    }

    private static QueueTestHost NewHost(int concurrency = 2)
        => new(concurrency: concurrency, configureServices: AddMemoryCoordination);

    [Fact]
    public async Task WithoutOverlapping_runs_jobs_with_the_same_key_one_at_a_time_and_does_not_consume_attempts()
    {
        await using var host = NewHost();
        var key = NewId();
        var first = NewId();
        var second = NewId();
        await host.Dispatch(new OverlapJob { RunId = first, Key = key });
        await host.Dispatch(new OverlapJob { RunId = second, Key = key });

        (await Wait.Until(() => Recorder.Count(first, "run") == 1 && Recorder.Count(second, "run") == 1)).Should().BeTrue();
        Concurrency.For(key).Max.Should().Be(1, "the second job had to wait for the lock");
        Recorder.Count(first, "attempt-2").Should().Be(0);
        Recorder.Count(second, "attempt-2").Should().Be(0, "releasing a blocked job does not use an attempt");
    }

    [Fact]
    public async Task WithoutOverlapping_lets_jobs_with_different_keys_run_together()
    {
        await using var host = NewHost();
        var group = NewId();
        var first = NewId();
        var second = NewId();
        await host.Dispatch(new OverlapJob { RunId = first, Key = NewId(), Group = group, RunMilliseconds = 400 });
        await host.Dispatch(new OverlapJob { RunId = second, Key = NewId(), Group = group, RunMilliseconds = 400 });

        (await Wait.Until(() => Recorder.Count(first, "run") == 1 && Recorder.Count(second, "run") == 1)).Should().BeTrue();
        Concurrency.For(group).Max.Should().Be(2, "different keys do not block each other");
    }

    [Fact]
    public async Task WithoutOverlapping_can_drop_a_blocked_job_instead_of_releasing_it()
    {
        await using var host = NewHost();
        var key = NewId();
        var holder = NewId();
        var dropped = NewId();
        await host.Dispatch(new OverlapJob { RunId = holder, Key = key, RunMilliseconds = 500 });
        (await Wait.Until(() => Recorder.Count(holder, "start") == 1)).Should().BeTrue();
        await host.Dispatch(new OverlapJob { RunId = dropped, Key = key, Mode = "skip" });

        (await Wait.Until(() => Recorder.Count(holder, "run") == 1)).Should().BeTrue();
        await Task.Delay(300);
        Recorder.Count(dropped, "run").Should().Be(0);
        (await host.Services.GetRequiredService<QueueManager>().Connection(host.Connection).SizeAsync("q"))
            .Should().Be(0, "the dropped job was acknowledged, not left in the queue");
    }

    [Fact]
    public async Task WithoutOverlapping_releases_its_lock_when_the_job_throws()
    {
        await using var host = NewHost(concurrency: 1);
        var id = NewId();
        await host.Dispatch(new OverlapJob { RunId = id, Key = NewId(), Mode = "throw-once", RunMilliseconds = 10 });

        // If the lock leaked, the retry would be blocked until the 15 minute lock lifetime ends.
        (await Wait.Until(() => Recorder.Count(id, "run") == 1)).Should().BeTrue();
        Recorder.Count(id, "attempt-2").Should().Be(1);
    }

    [Fact]
    public async Task RateLimited_holds_extra_jobs_until_the_window_ends_without_consuming_attempts()
    {
        await using var host = NewHost();
        var limiter = NewId();
        var ids = Enumerable.Range(0, 4).Select(_ => NewId()).ToArray();
        foreach (var id in ids) await host.Dispatch(new LimitedJob { RunId = id, Limiter = limiter });

        (await Wait.Until(() => ids.Count(id => Recorder.Count(id, "run") == 1) == 2, timeoutMs: 1500)).Should().BeTrue();
        await Task.Delay(400);
        ids.Count(id => Recorder.Count(id, "run") == 1).Should().Be(2, "only two jobs fit in the first window");

        // MaxAttempts is 1: if a release used an attempt the held jobs would have failed instead of running later.
        (await Wait.Until(() => ids.All(id => Recorder.Count(id, "run") == 1), timeoutMs: 10000)).Should().BeTrue();
    }

    [Fact]
    public async Task ThrottlesExceptions_stops_running_jobs_while_the_circuit_is_open()
    {
        await using var host = NewHost();
        var id = NewId();
        await host.Dispatch(new UnstableJob { RunId = id });

        (await Wait.Until(() => Recorder.Count(id, "run") >= 2)).Should().BeTrue();
        await Task.Delay(300);
        Recorder.Count(id, "run").Should().Be(2, "after two exceptions the circuit is open and the job is held back, not retried every 10 ms");

        (await Wait.Until(() => Recorder.Count(id, "run") >= 3, timeoutMs: 5000)).Should().BeTrue("the job runs again once the circuit closes");
    }

    [Fact]
    public void Registration_fails_at_startup_with_a_clear_message_when_Cache_is_not_registered()
    {
        var act = () => new QueueTestHost(configureServices: services => services.AddQueueCoordination());

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddNaravelCache*");
    }

    [Fact]
    public void Middleware_rejects_invalid_settings()
    {
        FluentActions.Invoking(() => new WithoutOverlapping(" ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new WithoutOverlapping("k").ExpireAfter(TimeSpan.Zero)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new RateLimited("l", 0, TimeSpan.FromSeconds(1))).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new ThrottlesExceptions(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task Redis_lock_stops_two_workers_from_running_the_same_key_at_once()
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
            services.AddJob<OverlapJob>();
        }

        // Two hosts = two workers with their own queue and cache client; only the Redis lock connects them.
        await using var workerA = new QueueTestHost(configureServices: AddRedisCoordination);
        await using var workerB = new QueueTestHost(configureServices: AddRedisCoordination);
        var key = NewId();
        var a = NewId();
        var b = NewId();
        await workerA.Dispatch(new OverlapJob { RunId = a, Key = key });
        await workerB.Dispatch(new OverlapJob { RunId = b, Key = key });

        (await Wait.Until(() => Recorder.Count(a, "run") == 1 && Recorder.Count(b, "run") == 1, timeoutMs: 15000)).Should().BeTrue();
        Concurrency.For(key).Max.Should().Be(1, "the Redis lock must exclude the other worker");
    }
}
