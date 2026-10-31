using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Extensions;
using Naravel.Queue.Serialization;

namespace Naravel.Mail.Queue.Tests;

/// <summary>The job is registered with source-generated metadata, and its payload is the JSON the reflection serializer wrote.</summary>
public sealed class MailJobMetadataTests
{
    [Fact]
    public void The_job_uses_source_generated_metadata_and_keeps_its_payload()
    {
        var services = new ServiceCollection();
        services.AddQueue(new ConfigurationBuilder().Build());
        services.AddNaravelMailQueue();
        using var provider = services.BuildServiceProvider();
        var job = new SendQueuedMailJob
        {
            From = "app@example.com",
            To = ["a@example.com", "b@example.com"],
            Cc = ["c@example.com"],
            Subject = "Hi <there>",
            TextBody = "text",
            HtmlBody = "<p>html</p>",
            Transport = "smtp",
        };

        provider.GetRequiredService<IJobTypeRegistry>().GetJsonTypeInfo(typeof(SendQueuedMailJob)).Should().NotBeNull();
        var payload = provider.GetRequiredService<IJobSerializer>().Serialize(job);

        payload.Should().Be(JsonSerializer.Serialize(job, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
        var back = (SendQueuedMailJob)provider.GetRequiredService<IJobSerializer>().Deserialize(payload.Replace("\"Subject\"", "\"subject\""), typeof(SendQueuedMailJob));
        back.Subject.Should().Be("Hi <there>");
        back.To.Should().Equal("a@example.com", "b@example.com");
        back.Transport.Should().Be("smtp");
    }
}
