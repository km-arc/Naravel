using Naravel.Queue.Drivers;
using Naravel.Queue.Redis;
using Naravel.Testing;
using StackExchange.Redis;

namespace Naravel.Queue.Providers.Tests;

/// <summary>Owner decision AD-018 (option B, RQR-003): a message that exhausted its attempts is removed from the queue and leaves no copy in a driver-native failed list.</summary>
public sealed class RedisFailedMessageTests
{
    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task A_failed_message_leaves_no_failed_list_and_nothing_in_the_queue()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        var queue = $"fail-{Guid.NewGuid():N}";
        var driver = new RedisQueueDriver(multiplexer, TimeSpan.FromMinutes(1));
        var database = multiplexer.GetDatabase();
        await driver.PushAsync(new QueuedMessage { Queue = queue, JobType = "job", Payload = "{}" });
        var popped = await driver.PopAsync(queue);

        (await driver.TryFailAsync(popped!)).Should().BeTrue();

        (await database.KeyExistsAsync($"netqueue:{queue}:failed")).Should().BeFalse("the driver keeps no failed list; the failed-job store is the record");
        (await database.HashLengthAsync($"netqueue:{queue}:messages")).Should().Be(0);
        (await driver.SizeAsync(queue)).Should().Be(0);
        (await driver.PopAsync(queue)).Should().BeNull();
    }

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task A_stale_worker_still_cannot_fail_a_message_that_another_worker_owns()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        var queue = $"fail-{Guid.NewGuid():N}";
        var driver = new RedisQueueDriver(multiplexer, TimeSpan.FromMilliseconds(100));
        var database = multiplexer.GetDatabase();
        try
        {
            await driver.PushAsync(new QueuedMessage { Queue = queue, JobType = "job", Payload = "{}" });
            var stale = await driver.PopAsync(queue);
            await Task.Delay(250);
            var live = await driver.PopAsync(queue); // the reservation expired and another worker owns the message now

            (await driver.TryFailAsync(stale!)).Should().BeFalse("the stale reservation token no longer matches");
            (await database.HashLengthAsync($"netqueue:{queue}:messages")).Should().Be(1, "the live copy was not touched");
            (await driver.TryFailAsync(live!)).Should().BeTrue();
        }
        finally
        {
            foreach (var key in new[] { "messages", "delayed", "reserved" }) await database.KeyDeleteAsync($"netqueue:{queue}:{key}");
            for (var p = 0; p <= 9; p++) await database.KeyDeleteAsync($"netqueue:{queue}:ready:{p}");
        }
    }
}
