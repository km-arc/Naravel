using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Mail.Testing;
using Naravel.Mail.Transports;

namespace Naravel.Mail.Tests;

// Runs the snippets and the error messages of docs/en/mail.md. If a test here needs a change,
// the guide needs the same change.
public sealed class DocsExampleTests
{
    private sealed class WelcomeMail(string name) : Mailable
    {
        public override MailMessage Build(MailBuilder mail) =>
            mail.Subject("Welcome").Text($"Hi {name}").Html($"<p>Hi {name}</p>").Build();
    }

    private sealed class MonthlyReport : Mailable
    {
        public override MailMessage Build(MailBuilder mail) =>
            mail.From("reports@example.com")
                .Cc("manager@example.com")
                .Bcc("archive@example.com")
                .Subject("Monthly report")
                .Text("Your monthly report is ready.")
                .Build();
    }

    private sealed class NullMail : Mailable
    {
        public override MailMessage Build(MailBuilder mail) => null!;
    }

    private static ServiceProvider Build()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mail:Default"] = "array",
            ["Mail:From"] = "app@example.com",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelMail(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task The_quickstart_mailable_is_delivered_with_the_configured_sender()
    {
        await using var provider = Build();

        await provider.GetRequiredService<IMailer>().To("ada@example.com").SendAsync(new WelcomeMail("Ada"));

        var array = (ArrayMailTransport)provider.GetRequiredService<MailManager>().Driver("array");
        var sent = array.Sent.Should().ContainSingle().Subject;
        sent.From.Should().Be("app@example.com");
        sent.To.Should().Equal("ada@example.com");
        sent.Subject.Should().Be("Welcome");
        sent.TextBody.Should().Be("Hi Ada");
        sent.HtmlBody.Should().Be("<p>Hi Ada</p>");
    }

    [Fact]
    public async Task A_mailable_can_set_its_own_sender_and_copies()
    {
        await using var provider = Build();

        await provider.GetRequiredService<IMailer>().To("ada@example.com").SendAsync(new MonthlyReport());

        var array = (ArrayMailTransport)provider.GetRequiredService<MailManager>().Driver("array");
        var sent = array.Sent.Should().ContainSingle().Subject;
        sent.From.Should().Be("reports@example.com");
        sent.Cc.Should().Equal("manager@example.com");
        sent.Bcc.Should().Equal("archive@example.com");
    }

    [Fact]
    public async Task The_errors_in_the_guide_carry_these_messages()
    {
        FluentActions.Invoking(() => new MailBuilder(new[] { "ada@example.com" }).Subject("S").Text("T").Build())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The mail has no sender. Call From(...) in Mailable.Build or set Mail:From in configuration.");

        var fake = new MailFake { DefaultFrom = "app@example.com" };
        await FluentActions.Awaiting(() => fake.To("ada@example.com").SendAsync(new NullMail()))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("NullMail.Build returned null. Return mail.Build().");

        FluentActions.Invoking(() => new MailMessage("app@example.com", "not-an-address", "S", "B"))
            .Should().Throw<ArgumentException>().WithMessage("'not-an-address' is not a valid e-mail address.*");
        FluentActions.Invoking(() => new MailMessage("app@example.com", Array.Empty<string>(), "S", "B"))
            .Should().Throw<ArgumentException>().WithMessage("A mail message needs at least one recipient.*");
        FluentActions.Invoking(() => new MailMessage("app@example.com", "ada@example.com", "S", null))
            .Should().Throw<ArgumentException>().WithMessage("A mail message needs a text body, an HTML body, or both.*");

        await using var provider = Build();
        await FluentActions.Awaiting(() => provider.GetRequiredService<IMailer>().To("ada@example.com").QueueAsync(new WelcomeMail("Ada")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No mail queue adapter is registered. Install Naravel.Mail.Queue and call services.AddNaravelMailQueue(), or use SendAsync.");
    }

    [Fact]
    public async Task MailFake_assertions_fail_with_the_messages_in_the_guide()
    {
        var fake = new MailFake { DefaultFrom = "app@example.com" };
        FluentActions.Invoking(() => fake.AssertSent()).Should().Throw<InvalidOperationException>()
            .WithMessage("No mail message was sent.");

        await fake.To("ada@example.com").SendAsync(new WelcomeMail("Ada"));
        await fake.To("ada@example.com").QueueAsync(new WelcomeMail("Ada"));

        FluentActions.Invoking(() => fake.AssertSent(m => m.Subject == "Other")).Should().Throw<InvalidOperationException>()
            .WithMessage("No matching mail message was sent (1 sent in total).");
        FluentActions.Invoking(() => fake.AssertSentCount(2)).Should().Throw<InvalidOperationException>()
            .WithMessage("Expected 2 sent mail message(s), but found 1.");
        FluentActions.Invoking(() => fake.AssertNothingSent()).Should().Throw<InvalidOperationException>()
            .WithMessage("Expected no mail to be sent, but 1 message(s) were sent.");
        FluentActions.Invoking(() => fake.AssertNothingQueued()).Should().Throw<InvalidOperationException>()
            .WithMessage("Expected no mail to be queued, but 1 message(s) were queued.");
    }
}
