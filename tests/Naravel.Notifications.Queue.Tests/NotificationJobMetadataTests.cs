using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Extensions;
using Naravel.Queue.Serialization;

namespace Naravel.Notifications.Queue.Tests;

/// <summary>The job is registered with source-generated metadata, and its payload is the JSON the reflection serializer wrote.</summary>
public sealed class NotificationJobMetadataTests
{
    [Fact]
    public void The_job_uses_source_generated_metadata_and_keeps_its_payload()
    {
        var services = new ServiceCollection();
        services.AddQueue(new ConfigurationBuilder().Build());
        services.AddNaravelNotificationQueue();
        using var provider = services.BuildServiceProvider();
        var job = new SendQueuedNotificationJob { Alias = "invoice.ready", Payload = "{\"Email\":\"a@b.test\",\"InvoiceId\":7}" };

        provider.GetRequiredService<IJobTypeRegistry>().GetJsonTypeInfo(typeof(SendQueuedNotificationJob)).Should().NotBeNull();
        var serializer = provider.GetRequiredService<IJobSerializer>();
        var payload = serializer.Serialize(job);

        payload.Should().Be(JsonSerializer.Serialize(job, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
        var back = (SendQueuedNotificationJob)serializer.Deserialize(payload.Replace("\"Alias\"", "\"alias\""), typeof(SendQueuedNotificationJob));
        back.Alias.Should().Be("invoice.ready");
        back.Payload.Should().Be(job.Payload);
    }
}
