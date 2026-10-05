using System.Text.Json;
using System.Text.Json.Serialization;
using Naravel.Queue.Batching;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using Naravel.Queue.Events;
using Naravel.Queue.Jobs;
using Naravel.Queue.Serialization;

namespace Naravel.Queue.Tests;

public class WorkerTests
{
    private static string NewId() => Guid.NewGuid().ToString("N");

    [Fact]
    public async Task A_dispatched_job_runs_exactly_once()
    {
        await using var host = new QueueTestHost();
        var id = NewId();
        await host.Dispatch(new RecordingJob { RunId = id });

        (await Wait.Until(() => Recorder.Count(id, "run") == 1)).Should().BeTrue();
        await Task.Delay(150);
        Recorder.Count(id, "run").Should().Be(1);
    }

    [Fact]
    public async Task A_failing_job_is_retried_until_it_succeeds()
    {
        await using var host = new QueueTestHost();
        var id = NewId();
        await host.Dispatch(new FlakyJob { RunId = id, FailTimes = 2 });

        (await Wait.Until(() => Recorder.Count(id, "ok") == 1)).Should().BeTrue();
        Recorder.Count(id, "attempt").Should().Be(3);
    }

    [Fact]
    public async Task A_job_that_exhausts_its_attempts_calls_FailedAsync_once_and_stops()
    {
        await using var host = new QueueTestHost();
        var id = NewId();
        await host.Dispatch(new AlwaysFailingJob { RunId = id });

        (await Wait.Until(() => Recorder.Count(id, "failed") == 1)).Should().BeTrue();
        await Task.Delay(200);
        Recorder.Count(id, "attempt").Should().Be(2);
        Recorder.Count(id, "failed").Should().Be(1);
    }

    [Fact]
    public async Task A_delayed_job_does_not_run_before_its_time()
    {
        await using var host = new QueueTestHost();
        var id = NewId();
        await host.Dispatch(new RecordingJob { RunId = id }, o => o.DelayFor(TimeSpan.FromMilliseconds(500)));

        await Task.Delay(200);
        Recorder.Count(id, "run").Should().Be(0);
        (await Wait.Until(() => Recorder.Count(id, "run") == 1)).Should().BeTrue();
    }

    [Fact]
    public async Task Runtime_chaining_runs_the_follow_up_job_after_success()
    {
        await using var host = new QueueTestHost();
        var id = NewId();
        await host.Dispatch(new ChainingJob { RunId = id });

        (await Wait.Until(() => Recorder.Count(id + "-next", "run") == 1)).Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_assembly_qualified_job_is_rejected_without_running_its_constructor_and_worker_continues()
    {
        ConstructorSideEffectJob.ConstructorCalls = 0;
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = new QueueTestHost(configureServices: services =>
            services.AddSingleton<IQueueEventListener>(new FailureCaptureListener(failed)));
        await host.Services.GetRequiredService<QueueManager>().Connection(host.Connection).PushAsync(new QueuedMessage
        {
            Queue = "q",
            JobType = typeof(ConstructorSideEffectJob).AssemblyQualifiedName!,
            Payload = "{}",
            MaxAttempts = 1
        });

        var id = NewId();
        await host.Dispatch(new RecordingJob { RunId = id });

        (await failed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeOfType<UnknownJobTypeException>();
        (await Wait.Until(() => Recorder.Count(id, "run") == 1)).Should().BeTrue();
        ConstructorSideEffectJob.ConstructorCalls.Should().Be(0);
    }

    private sealed class FailureCaptureListener(TaskCompletionSource<Exception> failure) : IQueueEventListener
    {
        public Task OnFailedAsync(QueuedMessage message, Exception exception, CancellationToken cancellationToken)
        {
            failure.TrySetResult(exception);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_failing_job_is_deserialized_only_once_per_attempt()
    {
        var counter = new DeserializationCounter();
        await using var host = new QueueTestHost(configureServices: services => services.AddSingleton<IJobSerializer>(sp =>
            new CountingJobSerializer(sp.GetRequiredService<IJobTypeRegistry>(), counter)));
        var id = NewId();
        await host.Dispatch(new AlwaysFailingJob { RunId = id });

        (await Wait.Until(() => Recorder.Count(id, "failed") == 1)).Should().BeTrue();
        counter.Count.Should().Be(2);
    }

    [Fact]
    public async Task A_static_chain_runs_in_order_and_each_job_once()
    {
        await using var host = new QueueTestHost();
        var a = NewId();
        var b = NewId();
        await host.Dispatcher.Chain(new RecordingJob { RunId = a }, new RecordingJob { RunId = b })
            .DispatchAsync(o => o.OnQueue("q"));

        (await Wait.Until(() => Recorder.Count(b, "run") == 1)).Should().BeTrue();
        Recorder.Count(a, "run").Should().Be(1);
    }

    [Fact]
    public async Task Static_chain_uses_the_first_dispatch_connection_queue_and_attempt_limit()
    {
        var firstConnection = "first-" + Guid.NewGuid().ToString("N");
        var secondConnection = "second-" + Guid.NewGuid().ToString("N");
        await using var host = new QueueTestHost(
            additionalConnections: new[] { firstConnection, secondConnection },
            workerConnection: firstConnection);
        var id = NewId();

        await host.Dispatcher.Chain(
                new RecordingJob { RunId = NewId() },
                new QueueSettingsJob { RunId = id, DefaultConnection = secondConnection, DefaultQueue = "other" })
            .DispatchAsync(options => options
                .OnConnection(firstConnection)
                .OnQueue("q")
                .WithPriority(8)
                .WithMaxAttempts(6));

        (await Wait.Until(() => Recorder.Count(id, "6") == 1)).Should().BeTrue();
    }

    [Fact]
    public async Task Runtime_chain_uses_the_first_dispatch_connection_queue_and_attempt_limit()
    {
        var firstConnection = "first-" + Guid.NewGuid().ToString("N");
        var secondConnection = "second-" + Guid.NewGuid().ToString("N");
        await using var host = new QueueTestHost(
            additionalConnections: new[] { firstConnection, secondConnection },
            workerConnection: firstConnection);
        var id = NewId();

        await host.Dispatch(new RuntimeOptionsChainingJob
        {
            FollowupRunId = id,
            FollowupConnection = secondConnection,
            FollowupQueue = "other"
        }, options => options
            .OnConnection(firstConnection)
            .OnQueue("q")
            .WithPriority(8)
            .WithMaxAttempts(6));

        (await Wait.Until(() => Recorder.Count(id, "6") == 1)).Should().BeTrue();
    }

    [Fact]
    public async Task Old_chain_envelope_without_connection_continues_on_the_worker_connection()
    {
        await using var host = new QueueTestHost(
            configureServices: services => services.AddJob<RecordingJob>());
        var id = NewId();
        var serializerOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var oldEnvelope = new QueuedMessage
        {
            Queue = "q",
            JobType = typeof(RecordingJob).FullName!,
            Payload = JsonSerializer.Serialize(new RecordingJob { RunId = NewId() }),
            ChainedJobPayload = JsonSerializer.Serialize(new[]
            {
                new Naravel.Queue.Dispatch.ChainLink(
                    typeof(RecordingJob).FullName!,
                    JsonSerializer.Serialize(new RecordingJob { RunId = id }))
            }),
            MaxAttempts = 3
        };
        var oldPayload = JsonSerializer.Serialize(oldEnvelope, serializerOptions);
        using (var document = JsonDocument.Parse(oldPayload))
            document.RootElement.TryGetProperty(nameof(QueuedMessage.Connection), out _).Should().BeFalse();
        await host.Services.GetRequiredService<QueueManager>().Connection(host.Connection).PushAsync(
            JsonSerializer.Deserialize<QueuedMessage>(oldPayload)!);

        (await Wait.Until(() => Recorder.Count(id, "run") == 1)).Should().BeTrue();
    }

    [Fact]
    public async Task A_chain_stops_when_a_job_fails_permanently()
    {
        await using var host = new QueueTestHost();
        var failing = NewId();
        var after = NewId();
        await host.Dispatcher.Chain(new AlwaysFailingJob { RunId = failing }, new RecordingJob { RunId = after })
            .DispatchAsync(o => o.OnQueue("q"));

        (await Wait.Until(() => Recorder.Count(failing, "failed") == 1)).Should().BeTrue();
        await Task.Delay(200);
        Recorder.Count(after, "run").Should().Be(0);
    }

    [Fact]
    public async Task A_throwing_batch_callback_neither_reruns_the_job_nor_kills_the_worker()
    {
        await using var host = new QueueTestHost();
        var id = NewId();
        await host.Dispatcher.BatchAsync(
            new[] { new RecordingJob { RunId = id } },
            o => o.OnQueue("q"),
            b => b.OnCompleted = (_, _) => throw new InvalidOperationException("callback bug"));

        (await Wait.Until(() => Recorder.Count(id, "run") >= 1)).Should().BeTrue();
        await Task.Delay(300);
        Recorder.Count(id, "run").Should().Be(1, "the job was already acknowledged when the callback failed");

        var next = NewId();
        await host.Dispatch(new RecordingJob { RunId = next });
        (await Wait.Until(() => Recorder.Count(next, "run") == 1)).Should().BeTrue("the worker must survive");
    }

    [Fact]
    public async Task A_batch_reports_completion_when_every_job_finished()
    {
        await using var host = new QueueTestHost();
        var tcs = new TaskCompletionSource<QueueBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        await host.Dispatcher.BatchAsync(
            Enumerable.Range(0, 3).Select(_ => (IJob)new RecordingJob { RunId = NewId() }),
            o => o.OnQueue("q"),
            b => b.OnCompleted = (batch, _) => { tcs.TrySetResult(batch); return Task.CompletedTask; });

        var batch = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        batch.CompletedJobs.Should().Be(3);
        batch.FailedJobs.Should().Be(0);
    }
}
