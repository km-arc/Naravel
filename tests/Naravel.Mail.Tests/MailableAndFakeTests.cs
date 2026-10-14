using Naravel.Mail;
using Naravel.Mail.Testing;

namespace Naravel.Mail.Tests;

public class MailableAndFakeTests
{
    private sealed class WelcomeMail(string name) : Mailable
    {
        public override MailMessage Build(MailBuilder mail) =>
            mail.Subject("Welcome").Text($"Hi {name}").Html($"<p>Hi {name}</p>").Build();
    }

    private sealed class FromMail : Mailable
    {
        public override MailMessage Build(MailBuilder mail) => mail.From("custom@example.com").Cc("c@example.com").Subject("S").Text("T").Build();
    }

    private sealed class NullMail : Mailable
    {
        public override MailMessage Build(MailBuilder mail) => null!;
    }

    [Fact]
    public async Task Sending_a_mailable_builds_it_with_the_recipients_and_the_default_sender()
    {
        var fake = new MailFake { DefaultFrom = "app@example.com" };

        await fake.To("a@example.com").To("b@example.com").SendAsync(new WelcomeMail("Ada"));

        var message = fake.AssertSent();
        message.From.Should().Be("app@example.com");
        message.To.Should().Equal("a@example.com", "b@example.com");
        message.Subject.Should().Be("Welcome");
        message.TextBody.Should().Be("Hi Ada");
        message.HtmlBody.Should().Be("<p>Hi Ada</p>");
    }

    [Fact]
    public async Task A_mailable_can_override_the_sender_and_add_cc()
    {
        var fake = new MailFake();

        await fake.To("a@example.com").SendAsync(new FromMail());

        var message = fake.AssertSent();
        message.From.Should().Be("custom@example.com");
        message.Cc.Should().Equal("c@example.com");
    }

    [Fact]
    public async Task A_missing_sender_is_reported_clearly()
    {
        var fake = new MailFake { DefaultFrom = null };

        var act = () => fake.To("a@example.com").SendAsync(new WelcomeMail("Ada"));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Mail:From");
    }

    [Fact]
    public async Task A_mailable_that_returns_null_is_reported_with_its_name()
    {
        var fake = new MailFake();

        var act = () => fake.To("a@example.com").SendAsync(new NullMail());

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("NullMail");
    }

    [Fact]
    public async Task Fake_assertions_cover_sent_queued_counts_and_reset()
    {
        var fake = new MailFake();
        fake.AssertNothingSent();
        fake.AssertNothingQueued();

        await fake.To("a@example.com").SendAsync(new WelcomeMail("Ada"));
        await fake.To("b@example.com").QueueAsync(new WelcomeMail("Bob"));

        fake.AssertSent(m => m.To.Contains("a@example.com")).Subject.Should().Be("Welcome");
        fake.AssertSent(1).Should().HaveCount(1);
        fake.AssertQueued(m => m.To.Contains("b@example.com")).TextBody.Should().Be("Hi Bob");
        ((Action)(() => fake.AssertSent(m => m.To.Contains("b@example.com")))).Should().Throw<InvalidOperationException>();
        ((Action)(() => fake.AssertSent(2))).Should().Throw<InvalidOperationException>();
        ((Action)fake.AssertNothingSent).Should().Throw<InvalidOperationException>();
        ((Action)fake.AssertNothingQueued).Should().Throw<InvalidOperationException>();

        fake.Reset();
        fake.Sent.Should().BeEmpty();
        fake.Queued.Should().BeEmpty();
        ((Action)(() => fake.AssertSent())).Should().Throw<InvalidOperationException>();
        ((Action)(() => fake.AssertQueued())).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task The_fake_honours_cancellation()
    {
        var fake = new MailFake();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var message = new MailMessage("app@example.com", "a@example.com", "S", "T");

        var send = () => fake.SendAsync(message, cts.Token);
        var queue = () => fake.QueueAsync(message, cts.Token);

        await send.Should().ThrowAsync<OperationCanceledException>();
        await queue.Should().ThrowAsync<OperationCanceledException>();
    }
}
