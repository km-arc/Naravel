using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Notifications.Queue;
using Naravel.Queue.Extensions;
using Naravel.Queue.Memory;

namespace Naravel.Notifications.Queue.Tests;

[JsonSerializable(typeof(InvoicePayload))]
internal sealed partial class InvoicePayloadContext : JsonSerializerContext;

/// <summary>The JsonTypeInfo overload of AddQueuedNotification (trimming and Native AOT): same stored format, no reflection.</summary>
public sealed class QueuedNotificationTypeInfoTests
{
    private const string Alias = "invoice.typed";

    private static ServiceCollection Services()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["NaravelQueue:Default"] = "notify", ["NaravelQueue:Stores:notify:Driver"] = "memory" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueue(configuration).AddMemoryDriver(configuration);
        services.AddNaravelNotifications();
        services.AddNaravelNotificationQueue();
        return services;
    }

    [Fact]
    public async Task The_typed_registration_writes_the_same_payload_JSON_as_the_reflection_one_and_runs_the_handler()
    {
        InvoicePayload? received = null;
        var services = Services();
        services.AddQueuedNotification(Alias, InvoicePayloadContext.Default.InvoicePayload, (payload, _, _) =>
        {
            received = payload;
            return Task.CompletedTask;
        });
        await using var provider = services.BuildServiceProvider();
        var entry = provider.GetRequiredService<QueuedNotificationRegistry>().Get(Alias);
        var original = new InvoicePayload("ada@example.com", 7);

        var json = entry.Serialize(original);
        await entry.Run(json, provider, CancellationToken.None);

        json.Should().Be("""{"Email":"ada@example.com","InvoiceId":7}""");
        json.Should().Be(JsonSerializer.Serialize(original));
        received.Should().Be(original);
    }

    [Fact]
    public async Task The_typed_registration_reports_an_empty_stored_payload()
    {
        var services = Services();
        services.AddQueuedNotification(Alias, InvoicePayloadContext.Default.InvoicePayload, (_, _, _) => Task.CompletedTask);
        await using var provider = services.BuildServiceProvider();
        var entry = provider.GetRequiredService<QueuedNotificationRegistry>().Get(Alias);

        var act = () => entry.Run("null", provider, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*empty*");
    }

    [Fact]
    public void The_typed_registration_validates_its_arguments()
    {
        var services = Services();

        ((Action)(() => services.AddQueuedNotification<InvoicePayload>(Alias, null!, (_, _, _) => Task.CompletedTask))).Should().Throw<ArgumentNullException>();
        ((Action)(() => services.AddQueuedNotification(" ", InvoicePayloadContext.Default.InvoicePayload, (_, _, _) => Task.CompletedTask))).Should().Throw<ArgumentException>();
    }
}
