using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Naravel.Mail;
using Naravel.Mail.Queue;
using Naravel.Mail.Transports;
using Naravel.Queue.Extensions;

namespace Naravel.Mail.Queue.Tests;

public sealed class WelcomeMail : Mailable
{
    public override MailMessage Build(MailBuilder mail) => mail.Subject("Welcome").Text("Hello from the queue").Build();
}

public sealed class MailQueueTests
{
    [Fact]
    public void Job_payload_contains_only_message_data()
    {
        var message = new MailMessage("app@example.com", new[] { "ada@example.com" }, "Hi", "text", "<p>html</p>",
            new[] { "cc@example.com" }, new[] { "bcc@example.com" });

        var json = JsonSerializer.Serialize(SendQueuedMailJob.FromMessage(message, "smtp"));

        json.Should().Contain("ada@example.com").And.Contain("\"Transport\":\"smtp\"");
        json.Should().NotContain("Mailable").And.NotContain("Welcome").And.NotContain("Naravel.Mail.Tests");
        var roundTrip = JsonSerializer.Deserialize<SendQueuedMailJob>(json)!;
        roundTrip.To.Should().Equal("ada@example.com");
        roundTrip.Cc.Should().Equal("cc@example.com");
        roundTrip.Bcc.Should().Equal("bcc@example.com");
        roundTrip.HtmlBody.Should().Be("<p>html</p>");
        roundTrip.Transport.Should().Be("smtp");
    }

    [Fact]
    public async Task Job_rejects_a_tampered_address_when_it_runs()
    {
        var job = new SendQueuedMailJob { From = "app@example.com", To = ["not-an-address"], Subject = "x", TextBody = "x" };
        var act = () => job.HandleAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Queued_mail_is_delivered_by_the_worker_through_the_named_transport()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NaravelQueue:Default"] = "mail",
                ["NaravelQueue:Stores:mail:Driver"] = "memory",
                ["Mail:Default"] = "array",
                ["Mail:From"] = "app@example.com",
                ["Mail:Mailers:array:Driver"] = "array"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueue(configuration).AddMemoryDriver(configuration);
        services.AddNaravelMail(configuration);
        services.AddNaravelMailQueue();
        services.AddQueueWorker(options =>
        {
            options.Connection = "mail";
            options.Queues = ["default"];
            options.Rest = TimeSpan.FromMilliseconds(10);
        });
        await using var provider = services.BuildServiceProvider();
        var workers = provider.GetServices<IHostedService>().ToArray();
        foreach (var worker in workers) await worker.StartAsync(CancellationToken.None);

        try
        {
            var manager = provider.GetRequiredService<MailManager>();
            await manager.Mailer().To("ada@example.com").QueueAsync(new WelcomeMail());

            var transport = (ArrayMailTransport)await manager.DriverAsync("array");
            (await WaitUntilAsync(() => transport.Sent.Count == 1)).Should().BeTrue();
            transport.Sent[0].Subject.Should().Be("Welcome");
            transport.Sent[0].To.Should().Equal("ada@example.com");
        }
        finally
        {
            foreach (var worker in workers) await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task QueueAsync_without_the_adapter_explains_what_to_install()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mail:From"] = "app@example.com" }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelMail(configuration);
        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IMailer>().To("ada@example.com").QueueAsync(new WelcomeMail());

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*AddNaravelMailQueue*");
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
