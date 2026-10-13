using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using Naravel.Queue.Failed;

namespace Naravel.Queue.Tests;

/// <summary>
/// R07.T10 / D-28: a worker that outlived its visibility timeout must not leave a failed-job record, run the
/// <c>failed</c> handler or count a failure for a job another worker now owns.
/// </summary>
public class FencedFailureTests
{
    private sealed class FencedOutDriver(QueuedMessage message) : IQueueDriver
    {
        private int _popped;
        public int TryFailCalls;
        public Task PushAsync(QueuedMessage m, CancellationToken ct = default) => Task.CompletedTask;
        public Task<QueuedMessage?> PopAsync(string queue, CancellationToken ct = default)
            => Task.FromResult<QueuedMessage?>(Interlocked.Exchange(ref _popped, 1) == 0 ? message : null);
        public Task AckAsync(QueuedMessage m, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReleaseAsync(QueuedMessage m, TimeSpan delay, CancellationToken ct = default) => Task.CompletedTask;
        public Task FailAsync(QueuedMessage m, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> TryFailAsync(QueuedMessage m, CancellationToken ct = default)
        {
            Interlocked.Increment(ref TryFailCalls);
            return Task.FromResult(false); // the reservation now belongs to another worker
        }
        public Task<long> SizeAsync(string queue, CancellationToken ct = default) => Task.FromResult(0L);
    }

    [Fact]
    public async Task A_fenced_out_worker_leaves_no_failed_job_record_and_does_not_run_the_failed_handler()
    {
        var id = "fenced-" + Guid.NewGuid().ToString("N");
        var job = new AlwaysFailingJob { RunId = id };
        var message = new QueuedMessage
        {
            Queue = "q",
            Connection = "fenced",
            JobType = typeof(AlwaysFailingJob).FullName!,
            Payload = JsonSerializer.Serialize(job),
            Attempts = job.MaxAttempts,
            MaxAttempts = job.MaxAttempts,
        };
        var driver = new FencedOutDriver(message);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = "fenced",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueue(config);
        services.AddJob<AlwaysFailingJob>();
        services.AddQueueWorker(o =>
        {
            o.Connection = "fenced";
            o.Queues = new[] { "q" };
            o.StopWhenEmpty = true;
            o.Rest = TimeSpan.FromMilliseconds(5);
        });
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<QueueManager>().Extend("fenced", _ => driver);
        var worker = provider.GetServices<IHostedService>().Single();

        await worker.StartAsync(CancellationToken.None);
        if (worker is BackgroundService { ExecuteTask: { } task }) await task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        driver.TryFailCalls.Should().Be(1);
        (await provider.GetRequiredService<IFailedJobStore>().ListAsync(CancellationToken.None)).Should().BeEmpty();
        Recorder.Count(id, "failed").Should().Be(0);
        Recorder.Count(id, "attempt").Should().Be(0);
    }
}
