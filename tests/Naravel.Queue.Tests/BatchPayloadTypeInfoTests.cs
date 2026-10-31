using System.Text.Json;
using System.Text.Json.Serialization;
using Naravel.Queue.Batching;

namespace Naravel.Queue.Tests;

public sealed record BatchPayload(int Count, string Name);

[JsonSerializable(typeof(BatchPayload))]
internal sealed partial class BatchPayloadJsonContext : JsonSerializerContext;

/// <summary>The JsonTypeInfo overloads of the batch callbacks write and read the same JSON as the reflection based ones.</summary>
public class BatchPayloadTypeInfoTests
{
    private static readonly BatchPayload Payload = new(3, "nightly <x>");

    [Fact]
    public void Then_catch_and_finally_store_the_payload_the_reflection_overloads_would_store()
    {
        var typeInfo = BatchPayloadJsonContext.Default.BatchPayload;
        var options = new BatchOptions()
            .Then("then", Payload, typeInfo)
            .Catch("catch", Payload, typeInfo)
            .Finally("finally", Payload, typeInfo);

#pragma warning disable IL2026, IL3050 // the reflection overloads are the reference the metadata overloads must match
        var reflection = new BatchOptions().Then("then", Payload).Catch("catch", Payload).Finally("finally", Payload);
#pragma warning restore IL2026, IL3050

        options.ThenCallbacks.Should().Equal(reflection.ThenCallbacks);
        options.CatchCallbacks.Should().Equal(reflection.CatchCallbacks);
        options.FinallyCallbacks.Should().Equal(reflection.FinallyCallbacks);
        options.ThenCallbacks[0].Payload.Should().Be(JsonSerializer.Serialize(Payload));
    }

    [Fact]
    public async Task A_handler_registered_with_metadata_receives_the_payload()
    {
        var registry = new BatchCallbackRegistry();
        BatchPayload? received = null;
        registry.Register<BatchPayload>("done", (_, payload, _) =>
        {
            received = payload;
            return Task.CompletedTask;
        }, BatchPayloadJsonContext.Default.BatchPayload);
        var reference = new BatchOptions().Then("done", Payload, BatchPayloadJsonContext.Default.BatchPayload).ThenCallbacks;

        await registry.InvokeAsync(reference, new QueueBatch(), CancellationToken.None);

        received.Should().Be(Payload);
    }

    [Fact]
    public async Task A_handler_registered_without_metadata_still_receives_the_payload()
    {
        var registry = new BatchCallbackRegistry();
        BatchPayload? received = null;
#pragma warning disable IL2026, IL3050 // the reflection overload is what this test covers
        registry.Register<BatchPayload>("done", (_, payload, _) =>
        {
            received = payload;
            return Task.CompletedTask;
        });
#pragma warning restore IL2026, IL3050
        var reference = new BatchCallbackReference("done", JsonSerializer.Serialize(Payload));

        await registry.InvokeAsync([reference], new QueueBatch(), CancellationToken.None);

        received.Should().Be(Payload);
    }
}
