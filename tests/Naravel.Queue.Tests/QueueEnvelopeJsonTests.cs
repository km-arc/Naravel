using System.Text.Json;
using Naravel.Queue.Batching;
using Naravel.Queue.Drivers;
using Naravel.Queue.Serialization;

namespace Naravel.Queue.Tests;

/// <summary>The shared envelope JSON must stay byte-for-byte what the default reflection based serializer wrote.</summary>
public class QueueEnvelopeJsonTests
{
    private static QueuedMessage Sample() => new()
    {
        Id = "abc123",
        Queue = "emails",
        Connection = "redis",
        JobType = "send-mail",
        Payload = "{\"To\":\"a@b.test\"}",
        Attempts = 2,
        MaxAttempts = 5,
        Priority = 7,
        CreatedAt = new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero),
        AvailableAt = new DateTimeOffset(2026, 10, 30, 8, 5, 0, TimeSpan.FromHours(3.5)),
        ReservedAt = new DateTimeOffset(2026, 10, 30, 8, 6, 0, TimeSpan.Zero),
        ReservationId = "r1",
        Error = "boom <&>",
        ChainedJobPayload = "[{\"JobType\":\"x\",\"Payload\":\"{}\"}]",
        BatchId = "b1",
        TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        TraceState = "k=v",
    };

    [Fact]
    public void The_envelope_json_equals_what_the_reflection_serializer_wrote()
    {
        var message = Sample();

        QueueEnvelopeJson.Serialize(message).Should().Be(JsonSerializer.Serialize(message));
        QueueEnvelopeJson.SerializeToUtf8Bytes(message).Should().Equal(JsonSerializer.SerializeToUtf8Bytes(message));
    }

    [Fact]
    public void An_envelope_round_trips_and_reads_what_the_reflection_serializer_wrote()
    {
        var message = Sample();

        var fromOwn = QueueEnvelopeJson.Deserialize(QueueEnvelopeJson.Serialize(message))!;
        var fromReflection = QueueEnvelopeJson.Deserialize(JsonSerializer.Serialize(message))!;

        fromOwn.Should().BeEquivalentTo(message);
        fromReflection.Should().BeEquivalentTo(message);
    }

    [Fact]
    public void Clone_is_an_independent_copy()
    {
        var message = Sample();

        var copy = QueueEnvelopeJson.Clone(message);
        copy.Id = "changed";

        copy.Should().NotBeSameAs(message);
        message.Id.Should().Be("abc123");
        copy.Payload.Should().Be(message.Payload);
    }

    [Fact]
    public void A_corrupt_envelope_throws_JsonException()
    {
        var act = () => QueueEnvelopeJson.Deserialize("{ not json");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Batch_callbacks_keep_the_json_array_shape_and_round_trip()
    {
        IReadOnlyList<BatchCallbackReference> callbacks =
        [
            new BatchCallbackReference("done", "{\"Id\":1}"),
            new BatchCallbackReference("audit", "\"x\""),
        ];

        var json = QueueEnvelopeJson.SerializeCallbacks(callbacks);

        json.Should().Be(JsonSerializer.Serialize(callbacks));
        QueueEnvelopeJson.DeserializeCallbacks(json).Should().Equal(callbacks);
        QueueEnvelopeJson.DeserializeCallbacks("null").Should().BeEmpty();
        QueueEnvelopeJson.DeserializeCallbacks("[]").Should().BeEmpty();
    }
}
