using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Events.Queue;
using Naravel.Queue.Extensions;
using Naravel.Queue.Serialization;

namespace Naravel.Events.Tests;

/// <summary>The job is registered with source-generated metadata, and its payload is the JSON the reflection serializer wrote.</summary>
public sealed class QueuedEventJobMetadataTests
{
    [Fact]
    public void The_job_uses_source_generated_metadata_and_keeps_its_payload()
    {
        var services = new ServiceCollection();
        services.AddQueue(new ConfigurationBuilder().Build());
        services.AddNaravelEventsQueue();
        using var provider = services.BuildServiceProvider();
        var job = new QueuedEventListenerJob { ListenerAlias = "invoice.ready", EventPayload = "{\"InvoiceId\":\"i1\"}" };

        provider.GetRequiredService<IJobTypeRegistry>().GetJsonTypeInfo(typeof(QueuedEventListenerJob)).Should().NotBeNull();
        var serializer = provider.GetRequiredService<IJobSerializer>();
        var payload = serializer.Serialize(job);

        payload.Should().Be(JsonSerializer.Serialize(job, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
        var back = (QueuedEventListenerJob)serializer.Deserialize(payload.Replace("\"ListenerAlias\"", "\"listeneralias\""), typeof(QueuedEventListenerJob));
        back.ListenerAlias.Should().Be("invoice.ready");
        back.EventPayload.Should().Be(job.EventPayload);
    }
}
