using Naravel.Queue.Drivers;
using Naravel.Queue.Kafka;

namespace Naravel.Queue.Providers.Tests;

/// <summary>
/// PDR-012a (AUDIT D-29): Kafka is an immediate-delivery driver. These cases need no broker: the guard runs before
/// anything is produced, and the producer is created lazily without connecting.
/// </summary>
public sealed class KafkaDelayedDeliveryTests
{
    [Fact]
    public void The_driver_declares_that_it_does_not_support_delayed_delivery()
    {
        using var driver = new KafkaQueueDriver("localhost:9092", "unit-tests");

        ((IQueueDriver)driver).SupportsDelayedDelivery.Should().BeFalse();
    }

    [Fact]
    public async Task A_delayed_dispatch_is_rejected_with_an_explanation_before_anything_is_produced()
    {
        using var driver = new KafkaQueueDriver("localhost:9092", "unit-tests");
        var message = new QueuedMessage
        {
            Queue = "emails",
            JobType = "unit-test",
            Payload = "{}",
            AvailableAt = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1)
        };

        var push = () => driver.PushAsync(message);

        var thrown = await push.Should().ThrowAsync<NotSupportedException>();
        thrown.Which.Message.Should().Contain("delayed delivery").And.Contain("emails").And.Contain("Redis");
    }
}
