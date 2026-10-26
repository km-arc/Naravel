using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using Naravel.Queue.RabbitMQ;

namespace Naravel.Queue.Providers.Tests;

/// <summary>The ready-queue type is chosen when the queue is declared, so these tests pin the declared arguments (no broker needed).</summary>
public sealed class RabbitMqQueueTypeTests
{
    [Fact]
    public void Classic_is_the_default_and_keeps_priorities()
    {
        var arguments = RabbitMqQueueDriver.ReadyQueueArguments("mail", RabbitMqQueueType.Classic, null);

        arguments.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, object?>("x-max-priority", 9));
    }

    [Fact]
    public void Quorum_declares_the_type_dead_letters_to_the_failed_queue_and_drops_priorities()
    {
        var arguments = RabbitMqQueueDriver.ReadyQueueArguments("mail", RabbitMqQueueType.Quorum, 5);

        arguments["x-queue-type"].Should().Be("quorum");
        arguments["x-delivery-limit"].Should().Be(5);
        arguments["x-dead-letter-exchange"].Should().Be("");
        arguments["x-dead-letter-routing-key"].Should().Be("netqueue.failed.mail");
        arguments.Should().NotContainKey("x-max-priority");
    }

    [Fact]
    public void Quorum_without_a_limit_leaves_the_broker_default()
    {
        RabbitMqQueueDriver.ReadyQueueArguments("mail", RabbitMqQueueType.Quorum, null).Should().NotContainKey("x-delivery-limit");
    }

    [Fact]
    public void A_delivery_limit_needs_the_quorum_type_and_a_positive_value()
    {
        var classic = () => new RabbitMqQueueDriver("localhost", 5672, null, null, null, RabbitMqQueueType.Classic, 3);
        var zero = () => new RabbitMqQueueDriver("localhost", 5672, null, null, null, RabbitMqQueueType.Quorum, 0);

        classic.Should().Throw<ArgumentException>();
        zero.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Configuration_selects_the_queue_type_and_limit()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = "rabbit",
            ["NaravelQueue:Stores:rabbit:Driver"] = "rabbitmq",
            ["NaravelQueue:Stores:rabbit:QueueType"] = "quorum",
            ["NaravelQueue:Stores:rabbit:DeliveryLimit"] = "4"
        }).Build();
        var services = new ServiceCollection();
        services.AddQueue(configuration);
        services.AddRabbitMqDriver(configuration);

        // Building the driver must not connect; a wrong type or limit would throw here.
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<QueueManager>().Connection("rabbit").Should().BeOfType<RabbitMqQueueDriver>();
    }
}
