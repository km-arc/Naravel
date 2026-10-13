using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Middleware;

namespace Naravel.Queue.Tests;

/// <summary>PDR-012b section 2 / R08.T02: explicit middleware outcomes and per-job middleware.</summary>
public class MiddlewareOutcomeTests
{
    private static string NewId() => Guid.NewGuid().ToString("N");

    private static class Order
    {
        private static readonly ConcurrentDictionary<string, ConcurrentQueue<string>> Logs = new();
        public static void Add(string runId, string entry) => Logs.GetOrAdd(runId, _ => new()).Enqueue(entry);
        public static string[] Get(string runId) => Logs.TryGetValue(runId, out var q) ? q.ToArray() : Array.Empty<string>();
    }

    private sealed class GlobalMiddleware : IJobMiddleware
    {
        public async Task InvokeAsync(JobContext context, Func<Task> next)
        {
            if (context.Message.JobType == typeof(OutcomeJob).FullName!)
            {
                var id = System.Text.Json.JsonSerializer.Deserialize<OutcomeJob>(context.Message.Payload)!.RunId;
                Order.Add(id, "global");
            }
            await next();
        }
    }

    private sealed class ModeMiddleware(string runId, string mode) : IJobMiddleware
    {
        public async Task InvokeAsync(JobContext context, Func<Task> next)
        {
            Order.Add(runId, "job-middleware");
            switch (mode)
            {
                case "none": return;
                case "release-once":
                    if (Recorder.Count(runId, "released") == 0)
                    {
                        Recorder.Add(runId, "released");
                        context.Release(TimeSpan.FromMilliseconds(50));
                        return;
                    }
                    break;
                case "skip":
                    context.Skip();
                    return;
            }
            await next();
        }
    }

    public class OutcomeJob : Job, IHasJobMiddleware
    {
        public string RunId { get; set; } = "";
        public string Mode { get; set; } = "next";
        public override int MaxAttempts => 1;
        public IEnumerable<IJobMiddleware> Middleware(JobContext context) => new IJobMiddleware[] { new ModeMiddleware(RunId, Mode) };

        public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
        {
            Order.Add(RunId, "handle");
            Recorder.Add(RunId, "run");
            Recorder.Add(RunId, $"attempt-{context.Attempt}");
            return Task.CompletedTask;
        }

        public override Task FailedAsync(JobContext context, Exception exception, CancellationToken cancellationToken)
        {
            Recorder.Add(RunId, "failed");
            Recorder.Add(RunId, "failed:" + exception.Message.Contains("without calling next"));
            return Task.CompletedTask;
        }
    }

    private static QueueTestHost NewHost() => new(configureServices: s =>
    {
        s.AddJob<OutcomeJob>();
        s.AddSingleton<IJobMiddleware, GlobalMiddleware>();
    });

    [Fact]
    public async Task Per_job_middleware_runs_inside_the_global_middleware()
    {
        await using var host = NewHost();
        var id = NewId();
        await host.Dispatch(new OutcomeJob { RunId = id, Mode = "next" });

        (await Wait.Until(() => Recorder.Count(id, "run") == 1)).Should().BeTrue();
        Order.Get(id).Should().Equal("global", "job-middleware", "handle");
    }

    [Fact]
    public async Task A_middleware_that_returns_without_next_or_an_outcome_fails_the_job_instead_of_succeeding()
    {
        await using var host = NewHost();
        var id = NewId();
        await host.Dispatch(new OutcomeJob { RunId = id, Mode = "none" });

        (await Wait.Until(() => Recorder.Count(id, "failed") == 1)).Should().BeTrue();
        Recorder.Count(id, "failed:True").Should().Be(1);
        Recorder.Count(id, "run").Should().Be(0);
    }

    [Fact]
    public async Task Release_hands_the_job_back_without_counting_an_attempt()
    {
        await using var host = NewHost();
        var id = NewId();
        await host.Dispatch(new OutcomeJob { RunId = id, Mode = "release-once" });

        (await Wait.Until(() => Recorder.Count(id, "run") == 1)).Should().BeTrue();
        Recorder.Count(id, "released").Should().Be(1);
        Recorder.Count(id, "attempt-1").Should().Be(1, "a released job is still on its first attempt");
        Recorder.Count(id, "failed").Should().Be(0);
    }

    [Fact]
    public async Task Skip_acknowledges_the_job_without_running_or_failing_it()
    {
        await using var host = NewHost();
        var id = NewId();
        var follow = NewId();
        await host.Dispatch(new OutcomeJob { RunId = id, Mode = "skip" });
        await host.Dispatch(new OutcomeJob { RunId = follow, Mode = "next" });

        (await Wait.Until(() => Recorder.Count(follow, "run") == 1)).Should().BeTrue("the worker keeps going after a skip");
        Recorder.Count(id, "run").Should().Be(0);
        Recorder.Count(id, "failed").Should().Be(0);
        var manager = host.Services.GetRequiredService<QueueManager>();
        (await manager.Connection(host.Connection).SizeAsync("q")).Should().Be(0, "the skipped message was acknowledged");
    }
}
