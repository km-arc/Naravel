using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Batching;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Jobs;
using Naravel.Queue.Testing;

namespace Naravel.Queue.Tests;

/// <summary>
/// Owner decision AD-011 (audit finding QUE-004): a batch needs at least one job. A batch is finished by the jobs that finish, so an
/// empty one would never finish and its callbacks would never run; it is refused before any record is made.
/// </summary>
public class EmptyBatchTests
{
    [Fact]
    public async Task An_empty_batch_is_rejected_before_anything_is_registered()
    {
        var repository = new CountingBatchRepository();
        await using var host = new QueueTestHost(configureServices: services => services.AddSingleton<IBatchRepository>(repository));
        var completed = false;

        foreach (var empty in new IEnumerable<IJob>[] { Array.Empty<IJob>(), Enumerable.Range(0, 0).Select(_ => (IJob)new RecordingJob()) })
        {
            var dispatch = () => host.Dispatcher.BatchAsync(
                empty,
                options => options.OnQueue("q"),
                batch => batch.OnCompleted = (_, _) => { completed = true; return Task.CompletedTask; });

            (await dispatch.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("jobs");
        }

        repository.Registered.Should().Be(0, "no batch record may be created for an empty batch");
        completed.Should().BeFalse();
    }

    [Fact]
    public async Task A_batch_with_one_job_is_still_registered_and_completes()
    {
        var repository = new CountingBatchRepository();
        await using var host = new QueueTestHost(configureServices: services => services.AddSingleton<IBatchRepository>(repository));

        var batch = await host.Dispatcher.BatchAsync(
            new IJob[] { new RecordingJob { RunId = Guid.NewGuid().ToString("N") } },
            options => options.OnQueue("q"));

        repository.Registered.Should().Be(1);
        (await Wait.Until(() => batch.IsFinished)).Should().BeTrue();
        batch.CompletedJobs.Should().Be(1);
    }

    [Fact]
    public async Task The_queue_fake_rejects_an_empty_batch_like_the_real_dispatcher()
    {
        var fake = new QueueFake();

        var dispatch = () => fake.BatchAsync(Array.Empty<IJob>());

        (await dispatch.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("jobs");
        fake.AssertNotDispatched<RecordingJob>();
    }

    /// <summary>An in-memory repository that counts how many batches were registered.</summary>
    private sealed class CountingBatchRepository : IBatchRepository
    {
        private readonly InMemoryBatchRepository _inner = new();
        private int _registered;

        public int Registered => Volatile.Read(ref _registered);

        public Task RegisterAsync(QueueBatch batch, BatchOptions options, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _registered);
            return _inner.RegisterAsync(batch, options, cancellationToken);
        }

        public Task<QueueBatch?> GetAsync(string batchId, CancellationToken cancellationToken) => _inner.GetAsync(batchId, cancellationToken);
        public Task<bool> IsCancelledAsync(string batchId, CancellationToken cancellationToken) => _inner.IsCancelledAsync(batchId, cancellationToken);
        public Task CancelAsync(string batchId, CancellationToken cancellationToken) => _inner.CancelAsync(batchId, cancellationToken);
        public Task MarkJobCompletedAsync(string batchId, CancellationToken cancellationToken) => _inner.MarkJobCompletedAsync(batchId, cancellationToken);
        public Task MarkJobFailedAsync(string batchId, Exception exception, CancellationToken cancellationToken) => _inner.MarkJobFailedAsync(batchId, exception, cancellationToken);
        public Task MarkJobCancelledAsync(string batchId, CancellationToken cancellationToken) => _inner.MarkJobCancelledAsync(batchId, cancellationToken);
        public Task ResumePendingCallbacksAsync(CancellationToken cancellationToken) => _inner.ResumePendingCallbacksAsync(cancellationToken);
    }
}
