using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Mail;
using Naravel.Mail.Smtp;

namespace Naravel.Mail.Smtp.Tests;

public sealed class SmtpMailTransportTests
{
    private static SmtpMailTransport Transport(FakeSmtpServer server) => new(new SmtpMailOptions
    {
        Host = "127.0.0.1",
        Port = server.Port,
        Security = SmtpSecurity.None,
        Timeout = TimeSpan.FromSeconds(5)
    });

    [Fact]
    public async Task Sends_envelope_and_content_through_a_real_smtp_conversation()
    {
        await using var server = new FakeSmtpServer();
        var message = new MailMessage("app@example.com", new[] { "ada@example.com" }, "Welcome Ada", "plain text", "<p>html</p>",
            new[] { "cc@example.com" }, new[] { "bcc@example.com" });

        await Transport(server).SendAsync(message);

        (await server.WaitForMessageAsync()).Should().BeTrue();
        server.EnvelopeFrom.Should().ContainSingle().Which.Should().Contain("app@example.com");
        server.EnvelopeRecipients.Should().HaveCount(3);
        server.EnvelopeRecipients.Should().Contain(r => r.Contains("ada@example.com"));
        server.EnvelopeRecipients.Should().Contain(r => r.Contains("cc@example.com"));
        server.EnvelopeRecipients.Should().Contain(r => r.Contains("bcc@example.com"));
        var data = server.Data.Single();
        data.Should().Contain("Subject: Welcome Ada").And.Contain("plain text").And.Contain("<p>html</p>");
        data.Should().NotContain("bcc@example.com", "Bcc must be an envelope recipient only, never a header");
    }

    [Fact]
    public async Task A_rejected_recipient_is_thrown_to_the_caller()
    {
        await using var server = new FakeSmtpServer { RejectRecipientWith = "550 no such user" };
        var message = new MailMessage("app@example.com", "ghost@example.com", "Hi", "text");

        var act = () => Transport(server).SendAsync(message);

        await act.Should().ThrowAsync<SmtpCommandException>();
        server.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unreachable_server_is_thrown_not_swallowed()
    {
        var port = FreePort();
        var transport = new SmtpMailTransport(new SmtpMailOptions { Host = "127.0.0.1", Port = port, Security = SmtpSecurity.None, Timeout = TimeSpan.FromSeconds(2) });

        var act = () => transport.SendAsync(new MailMessage("a@example.com", "b@example.com", "x", "x"));

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task A_cancelled_token_stops_before_connecting()
    {
        await using var server = new FakeSmtpServer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Transport(server).SendAsync(new MailMessage("a@example.com", "b@example.com", "x", "x"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void A_subject_with_line_breaks_cannot_inject_a_header()
    {
        var message = new MailMessage("a@example.com", "b@example.com", "Hi\r\nBcc: evil@example.com", "text");

        var mime = SmtpMailTransport.ToMimeMessage(message);
        using var buffer = new MemoryStream();
        mime.WriteTo(buffer);
        var raw = System.Text.Encoding.UTF8.GetString(buffer.ToArray());

        raw.Should().NotContain("\r\nBcc:");
        mime.Bcc.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", 587, "Host")]
    [InlineData("mail.example.com", 0, "Port")]
    [InlineData("mail.example.com", 70000, "Port")]
    public void Invalid_options_fail_at_construction_with_a_message_naming_the_setting(string host, int port, string expected)
    {
        var act = () => new SmtpMailTransport(new SmtpMailOptions { Host = host, Port = port });

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void Defaults_require_STARTTLS_on_587()
    {
        var options = new SmtpMailOptions();

        options.Security.Should().Be(SmtpSecurity.StartTls);
        options.Port.Should().Be(587);
        SmtpMailTransport.ToSocketOptions(SmtpSecurity.StartTls).Should().Be(MailKit.Security.SecureSocketOptions.StartTls);
    }

    [Fact]
    public async Task The_smtp_driver_is_selected_by_configuration_and_delivers()
    {
        await using var server = new FakeSmtpServer();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mail:Default"] = "relay",
            ["Mail:From"] = "app@example.com",
            ["Mail:Mailers:relay:Driver"] = "smtp",
            ["Mail:Mailers:relay:Host"] = "127.0.0.1",
            ["Mail:Mailers:relay:Port"] = server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Mail:Mailers:relay:Security"] = "none",
            ["Mail:Mailers:relay:Timeout"] = "5"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelMail(configuration);
        services.AddNaravelMailSmtp(configuration);
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IMailer>().To("ada@example.com").SendAsync(new Welcome());

        (await server.WaitForMessageAsync()).Should().BeTrue();
        server.Data.Single().Should().Contain("Subject: Hello");
    }

    [Fact]
    public void Configuration_errors_name_the_offending_key()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mail:Mailers:relay:Driver"] = "smtp",
            ["Mail:Mailers:relay:Host"] = "mail.example.com",
            ["Mail:Mailers:relay:Security"] = "tls13"
        }).Build();

        var act = () => SmtpServiceCollectionExtensions.ReadOptions(configuration.GetSection("Mail:Mailers:relay"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Mail:Mailers:relay:Security*");
    }

    [Fact]
    public async Task Delivers_to_a_real_server_when_NARAVEL_TEST_SMTP_is_set()
    {
        var target = Environment.GetEnvironmentVariable("NARAVEL_TEST_SMTP");
        if (string.IsNullOrWhiteSpace(target)) return; // env-gated: host:port of a plain SMTP test server such as Mailpit

        var parts = target.Split(':');
        var transport = new SmtpMailTransport(new SmtpMailOptions
        {
            Host = parts[0],
            Port = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
            Security = SmtpSecurity.None
        });

        await transport.SendAsync(new MailMessage("app@example.com", "ada@example.com", "Naravel SMTP test", "ok"));
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class Welcome : Mailable
    {
        public override MailMessage Build(MailBuilder mail) => mail.Subject("Hello").Text("Hi").Build();
    }
}
