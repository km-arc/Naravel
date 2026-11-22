using Naravel.Queue.Drivers;
using Naravel.Queue.RabbitMQ;
using Naravel.Testing;
using RabbitMQ.Client;

namespace Naravel.Queue.Providers.Tests;

/// <summary>
/// RQM-001: a retry (<c>ReleaseAsync</c>) or a failed job (<c>FailAsync</c>) on RabbitMQ publishes its replacement message and waits
/// for the broker to confirm it before it acknowledges the original delivery. A stop between the two must leave at least one copy.
/// These tests need a broker (<c>NARAVEL_TEST_RABBITMQ</c>); a stop is simulated by the driver's two internal test seams and by
/// closing the driver, which makes the broker requeue every delivery that was not acknowledged.
/// </summary>
public sealed class RabbitMqPublishBeforeAckTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly string _queue = "rqm001-" + Guid.NewGuid().ToString("N")[..10];
    private readonly List<RabbitMqQueueDriver> _drivers = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var driver in _drivers)
        {
            try { await driver.DisposeAsync(); } catch { /* already closed by the test */ }
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NARAVEL_TEST_RABBITMQ"))) return;
        await using var connection = await CreateFactory().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeleteAsync($"netqueue.ready.{_queue}");
        await channel.QueueDeleteAsync($"netqueue.delay.{_queue}");
        await channel.QueueDeleteAsync($"netqueue.failed.{_queue}");
    }

    [ServiceFact("NARAVEL_TEST_RABBITMQ")]
    public async Task Release_has_the_retry_copy_on_the_broker_before_it_acknowledges_the_original()
    {
        var worker = NewDriver();
        var observer = NewDriver();
        var popped = await PushAndPopAsync(worker);
        long readyWhileOriginalUnacknowledged = -1;
        worker.AfterReplacementConfirmed = async cancellationToken =>
            readyWhileOriginalUnacknowledged = await observer.SizeAsync(_queue, cancellationToken);

        await worker.ReleaseAsync(popped, TimeSpan.Zero);

        readyWhileOriginalUnacknowledged.Should().Be(1, "the replacement is ready on the broker while the original is still unacknowledged");
        (await observer.SizeAsync(_queue)).Should().Be(1);
        await worker.DisposeAsync();
        (await DrainAsync(observer, expected: 1)).Should().ContainSingle().Which.Id.Should().Be(popped.Id);
    }

    [ServiceFact("NARAVEL_TEST_RABBITMQ")]
    public async Task Release_that_stops_before_the_copy_is_published_leaves_the_original_deliverable()
    {
        var worker = NewDriver();
        var observer = NewDriver();
        var popped = await PushAndPopAsync(worker);
        worker.BeforeReplacementPublished = _ => throw new InvalidOperationException("the process stops");

        var release = () => worker.ReleaseAsync(popped, TimeSpan.Zero);

        await release.Should().ThrowAsync<InvalidOperationException>();
        await worker.DisposeAsync(); // the broker requeues what was not acknowledged
        (await DrainAsync(observer, expected: 1)).Should().ContainSingle().Which.Id.Should().Be(popped.Id);
    }

    [ServiceFact("NARAVEL_TEST_RABBITMQ")]
    public async Task Release_that_stops_after_the_copy_is_confirmed_and_before_the_acknowledgement_loses_nothing()
    {
        var worker = NewDriver();
        var observer = NewDriver();
        var popped = await PushAndPopAsync(worker);
        worker.AfterReplacementConfirmed = _ => throw new InvalidOperationException("the process stops");

        var release = () => worker.ReleaseAsync(popped, TimeSpan.Zero);

        await release.Should().ThrowAsync<InvalidOperationException>();
        await worker.DisposeAsync();
        // At-least-once: the replacement and the redelivered original are both there (a duplicate, never a loss).
        var copies = await DrainAsync(observer, expected: 2);
        copies.Should().HaveCountGreaterThanOrEqualTo(1);
        copies.Should().OnlyContain(copy => copy.Id == popped.Id);
    }

    [ServiceFact("NARAVEL_TEST_RABBITMQ")]
    public async Task Release_does_not_acknowledge_the_replacement_that_a_worker_popped_in_the_meantime()
    {
        var worker = NewDriver();
        var observer = NewDriver();
        var popped = await PushAndPopAsync(worker);
        QueuedMessage? replacement = null;
        // The replacement keeps the message id, so a worker of this process can pop it before the original is acknowledged.
        worker.AfterReplacementConfirmed = async cancellationToken => replacement = await worker.PopAsync(_queue, cancellationToken);

        await worker.ReleaseAsync(popped, TimeSpan.Zero);

        replacement.Should().NotBeNull();
        replacement!.Id.Should().Be(popped.Id);
        await worker.AckAsync(replacement);
        await worker.DisposeAsync();
        // The original was acknowledged by ReleaseAsync and the replacement by the worker: nothing is redelivered.
        await Task.Delay(500);
        (await observer.SizeAsync(_queue)).Should().Be(0);
        (await observer.PopAsync(_queue)).Should().BeNull();
    }

    [ServiceFact("NARAVEL_TEST_RABBITMQ")]
    public async Task Fail_has_the_failed_copy_on_the_broker_and_acknowledges_the_original_after_it()
    {
        var worker = NewDriver();
        var observer = NewDriver();
        var popped = await PushAndPopAsync(worker);
        long failedWhileOriginalUnacknowledged = -1;
        worker.AfterReplacementConfirmed = async _ => failedWhileOriginalUnacknowledged = await FailedCountAsync();

        await worker.FailAsync(popped);

        failedWhileOriginalUnacknowledged.Should().Be(1, "the failed copy is on the broker while the original is still unacknowledged");
        await worker.DisposeAsync();
        (await FailedCountAsync()).Should().Be(1);
        (await observer.SizeAsync(_queue)).Should().Be(0, "the original was acknowledged and is not redelivered");
    }

    [ServiceFact("NARAVEL_TEST_RABBITMQ")]
    public async Task Fail_that_stops_before_the_copy_is_published_leaves_the_original_deliverable()
    {
        var worker = NewDriver();
        var observer = NewDriver();
        var popped = await PushAndPopAsync(worker);
        worker.BeforeReplacementPublished = _ => throw new InvalidOperationException("the process stops");

        var fail = () => worker.FailAsync(popped);

        await fail.Should().ThrowAsync<InvalidOperationException>();
        await worker.DisposeAsync();
        (await DrainAsync(observer, expected: 1)).Should().ContainSingle().Which.Id.Should().Be(popped.Id);
        (await FailedCountAsync()).Should().Be(0);
    }

    [ServiceFact("NARAVEL_TEST_RABBITMQ")]
    public async Task Fail_that_stops_after_the_copy_is_confirmed_and_before_the_acknowledgement_loses_nothing()
    {
        var worker = NewDriver();
        var observer = NewDriver();
        var popped = await PushAndPopAsync(worker);
        worker.AfterReplacementConfirmed = _ => throw new InvalidOperationException("the process stops");

        var fail = () => worker.FailAsync(popped);

        await fail.Should().ThrowAsync<InvalidOperationException>();
        await worker.DisposeAsync();
        // The failed copy is kept, and the original is redelivered (it will be failed again later: a duplicate, not a loss).
        (await FailedCountAsync()).Should().Be(1);
        (await DrainAsync(observer, expected: 1)).Should().ContainSingle().Which.Id.Should().Be(popped.Id);
    }

    private RabbitMqQueueDriver NewDriver()
    {
        var uri = new Uri(Environment.GetEnvironmentVariable("NARAVEL_TEST_RABBITMQ")!);
        var credentials = uri.UserInfo.Split(':', 2);
        var virtualHost = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var driver = new RabbitMqQueueDriver(
            uri.Host,
            uri.Port,
            credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0]) : null,
            credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,
            string.IsNullOrEmpty(virtualHost) ? "/" : virtualHost);
        _drivers.Add(driver);
        return driver;
    }

    private static ConnectionFactory CreateFactory()
    {
        var uri = new Uri(Environment.GetEnvironmentVariable("NARAVEL_TEST_RABBITMQ")!);
        var credentials = uri.UserInfo.Split(':', 2);
        return new ConnectionFactory
        {
            HostName = uri.Host,
            Port = uri.Port,
            UserName = credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0]) : "guest",
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "guest",
            VirtualHost = string.IsNullOrEmpty(uri.AbsolutePath.TrimStart('/')) ? "/" : Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            ClientProvidedName = "naravel-tests-rqm001"
        };
    }

    private async Task<QueuedMessage> PushAndPopAsync(RabbitMqQueueDriver worker)
    {
        await worker.PushAsync(new QueuedMessage { Queue = _queue, JobType = "job", Payload = "{}" });
        var popped = await worker.PopAsync(_queue);
        popped.Should().NotBeNull();
        return popped!;
    }

    /// <summary>The number of messages waiting in the failed queue.</summary>
    private async Task<long> FailedCountAsync()
    {
        await using var connection = await CreateFactory().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var status = await channel.QueueDeclarePassiveAsync($"netqueue.failed.{_queue}");
        return status.MessageCount;
    }

    /// <summary>
    /// Takes messages from the ready queue until <paramref name="expected"/> have been seen or the time is up. The broker requeues
    /// the unacknowledged deliveries of a closed channel asynchronously, so a message can appear a moment after the close.
    /// </summary>
    private async Task<List<QueuedMessage>> DrainAsync(RabbitMqQueueDriver observer, int expected)
    {
        var seen = new List<QueuedMessage>();
        var deadline = DateTime.UtcNow + Patience;
        while (seen.Count < expected && DateTime.UtcNow < deadline)
        {
            var message = await observer.PopAsync(_queue);
            if (message is null) await Task.Delay(100);
            else seen.Add(message);
        }

        return seen;
    }
}
