using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Memory;
using Naravel.Queue.Serialization;
using Naravel.Queue.Worker;
using System.Text.Json;

namespace Naravel.Queue.Tests;

/// <summary>Thread-safe log jobs write to, keyed by run id, so parallel tests never see each other's data.</summary>
public static class Recorder
{
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<string>> Logs = new();

    public static void Add(string runId, string entry) => Logs.GetOrAdd(runId, _ => new()).Enqueue(entry);

    public static int Count(string runId, string entry) => Logs.TryGetValue(runId, out var q) ? q.Count(e => e == entry) : 0;
}

public class RecordingJob : Job
{
    public string RunId { get; set; } = "";
    public override Task HandleAsync(JobContext context, CancellationToken ct)
    {
        Recorder.Add(RunId, "run");
        return Task.CompletedTask;
    }
}

public class FlakyJob : Job
{
    public string RunId { get; set; } = "";
    public int FailTimes { get; set; }
    public override int MaxAttempts => 5;
    public override TimeSpan[] Backoff => new[] { TimeSpan.FromMilliseconds(10) };
    public override Task HandleAsync(JobContext context, CancellationToken ct)
    {
        Recorder.Add(RunId, "attempt");
        if (context.Attempt <= FailTimes) throw new InvalidOperationException("boom");
        Recorder.Add(RunId, "ok");
        return Task.CompletedTask;
    }
}

public class AlwaysFailingJob : Job
{
    public string RunId { get; set; } = "";
    public override int MaxAttempts => 2;
    public override TimeSpan[] Backoff => new[] { TimeSpan.FromMilliseconds(10) };
    public override Task HandleAsync(JobContext context, CancellationToken ct)
    {
        Recorder.Add(RunId, "attempt");
        throw new InvalidOperationException("always");
    }
    public override Task FailedAsync(JobContext context, Exception exception, CancellationToken ct)
    {
        Recorder.Add(RunId, "failed");
        return Task.CompletedTask;
    }
}

public class FailFirstExecutionsJob : Job
{
    public string RunId { get; set; } = "";
    public int FailuresBeforeSuccess { get; set; }
    public override int MaxAttempts => 2;
    public override TimeSpan[] Backoff => new[] { TimeSpan.FromMilliseconds(10) };

    public override Task HandleAsync(JobContext context, CancellationToken ct)
    {
        Recorder.Add(RunId, "attempt");
        if (Recorder.Count(RunId, "attempt") <= FailuresBeforeSuccess)
            throw new InvalidOperationException("not yet");

        Recorder.Add(RunId, "succeeded");
        return Task.CompletedTask;
    }
}

public class JobTimeoutJob : Job
{
    public string RunId { get; set; } = "";
    public override int MaxAttempts => 1;
    public override TimeSpan? Timeout => TimeSpan.FromMilliseconds(50);

    public override Task HandleAsync(JobContext context, CancellationToken ct)
        => Task.Delay(System.Threading.Timeout.Infinite, ct);

    public override Task FailedAsync(JobContext context, Exception exception, CancellationToken ct)
    {
        Recorder.Add(RunId, "failed");
        return Task.CompletedTask;
    }
}

public sealed class BatchGate
{
    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public static class BatchGates
{
    private static readonly ConcurrentDictionary<string, BatchGate> Gates = new();

    public static void Add(string id, BatchGate gate) => Gates[id] = gate;
    public static bool Remove(string id) => Gates.TryRemove(id, out _);
    public static BatchGate Get(string id) => Gates[id];
}

public class BlockingBatchJob : Job
{
    public string RunId { get; set; } = "";

    public override async Task HandleAsync(JobContext context, CancellationToken ct)
    {
        var gate = BatchGates.Get(RunId);
        gate.Started.TrySetResult(true);
        await gate.Continue.Task.WaitAsync(ct);
    }
}

public class TraceRecordingJob : Job
{
    public string RunId { get; set; } = "";

    public override Task HandleAsync(JobContext context, CancellationToken ct)
    {
        var activity = Activity.Current;
        Recorder.Add(RunId, $"{activity?.TraceId.ToString()}|{activity?.TraceStateString}");
        return Task.CompletedTask;
    }
}

public class ChainingJob : Job
{
    public string RunId { get; set; } = "";
    public override Task HandleAsync(JobContext context, CancellationToken ct)
    {
        Recorder.Add(RunId, "run");
        context.Then(new RecordingJob { RunId = RunId + "-next" });
        return Task.CompletedTask;
    }
}

public class RuntimeOptionsChainingJob : Job
{
    public string FollowupRunId { get; set; } = "";
    public string? FollowupConnection { get; set; }
    public string? FollowupQueue { get; set; }

    public override Task HandleAsync(JobContext context, CancellationToken ct)
    {
        context.Then(new QueueSettingsJob
        {
            RunId = FollowupRunId,
            DefaultConnection = FollowupConnection,
            DefaultQueue = FollowupQueue
        });
        return Task.CompletedTask;
    }
}

public class ConstructorSideEffectJob : Job
{
    public static int ConstructorCalls;

    public ConstructorSideEffectJob() => Interlocked.Increment(ref ConstructorCalls);

    public override Task HandleAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
}

public class QueueSettingsJob : Job
{
    public string RunId { get; set; } = "";
    public string? DefaultConnection { get; set; }
    public string? DefaultQueue { get; set; }

    public override string? Connection => DefaultConnection;
    public override string? Queue => DefaultQueue;
    public override int MaxAttempts => 2;

    public override Task HandleAsync(JobContext context, CancellationToken ct)
    {
        Recorder.Add(RunId, context.MaxAttempts.ToString());
        return Task.CompletedTask;
    }
}

public sealed class DeserializationCounter
{
    public int Count;
}

public sealed class CountingJobSerializer : IJobSerializer
{
    private readonly JsonJobSerializer _inner;
    private readonly DeserializationCounter _counter;

    public CountingJobSerializer(IJobTypeRegistry registry, DeserializationCounter counter)
    {
        _inner = new JsonJobSerializer(registry);
        _counter = counter;
    }

    public string GetTypeName(Type type) => _inner.GetTypeName(type);
    public Type ResolveType(string typeName) => _inner.ResolveType(typeName);
    public string Serialize(object job) => _inner.Serialize(job);

    public object Deserialize(string payload, Type type)
    {
        Interlocked.Increment(ref _counter.Count);
        return _inner.Deserialize(payload, type);
    }
}

/// <summary>Builds a real service provider + running worker on the in-memory driver with a unique connection.</summary>
public sealed class QueueTestHost : IAsyncDisposable
{
    public string Connection { get; } = "test-" + Guid.NewGuid().ToString("N");
    public ServiceProvider Services { get; }
    public IJobDispatcher Dispatcher => Services.GetRequiredService<IJobDispatcher>();
    private readonly List<IHostedService> _workers;

    public QueueTestHost(int concurrency = 1, IEnumerable<string>? additionalConnections = null, string? workerConnection = null, string[]? queues = null, Action<IServiceCollection>? configureServices = null, Action<QueueWorkerOptions>? configureWorker = null)
    {
        var connections = new[] { Connection }.Concat(additionalConnections ?? Array.Empty<string>()).Distinct().ToArray();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = Connection,
            [$"NaravelQueue:Stores:{Connection}:Driver"] = "memory",
        }).Build();
        foreach (var connection in connections.Skip(1))
            config[$"NaravelQueue:Stores:{connection}:Driver"] = "memory";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueue(config).AddMemoryDriver(config);
        configureServices?.Invoke(services);
        services.AddQueueWorker(o =>
        {
            o.Connection = workerConnection ?? Connection;
            o.Queues = queues ?? new[] { "q" };
            o.Concurrency = concurrency;
            o.SleepWhenEmpty = TimeSpan.FromMilliseconds(15);
            configureWorker?.Invoke(o);
        });
        Services = services.BuildServiceProvider();
        _workers = Services.GetServices<IHostedService>().ToList();
        foreach (var w in _workers) w.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task<string> Dispatch(IJob job, Action<DispatchOptions>? configure = null) =>
        Dispatcher.DispatchAsync(job, o => { o.OnQueue("q"); configure?.Invoke(o); });

    public async ValueTask DisposeAsync()
    {
        foreach (var w in _workers) await w.StopAsync(CancellationToken.None);
        await Services.DisposeAsync();
    }
}

public static class Wait
{
    public static async Task<bool> Until(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    public static async Task<bool> UntilAsync(Func<Task<bool>> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(10);
        }
        return await condition();
    }
}
