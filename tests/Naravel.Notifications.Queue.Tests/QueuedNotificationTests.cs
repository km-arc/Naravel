using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Naravel.Notifications.Queue;
using Naravel.Queue.Extensions;
using Naravel.Queue.Memory;

namespace Naravel.Notifications.Queue.Tests;

public sealed record InvoiceReady(int InvoiceId);

public sealed record User(string Email);

public sealed record InvoicePayload(string Email, int InvoiceId);

public sealed class QueuedNotificationTests
{
    private const string Alias = "invoice.ready";

    private static ServiceCollection BaseServices(ConcurrentQueue<string>? delivered = null, bool register = true)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NaravelQueue:Default"] = "notify",
                ["NaravelQueue:Stores:notify:Driver"] = "memory"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueue(configuration).AddMemoryDriver(configuration);
        services.AddNaravelNotifications();
        services.AddNaravelNotificationQueue();
        if (delivered is not null)
        {
            services.AddNotificationChannel<InvoiceReady>("record", (context, notification, _) =>
            {
                delivered.Enqueue($"{context.GetRecipient<User>().Email}|{notification.InvoiceId}|{context.Channel}");
                return Task.CompletedTask;
            });
        }

        if (register)
        {
            services.AddQueuedNotification<InvoicePayload>(Alias, (payload, sender, ct) =>
                sender.SendAsync(new User(payload.Email), new InvoiceReady(payload.InvoiceId), ["record"], ct));
        }

        return services;
    }

    [Fact]
    public void The_stored_job_holds_only_an_alias_and_plain_payload_data()
    {
        var job = new SendQueuedNotificationJob { Alias = Alias, Payload = JsonSerializer.Serialize(new InvoicePayload("ada@example.com", 7)) };

        var json = JsonSerializer.Serialize(job);

        json.Should().Contain("\"Alias\":\"invoice.ready\"").And.Contain("ada@example.com");
        json.Should().NotContain("InvoiceReady").And.NotContain("User").And.NotContain("Naravel.Notifications.Queue.Tests").And.NotContain("Func");
        var roundTrip = JsonSerializer.Deserialize<SendQueuedNotificationJob>(json)!;
        roundTrip.Alias.Should().Be(Alias);
        JsonSerializer.Deserialize<InvoicePayload>(roundTrip.Payload).Should().Be(new InvoicePayload("ada@example.com", 7));
    }

    [Fact]
    public async Task Queued_notifications_are_delivered_by_the_worker_through_the_registered_channel()
    {
        var delivered = new ConcurrentQueue<string>();
        var services = BaseServices(delivered);
        services.AddQueueWorker(options =>
        {
            options.Connection = "notify";
            options.Queues = ["default"];
            options.Rest = TimeSpan.FromMilliseconds(10);
        });
        await using var provider = services.BuildServiceProvider();
        var workers = provider.GetServices<IHostedService>().ToArray();
        foreach (var worker in workers) await worker.StartAsync(CancellationToken.None);

        try
        {
            await provider.GetRequiredService<INotificationQueue>().QueueAsync(Alias, new InvoicePayload("ada@example.com", 7));

            (await WaitUntilAsync(() => delivered.Count == 1)).Should().BeTrue();
            delivered.Single().Should().Be("ada@example.com|7|record");
        }
        finally
        {
            foreach (var worker in workers) await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Queueing_an_unregistered_alias_fails_with_a_clear_message()
    {
        await using var provider = BaseServices(register: false).BuildServiceProvider();
        var act = () => provider.GetRequiredService<INotificationQueue>().QueueAsync("nope", new InvoicePayload("a@b.c", 1));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'nope'*AddQueuedNotification*");
    }

    [Fact]
    public async Task Queueing_a_payload_of_another_type_than_the_registered_one_fails()
    {
        await using var provider = BaseServices().BuildServiceProvider();
        var act = () => provider.GetRequiredService<INotificationQueue>().QueueAsync(Alias, new User("a@b.c"));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*InvoicePayload*User*");
    }

    [Fact]
    public async Task Two_registrations_of_one_alias_are_refused()
    {
        var services = BaseServices();
        services.AddQueuedNotification<InvoicePayload>(Alias, (_, _, _) => Task.CompletedTask);
        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<INotificationQueue>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*more than once*");
    }

    [Fact]
    public async Task A_tampered_alias_fails_the_job_instead_of_guessing_a_type()
    {
        await using var provider = BaseServices().BuildServiceProvider();
        var registry = provider.GetRequiredService<QueuedNotificationRegistry>();

        var act = () => registry.Get("Naravel.Something.EvilType, Naravel");

        act.Should().Throw<InvalidOperationException>().WithMessage("*alias*");
    }

    [Fact]
    public async Task An_empty_stored_payload_is_reported()
    {
        var services = BaseServices();
        await using var provider = services.BuildServiceProvider();
        var entry = provider.GetRequiredService<QueuedNotificationRegistry>().Get(Alias);

        var act = () => entry.Run("null", new Naravel.Notifications.Testing.NotificationFake(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*empty*");
    }

    [Fact]
    public void Registration_rejects_bad_arguments()
    {
        var services = new ServiceCollection();

        ((Action)(() => services.AddQueuedNotification<InvoicePayload>(" ", (_, _, _) => Task.CompletedTask))).Should().Throw<ArgumentException>();
        ((Action)(() => services.AddQueuedNotification<InvoicePayload>(Alias, null!))).Should().Throw<ArgumentNullException>();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }

        return condition();
    }
}
