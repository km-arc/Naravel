using Naravel.Mail;

namespace Naravel.Mail.Tests;

public class MailMessageTests
{
    [Fact]
    public void A_valid_message_keeps_its_parts_and_trims_addresses()
    {
        var message = new MailMessage(" app@example.com ", new[] { "a@example.com", "Bob <b@example.com>" }, "Hello", "text", "<p>html</p>",
            new[] { "c@example.com" }, new[] { "d@example.com" });

        message.From.Should().Be("app@example.com");
        message.To.Should().Equal("a@example.com", "Bob <b@example.com>");
        message.Cc.Should().Equal("c@example.com");
        message.Bcc.Should().Equal("d@example.com");
        message.Subject.Should().Be("Hello");
        message.TextBody.Should().Be("text");
        message.HtmlBody.Should().Be("<p>html</p>");
    }

    [Fact]
    public void The_single_recipient_constructor_and_an_html_only_body_are_accepted()
    {
        var message = new MailMessage("app@example.com", "a@example.com", "Hi", null, "<p>x</p>");

        message.To.Should().Equal("a@example.com");
        message.TextBody.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("a@@example.com")]
    public void An_invalid_sender_is_rejected(string from)
    {
        var act = () => new MailMessage(from, "a@example.com", "Hi", "text");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_invalid_recipient_cc_or_bcc_is_rejected()
    {
        ((Action)(() => new MailMessage("app@example.com", "nope", "Hi", "text"))).Should().Throw<ArgumentException>();
        ((Action)(() => new MailMessage("app@example.com", new[] { "a@example.com" }, "Hi", "text", null, new[] { "bad" }, null)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => new MailMessage("app@example.com", new[] { "a@example.com" }, "Hi", "text", null, null, new[] { "bad" })))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void At_least_one_recipient_and_one_body_are_required()
    {
        ((Action)(() => new MailMessage("app@example.com", Array.Empty<string>(), "Hi", "text"))).Should().Throw<ArgumentException>();
        ((Action)(() => new MailMessage("app@example.com", "a@example.com", "Hi", null))).Should().Throw<ArgumentException>();
        ((Action)(() => new MailMessage("app@example.com", "a@example.com", "Hi", "", ""))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_null_subject_is_rejected_but_an_empty_one_is_allowed()
    {
        ((Action)(() => new MailMessage("app@example.com", "a@example.com", null!, "text"))).Should().Throw<ArgumentNullException>();
        new MailMessage("app@example.com", "a@example.com", "", "text").Subject.Should().BeEmpty();
    }
}
