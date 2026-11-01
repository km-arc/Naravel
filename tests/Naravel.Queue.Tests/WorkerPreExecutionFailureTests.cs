using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Naravel.Queue.Batching;
using Naravel.Queue.Drivers;
using Naravel.Queue.Events;
using Naravel.Queue.Extensions;
using Naravel.Queue.Failed;
using Naravel.Queue.Jobs;
using Naravel.Queue.Middleware;
using Naravel.Queue.Worker;

namespace Naravel.Queue.Tests;

/// <summary>
/// A reserved message must never be left reserved. Every step the worker runs between "the driver handed me the message"
/// and "the job handler is called" (batch cancellation check, scope and listener resolution, listener notification,
/// deserialization), and every step that settles the message afterwards (acknowledge, release, fail), can throw. A message
/// whose processing threw anywhere must end up acknowledged, released or failed - exactly one of them - on drivers that
/// have no expiry-based reclaim (Memory, RabbitMQ, Kafka) as well as on those that do.
/// </summary>
public class WorkerPreExecutionFailureTests
{
    private static string NewId() => Guid.NewGuid().ToString("N");

    private static readonly TimeSpan Rest = TimeSpan.FromMilliseconds(5);

    /// <summary>
    /// An in-process driver that, unlike the Memory driver, remembers what happened to every reservation, honours the
    /// cancellation token the way a database or network driver does, and can be told to fail a settle call.
    /// </summary>
    private sealed class LedgerDriver(bool supportsDelayedDelivery) : IQueueDriver
    {
        private enum State { Pending, Reserved, Acked, Failed }

        private readonly object _gate = new();
        private readonly List<(QueuedMessage Message, State State)> _entries = new();

        public bool SupportsDelayedDelivery => supportsDelayedDelivery;

        public int Pops;
        public int Acks;
        public int Releases;
        public int Fails;
        public int SettleCallsAfterSettlement;
        public int ReleaseFailuresToInject;
        public Func<Task>? OnPop;
        public List<TimeSpan> ReleaseDelays { get; } = new();

        public void Enqueue(QueuedMessage message)
        {
            lock (_gate) _entries.Add((message, State.Pending));
        }

        public int Stuck { get { lock (_gate) return _entries.Count(e => e.State == State.Reserved); } }

        public Task PushAsync(QueuedMessage message, CancellationToken ct = default)
        {
            Enqueue(message);
            return Task.CompletedTask;
        }

        public async Task<QueuedMessage?> PopAsync(string queue, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            QueuedMessage? popped = null;
            lock (_gate)
            {
                for (var i = 0; i < _entries.Count; i++)
                {
                    var (message, state) = _entries[i];
                    if (state != State.Pending || message.Queue != queue) continue;
                    _entries[i] = (message, State.Reserved);
                    Pops++;
                    popped = message;
                    break;
                }
            }

            if (popped is not null && OnPop is { } hook) await hook();
            return popped;
        }

        public Task AckAsync(QueuedMessage message, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Settle(message, State.Acked, () => Acks++);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Interlocked.Decrement(ref ReleaseFailuresToInject) >= 0)
                throw new IOException("injected: the release call failed");
            lock (_gate)
            {
                var index = _entries.FindIndex(e => ReferenceEquals(e.Message, message));
                if (index < 0 || _entries[index].State != State.Reserved)
                {
                    SettleCallsAfterSettlement++;
                    return Task.CompletedTask;
                }
                _entries[index] = (message, State.Pending);
                Releases++;
                ReleaseDelays.Add(delay);
            }
            return Task.CompletedTask;
        }

        public Task FailAsync(QueuedMessage message, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Settle(message, State.Failed, () => Fails++);
            return Task.CompletedTask;
        }

        public Task<long> SizeAsync(string queue, CancellationToken ct = default) => Task.FromResult(0L);

        private void Settle(QueuedMessage message, State to, Action count)
        {
            lock (_gate)
            {
                var index = _entries.FindIndex(e => ReferenceEquals(e.Message, message));
                if (index < 0 || _entries[index].State != State.Reserved)
                {
                    SettleCallsAfterSettlement++;
                    return;
                }
                _entries[index] = (message, to);
                count();
            }
        }
    }

    /// <summary>A batch store whose individual calls can be made to throw or to block until the worker stops.</summary>
    private sealed class FlakyBatchRepository : IBatchRepository
    {
        public int IsCancelledFailures;
        public bool Cancelled;
        public int MarkJobCancelledFailures;
        public Func<CancellationToken, Task<bool>>? IsCancelledOverride;
        public int MarkJobFailedCalls;
        public int MarkJobCancelledCalls;

        public Task RegisterAsync(QueueBatch batch, BatchOptions options, CancellationToken ct) => Task.CompletedTask;
        public Task<QueueBatch?> GetAsync(string batchId, CancellationToken ct) => Task.FromResult<QueueBatch?>(null);
        public Task CancelAsync(string batchId, CancellationToken ct) => Task.CompletedTask;
        public Task MarkJobCompletedAsync(string batchId, CancellationToken ct) => Task.CompletedTask;
        public Task ResumePendingCallbacksAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<bool> IsCancelledAsync(string batchId, CancellationToken ct)
        {
            if (IsCancelledOverride is { } custom) return custom(ct);
            if (Interlocked.Decrement(ref IsCancelledFailures) >= 0)
                throw new IOException("injected: the batch store is unavailable");
            return Task.FromResult(Cancelled);
        }

        public Task MarkJobFailedAsync(string batchId, Exception exception, CancellationToken ct)
        {
            Interlocked.Increment(ref MarkJobFailedCalls);
            return Task.CompletedTask;
        }

        public Task MarkJobCancelledAsync(string batchId, CancellationToken ct)
        {
            Interlocked.Increment(ref MarkJobCancelledCalls);
            if (Interlocked.Decrement(ref MarkJobCancelledFailures) >= 0)
                throw new IOException("injected: the batch store is unavailable");
            return Task.CompletedTask;
        }
    }

    private sealed class FlakyFailedJobStore : IFailedJobStore
    {
        private readonly InMemoryFailedJobStore _inner = new();
        public int RecordFailures;

        public Task RecordAsync(QueuedMessage message, Exception exception, CancellationToken ct)
        {
            if (Interlocked.Decrement(ref RecordFailures) >= 0)
                throw new IOException("injected: the failed-job store is unavailable");
            return _inner.RecordAsync(message, exception, ct);
        }

        public Task<IReadOnlyList<QueuedMessage>> ListAsync(CancellationToken ct) => _inner.ListAsync(ct);
        public Task<bool> ForgetAsync(string id, CancellationToken ct) => _inner.ForgetAsync(id, ct);
        public Task<int> FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
    }

    private sealed class SkipEverythingMiddleware : IJobMiddleware
    {
        public Task InvokeAsync(JobContext context, Func<Task> next)
        {
            context.Skip();
            return Task.CompletedTask;
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        public LedgerDriver Driver { get; }
        public ServiceProvider Services { get; }
        public QueueWorkerService Worker { get; }

        public Rig(bool supportsDelayedDelivery = false, Action<IServiceCollection>? configure = null, Action<QueueWorkerOptions>? configureWorker = null)
        {
            Driver = new LedgerDriver(supportsDelayedDelivery);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NaravelQueue:Default"] = "ledger",
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddQueue(config);
            services.AddJob<RecordingJob>();
            services.AddJob<FlakyJob>();
            configure?.Invoke(services);
            services.AddQueueWorker(o =>
            {
                o.Connection = "ledger";
                o.Queues = new[] { "q" };
                o.Rest = Rest;
                configureWorker?.Invoke(o);
            });
            Services = services.BuildServiceProvider();
            Services.GetRequiredService<QueueManager>().Extend("ledger", _ => Driver);
            Worker = (QueueWorkerService)Services.GetServices<IHostedService>().Single();
        }

        public Task StartAsync() => Worker.StartAsync(CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await Worker.StopAsync(CancellationToken.None);
            await Services.DisposeAsync();
        }
    }

    private static QueuedMessage MessageFor(Job job, string? batchId = null, int? maxAttempts = null, int attempts = 0) => new()
    {
        Queue = "q",
        Connection = "ledger",
        JobType = job.GetType().FullName!,
        Payload = JsonSerializer.Serialize(job, job.GetType()),
        MaxAttempts = maxAttempts ?? job.MaxAttempts,
        Attempts = attempts,
        BatchId = batchId,
    };

    [Fact]
    public async Task A_batch_cancellation_check_that_throws_is_retried_instead_of_leaving_the_message_reserved()
    {
        var id = NewId();
        var batch = new FlakyBatchRepository { IsCancelledFailures = 1 };
        await using var rig = new Rig(configure: s => s.AddSingleton<IBatchRepository>(batch));
        rig.Driver.Enqueue(MessageFor(new RecordingJob { RunId = id }, batchId: "b1"));
        await rig.StartAsync();

        (await Wait.Until(() => Recorder.Count(id, "run") == 1)).Should().BeTrue("the second delivery runs the job once the batch store answers");
        (await Wait.Until(() => rig.Driver.Acks == 1)).Should().BeTrue();
        rig.Driver.Releases.Should().Be(1, "the first, failed delivery is handed back once");
        rig.Driver.Stuck.Should().Be(0);
        rig.Driver.SettleCallsAfterSettlement.Should().Be(0);
    }

    [Fact]
    public async Task A_listener_that_cannot_be_resolved_is_retried_instead_of_leaving_the_message_reserved()
    {
        var id = NewId();
        var failuresLeft = 1;
        await using var rig = new Rig(configure: s => s.AddScoped<IQueueEventListener>(_ =>
            Interlocked.Decrement(ref failuresLeft) >= 0
                ? throw new InvalidOperationException("injected: the listener constructor failed")
                : new NoopListener()));
        rig.Driver.Enqueue(MessageFor(new RecordingJob { RunId = id }));
        await rig.StartAsync();

        (await Wait.Until(() => Recorder.Count(id, "run") == 1)).Should().BeTrue();
        (await Wait.Until(() => rig.Driver.Acks == 1)).Should().BeTrue();
        rig.Driver.Releases.Should().Be(1);
        rig.Driver.Stuck.Should().Be(0);
    }

    private sealed class NoopListener : IQueueEventListener { }

    [Fact]
    public async Task A_pre_execution_failure_that_never_goes_away_ends_in_the_failed_store_and_not_in_a_reserved_message()
    {
        var id = NewId();
        var batch = new FlakyBatchRepository { IsCancelledFailures = int.MaxValue };
        var failedStore = new InMemoryFailedJobStore();
        await using var rig = new Rig(configure: s =>
        {
            s.AddSingleton<IBatchRepository>(batch);
            s.AddSingleton<IFailedJobStore>(failedStore);
        });
        rig.Driver.Enqueue(MessageFor(new RecordingJob { RunId = id }, batchId: "b1", maxAttempts: 2));
        await rig.StartAsync();

        (await Wait.Until(() => rig.Driver.Fails == 1)).Should().BeTrue("the second failed attempt exhausts MaxAttempts = 2");
        rig.Driver.Releases.Should().Be(1, "the first failed attempt is retried once");
        rig.Driver.Stuck.Should().Be(0);
        Recorder.Count(id, "run").Should().Be(0, "the job never started");
        (await failedStore.ListAsync(CancellationToken.None)).Should().HaveCount(1);
        batch.MarkJobFailedCalls.Should().Be(1);
    }

    [Fact]
    public async Task A_stop_during_the_batch_cancellation_check_hands_the_message_back_without_counting_an_attempt()
    {
        var id = NewId();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batch = new FlakyBatchRepository
        {
            IsCancelledOverride = async ct =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return false;
            },
        };
        await using var rig = new Rig(configure: s => s.AddSingleton<IBatchRepository>(batch));
        var message = MessageFor(new RecordingJob { RunId = id }, batchId: "b1");
        rig.Driver.Enqueue(message);
        await rig.StartAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await rig.Worker.StopAsync(CancellationToken.None);

        rig.Driver.Releases.Should().Be(1, "a shutdown must not strand a reserved message");
        rig.Driver.Stuck.Should().Be(0);
        message.Attempts.Should().Be(0, "a shutdown is not a failed attempt");
        rig.Driver.ReleaseDelays.Single().Should().Be(TimeSpan.Zero, "another worker may take it immediately");
        Recorder.Count(id, "run").Should().Be(0);
    }

    [Fact]
    public async Task A_failed_job_store_outage_at_the_last_attempt_does_not_strand_the_message()
    {
        var id = NewId();
        var failedStore = new FlakyFailedJobStore { RecordFailures = 1 };
        await using var rig = new Rig(configure: s => s.AddSingleton<IFailedJobStore>(failedStore));
        rig.Driver.Enqueue(MessageFor(new RecordingJob { RunId = id }, maxAttempts: 3, attempts: 3));
        await rig.StartAsync();

        (await Wait.Until(() => rig.Driver.Fails == 1)).Should().BeTrue("the second delivery records the failure once the store is back");
        rig.Driver.Releases.Should().Be(1);
        rig.Driver.Stuck.Should().Be(0);
        Recorder.Count(id, "run").Should().Be(0, "an exhausted job is never executed again");
        (await failedStore.ListAsync(CancellationToken.None)).Should().HaveCount(1);
    }

    [Fact]
    public async Task A_release_that_fails_once_is_repeated_so_the_message_is_not_stranded_and_the_attempt_is_counted_once()
    {
        var id = NewId();
        await using var rig = new Rig(supportsDelayedDelivery: true);
        rig.Driver.ReleaseFailuresToInject = 1;
        rig.Driver.Enqueue(MessageFor(new FlakyJob { RunId = id, FailTimes = 1 }));
        await rig.StartAsync();

        (await Wait.Until(() => Recorder.Count(id, "ok") == 1)).Should().BeTrue();
        Recorder.Count(id, "attempt-1").Should().Be(1);
        Recorder.Count(id, "attempt-2").Should().Be(1, "the failed attempt was counted exactly once");
        Recorder.Count(id, "attempt-3").Should().Be(0);
        rig.Driver.Stuck.Should().Be(0);
        rig.Driver.ReleaseDelays.Single().Should().Be(Rest, "the repeated release waits one Rest so a broken store is not hit in a hot loop");
    }

    [Fact]
    public async Task A_skipped_job_whose_batch_bookkeeping_throws_stays_acknowledged_and_is_never_retried()
    {
        var id = NewId();
        var batch = new FlakyBatchRepository { MarkJobCancelledFailures = 1 };
        await using var rig = new Rig(configure: s =>
        {
            s.AddSingleton<IBatchRepository>(batch);
            s.AddScoped<IJobMiddleware, SkipEverythingMiddleware>();
        });
        rig.Driver.Enqueue(MessageFor(new RecordingJob { RunId = id }, batchId: "b1"));
        await rig.StartAsync();

        (await Wait.Until(() => batch.MarkJobCancelledCalls >= 1)).Should().BeTrue();
        await Task.Delay(150);
        rig.Driver.Acks.Should().Be(1);
        rig.Driver.Releases.Should().Be(0, "an acknowledged message must not be put back by a later bookkeeping error");
        rig.Driver.SettleCallsAfterSettlement.Should().Be(0);
        rig.Driver.Pops.Should().Be(1, "the skipped job is not delivered again");
        Recorder.Count(id, "run").Should().Be(0);
    }

    [Fact]
    public async Task A_cancelled_batch_job_whose_bookkeeping_throws_stays_acknowledged_and_is_never_retried()
    {
        var id = NewId();
        var batch = new FlakyBatchRepository { Cancelled = true, MarkJobCancelledFailures = 1 };
        await using var rig = new Rig(configure: s => s.AddSingleton<IBatchRepository>(batch));
        rig.Driver.Enqueue(MessageFor(new RecordingJob { RunId = id }, batchId: "b1"));
        await rig.StartAsync();

        (await Wait.Until(() => batch.MarkJobCancelledCalls >= 1)).Should().BeTrue();
        await Task.Delay(150);
        rig.Driver.Acks.Should().Be(1);
        rig.Driver.Releases.Should().Be(0);
        rig.Driver.SettleCallsAfterSettlement.Should().Be(0);
        rig.Driver.Pops.Should().Be(1);
        Recorder.Count(id, "run").Should().Be(0);
    }

    [Fact]
    public async Task The_release_after_the_worker_stopped_polling_does_not_use_the_cancelled_stopping_token()
    {
        // The worker reserved a message just as MaxRuntime ran out and the host began to stop, so the message is
        // handed back through the "stop polling" path with a stopping token that is already cancelled. A driver
        // that honours its token (EF Core, a network client) would then throw and the message would stay reserved.
        var id = NewId();
        await using var rig = new Rig(configureWorker: o => o.MaxRuntime = TimeSpan.FromSeconds(1));
        rig.Driver.Enqueue(MessageFor(new RecordingJob { RunId = id }));
        rig.Driver.OnPop = async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1300)); // past MaxRuntime
            _ = rig.Worker.StopAsync(CancellationToken.None);  // cancels the stopping token synchronously
        };
        await rig.StartAsync();

        if (rig.Worker is BackgroundService { ExecuteTask: { } task }) await task.WaitAsync(TimeSpan.FromSeconds(10));

        rig.Driver.Releases.Should().Be(1);
        rig.Driver.Stuck.Should().Be(0);
        Recorder.Count(id, "run").Should().Be(0);
    }
}
