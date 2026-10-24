using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Mail;
using Naravel.Mail.Transports;

namespace Naravel.Notifications.Mail.Tests;

public sealed record InvoiceReady(int InvoiceId);

public sealed record User(string Email);

public sealed class InvoiceMail(InvoiceReady invoice) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.Subject($"Invoice {invoice.InvoiceId}").Text("Your invoice is ready.").Build();
}

public sealed class MailChannelTests
{
    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mail:Default"] = "primary",
                ["Mail:From"] = "app@example.com",
                ["Mail:Mailers:primary:Driver"] = "array",
                ["Mail:Mailers:other:Driver"] = "array"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelMail(configuration);
        services.AddNaravelNotifications();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static async Task<ArrayMailTransport> TransportAsync(ServiceProvider provider, string name) =>
        (ArrayMailTransport)await provider.GetRequiredService<MailManager>().DriverAsync(name);

    [Fact]
    public async Task The_mail_channel_sends_the_mailable_to_the_recipients_address_with_the_default_from()
    {
        await using var provider = Build(s => s.AddMailNotificationChannel<InvoiceReady>(
            c => c.GetRecipient<User>().Email,
            n => new InvoiceMail(n)));

        await provider.GetRequiredService<INotificationSender>().SendAsync(new User("ada@example.com"), new InvoiceReady(7), ["mail"]);

        var sent = (await TransportAsync(provider, "primary")).Sent;
        sent.Should().ContainSingle();
        sent[0].To.Should().Equal("ada@example.com");
        sent[0].From.Should().Be("app@example.com");
        sent[0].Subject.Should().Be("Invoice 7");
    }

    [Fact]
    public async Task A_named_mailer_and_channel_name_can_be_chosen()
    {
        await using var provider = Build(s => s.AddMailNotificationChannel<InvoiceReady>(
            c => c.GetRecipient<User>().Email,
            n => new InvoiceMail(n),
            mailer: "other",
            name: "billing-mail"));

        await provider.GetRequiredService<INotificationSender>().SendAsync(new User("ada@example.com"), new InvoiceReady(1), ["billing-mail"]);

        (await TransportAsync(provider, "other")).Sent.Should().ContainSingle();
        (await TransportAsync(provider, "primary")).Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task The_mail_channel_composes_with_other_channels_in_one_call()
    {
        var seen = new List<string>();
        await using var provider = Build(s =>
        {
            s.AddMailNotificationChannel<InvoiceReady>(c => c.GetRecipient<User>().Email, n => new InvoiceMail(n));
            s.AddNotificationChannel<InvoiceReady>("audit", (c, _, _) =>
            {
                seen.Add(c.Channel);
                return Task.CompletedTask;
            });
        });

        await provider.GetRequiredService<INotificationSender>().SendAsync(new User("ada@example.com"), new InvoiceReady(1), ["audit", "mail"]);

        seen.Should().Equal("audit");
        (await TransportAsync(provider, "primary")).Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task An_invalid_address_fails_the_channel_and_nothing_is_sent()
    {
        await using var provider = Build(s => s.AddMailNotificationChannel<InvoiceReady>(
            c => c.GetRecipient<User>().Email,
            n => new InvoiceMail(n)));
        var act = () => provider.GetRequiredService<INotificationSender>().SendAsync(new User("not-an-address"), new InvoiceReady(1), ["mail"]);

        var exception = (await act.Should().ThrowAsync<NotificationDeliveryException>()).Which;

        exception.Channel.Should().Be("mail");
        exception.InnerException.Should().NotBeNull();
        (await TransportAsync(provider, "primary")).Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_recipient_of_the_wrong_type_is_reported_by_the_channel()
    {
        await using var provider = Build(s => s.AddMailNotificationChannel<InvoiceReady>(
            c => c.GetRecipient<User>().Email,
            n => new InvoiceMail(n)));
        var act = () => provider.GetRequiredService<INotificationSender>().SendAsync("just a string", new InvoiceReady(1), ["mail"]);

        var exception = (await act.Should().ThrowAsync<NotificationDeliveryException>()).Which;

        exception.InnerException.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("User");
    }

    [Fact]
    public void Registration_rejects_missing_delegates()
    {
        var services = new ServiceCollection();

        ((Action)(() => services.AddMailNotificationChannel<InvoiceReady>(null!, n => new InvoiceMail(n)))).Should().Throw<ArgumentNullException>();
        ((Action)(() => services.AddMailNotificationChannel<InvoiceReady>(c => "a@b.c", null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => services.AddMailNotificationChannel<InvoiceReady>(c => "a@b.c", n => new InvoiceMail(n), name: " "))).Should().Throw<ArgumentException>();
    }
}
