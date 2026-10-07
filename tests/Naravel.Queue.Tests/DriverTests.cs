using Naravel.Queue.Drivers;
using Naravel.Queue.File;
using Naravel.Queue.Memory;

namespace Naravel.Queue.Tests;

/// <summary>Behaviour every driver must share. Run against each driver that needs no external service.</summary>
public abstract class QueueDriverContractTests : IDisposable
{
    private readonly string _queuePrefix = "q-" + Guid.NewGuid().ToString("N");
    private readonly HashSet<string> _usedQueues = new(StringComparer.Ordinal);

    protected abstract IQueueDriver CreateDriver();

    protected virtual bool SupportsPriority => true;

    protected virtual bool SupportsSize => true;

    protected string QueuePrefix => _queuePrefix;

    protected IReadOnlyCollection<string> UsedQueueNames => _usedQueues;

    protected string QueueName(string suffix)
    {
        var queue = $"{_queuePrefix}-{suffix}";
        _usedQueues.Add(queue);
        return queue;
    }

    protected static QueuedMessage Msg(string queue, int priority = 0, TimeSpan? delay = null) => new()
    {
        Queue = queue,
        JobType = "x",
        Payload = "{}",
        Priority = priority,
        AvailableAt = DateTimeOffset.UtcNow + (delay ?? TimeSpan.Zero),
    };

    protected static async Task<QueuedMessage?> PopEventuallyAsync(IQueueDriver driver, string queue, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (true)
        {
            var message = await driver.PopAsync(queue);
            if (message is not null || DateTimeOffset.UtcNow >= deadline) return message;
            await Task.Delay(50);
        }
    }

    // Brokers with asynchronous consumer start-up (Kafka) override this so the first real assertion on a queue does not
    // race the consumer joining its group. Other drivers keep the default no-op and are tested exactly as before.
    protected virtual Task WarmUpQueueAsync(IQueueDriver driver, string queue) => Task.CompletedTask;

    protected static async Task WarmUpWithProbeAsync(IQueueDriver driver, string queue)
    {
        var probe = Msg(queue);
        await driver.PushAsync(probe);
        var popped = await PopEventuallyAsync(driver, queue);
        popped.Should().NotBeNull("the driver must deliver a probe message to a freshly created queue");
        await driver.AckAsync(popped!);
    }

    protected async Task RunContractAsync()
    {
        var driver = CreateDriver();
        try
        {
            var emptyQueue = QueueName("empty");
            (await driver.PopAsync(emptyQueue)).Should().BeNull();

            var ackQueue = QueueName("ack");
            await WarmUpQueueAsync(driver, ackQueue);
            var message = Msg(ackQueue);
            await driver.PushAsync(message);
            if (SupportsSize)
                (await driver.SizeAsync(ackQueue)).Should().Be(1);
            var popped = await PopEventuallyAsync(driver, ackQueue);
            popped!.Id.Should().Be(message.Id);
            (await driver.PopAsync(ackQueue)).Should().BeNull("a reserved message must not be handed out again");
            await driver.AckAsync(popped);
            (await driver.PopAsync(ackQueue)).Should().BeNull();
            if (SupportsSize)
                (await driver.SizeAsync(ackQueue)).Should().Be(0);

            var delayedQueue = QueueName("delayed");
            await WarmUpQueueAsync(driver, delayedQueue);
            await driver.PushAsync(Msg(delayedQueue, delay: TimeSpan.FromMilliseconds(300)));
            (await driver.PopAsync(delayedQueue)).Should().BeNull();
            await Task.Delay(400);
            (await PopEventuallyAsync(driver, delayedQueue)).Should().NotBeNull();

            if (SupportsPriority)
            {
                var priorityQueue = QueueName("priority");
                await WarmUpQueueAsync(driver, priorityQueue);
                var low = Msg(priorityQueue, priority: 0);
                var high = Msg(priorityQueue, priority: 9);
                await driver.PushAsync(low);
                await driver.PushAsync(high);
                (await PopEventuallyAsync(driver, priorityQueue))!.Id.Should().Be(high.Id);
                (await PopEventuallyAsync(driver, priorityQueue))!.Id.Should().Be(low.Id);
            }

            var releaseQueue = QueueName("release");
            await WarmUpQueueAsync(driver, releaseQueue);
            await driver.PushAsync(Msg(releaseQueue));
            var released = (await PopEventuallyAsync(driver, releaseQueue))!;
            released.Attempts = 1;
            await driver.ReleaseAsync(released, TimeSpan.FromMilliseconds(200));
            (await driver.PopAsync(releaseQueue)).Should().BeNull();
            await Task.Delay(300);
            var retried = await PopEventuallyAsync(driver, releaseQueue);
            retried.Should().NotBeNull();
            retried!.Attempts.Should().Be(1);

            var failedQueue = QueueName("failed");
            await WarmUpQueueAsync(driver, failedQueue);
            await driver.PushAsync(Msg(failedQueue));
            await driver.FailAsync((await PopEventuallyAsync(driver, failedQueue))!);
            (await driver.PopAsync(failedQueue)).Should().BeNull();
            if (SupportsSize)
                (await driver.SizeAsync(failedQueue)).Should().Be(0);

            var isolatedQueue = QueueName("isolated");
            await WarmUpQueueAsync(driver, isolatedQueue);
            var otherQueue = QueueName("other");
            await driver.PushAsync(Msg(isolatedQueue));
            (await driver.PopAsync(otherQueue)).Should().BeNull();
            (await PopEventuallyAsync(driver, isolatedQueue)).Should().NotBeNull();

            var concurrentQueue = QueueName("concurrent");
            await WarmUpQueueAsync(driver, concurrentQueue);
            const int total = 60;
            for (var i = 0; i < total; i++) await driver.PushAsync(Msg(concurrentQueue));

            var received = new System.Collections.Concurrent.ConcurrentBag<string>();
            var consumers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            {
                while (await driver.PopAsync(concurrentQueue) is { } item)
                {
                    received.Add(item.Id);
                    await driver.AckAsync(item);
                }
            }));
            await Task.WhenAll(consumers);
            received.Should().HaveCount(total);
            received.Distinct().Should().HaveCount(total);
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

    protected virtual Task CleanupAsync() => Task.CompletedTask;

    public void Dispose() { }
}

public sealed class MemoryQueueDriverContractTests : QueueDriverContractTests
{
    [Fact]
    public Task Shared_contract() => RunContractAsync();

    protected override IQueueDriver CreateDriver()
        => new MemoryQueueDriver("contract-" + Guid.NewGuid().ToString("N"));
}

public sealed class FileQueueDriverContractTests : QueueDriverContractTests
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "naravel-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public Task Shared_contract() => RunContractAsync();

    protected override IQueueDriver CreateDriver() => new FileQueueDriver(_dir);

    protected override Task CleanupAsync()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        return Task.CompletedTask;
    }
}

public class FileDriverRecoveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "naravel-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task A_message_reserved_by_a_crashed_worker_is_reclaimed_after_the_visibility_timeout()
    {
        var d = new FileQueueDriver(_dir, TimeSpan.FromMilliseconds(150));
        var m = new QueuedMessage { Queue = "q", JobType = "x", Payload = "{}" };
        await d.PushAsync(m);

        (await d.PopAsync("q"))!.Id.Should().Be(m.Id);   // worker takes it and "crashes" (never acks)
        (await d.PopAsync("q")).Should().BeNull();        // still within the timeout

        await Task.Delay(300);
        var reclaimed = await d.PopAsync("q");
        reclaimed!.Id.Should().Be(m.Id);
        reclaimed.Attempts.Should().Be(1);
    }
}
