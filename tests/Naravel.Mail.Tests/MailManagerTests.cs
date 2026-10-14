using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Naravel.Mail;
using Naravel.Mail.Transports;

namespace Naravel.Mail.Tests;

public class MailManagerTests
{
    private sealed class WelcomeMail : Mailable
    {
        public override MailMessage Build(MailBuilder mail) => mail.Subject("Welcome").Text("Hi").Build();
    }

    private sealed class CapturingTransport : IMailTransport
    {
        public List<MailMessage> Sent { get; } = new();
        public Exception? Failure { get; set; }

        public Task SendAsync(MailMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);
        public void Dispose() { }

        private sealed class RecordingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (messages) messages.Add(formatter(state, exception));
            }
        }
    }

    private static ConfigurationManager Config(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values.ToDictionary(item => $"Mail:{item.Key}", item => item.Value));
        return configuration;
    }

    private static ServiceProvider Build(ConfigurationManager configuration, ILoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        if (logs is not null) services.AddLogging(b => b.AddProvider(logs));
        services.AddNaravelMail(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Without_any_configuration_mail_goes_to_the_log_transport_and_nothing_is_delivered()
    {
        var logs = new RecordingLoggerProvider();
        using var provider = Build(Config(), logs);
        var mailer = provider.GetRequiredService<IMailer>();

        await mailer.SendAsync(new MailMessage("app@example.com", "a@example.com", "Receipt", "text"));

        provider.GetRequiredService<MailManager>().DefaultDriverName.Should().Be("log");
        logs.Messages.Should().ContainSingle(m => m.Contains("a@example.com") && m.Contains("Receipt"));
        logs.Messages.Should().NotContain(m => m.Contains("text") && !m.Contains("Receipt"));
    }

    [Fact]
    public async Task The_default_mailer_sends_through_the_configured_default_transport_with_the_configured_sender()
    {
        using var provider = Build(Config(("Default", "array"), ("From", "app@example.com")));
        var mailer = provider.GetRequiredService<IMailer>();

        await mailer.To("user@example.com").SendAsync(new WelcomeMail());

        var array = (ArrayMailTransport)provider.GetRequiredService<MailManager>().Driver("array");
        array.Sent.Should().ContainSingle();
        array.Sent[0].From.Should().Be("app@example.com");
        array.Sent[0].To.Should().Equal("user@example.com");
    }

    [Fact]
    public async Task Named_mailers_from_configuration_are_separate_transport_instances()
    {
        using var provider = Build(Config(
            ("Default", "first"),
            ("Mailers:first:Driver", "array"),
            ("Mailers:second:Driver", "array")));
        var manager = provider.GetRequiredService<MailManager>();
        var message = new MailMessage("app@example.com", "a@example.com", "S", "T");

        await manager.Mailer().SendAsync(message);
        await manager.Mailer("second").SendAsync(message);
        await manager.Mailer("second").SendAsync(message);

        ((ArrayMailTransport)manager.Driver("first")).Sent.Should().HaveCount(1);
        ((ArrayMailTransport)manager.Driver("second")).Sent.Should().HaveCount(2);
        manager.Driver("first").Should().NotBeSameAs(manager.Driver("second"));
    }

    [Fact]
    public async Task A_transport_can_be_added_at_runtime_with_Extend()
    {
        using var provider = Build(Config());
        var manager = provider.GetRequiredService<MailManager>();
        var custom = new CapturingTransport();

        manager.Extend("custom", _ => custom);
        await manager.Mailer("custom").SendAsync(new MailMessage("app@example.com", "a@example.com", "S", "T"));

        custom.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task Config_reload_rebuilds_a_changed_mailer_ignores_unrelated_keys_and_follows_a_new_default()
    {
        var config = Config(("Default", "log"), ("Mailers:main:Driver", "array"), ("Mailers:main:Tag", "before"));
        using var provider = Build(config);
        var manager = provider.GetRequiredService<MailManager>();
        var original = manager.Driver("main");

        config["Mail:Unrelated"] = "x";
        ((IConfigurationRoot)config).Reload();
        manager.Driver("main").Should().BeSameAs(original);

        config["Mail:Mailers:main:Tag"] = "after";
        ((IConfigurationRoot)config).Reload();
        manager.Driver("main").Should().NotBeSameAs(original);

        var mailer = provider.GetRequiredService<IMailer>();
        config["Mail:Default"] = "main";
        ((IConfigurationRoot)config).Reload();
        await mailer.SendAsync(new MailMessage("app@example.com", "a@example.com", "S", "T"));

        manager.DefaultDriverName.Should().Be("main");
        ((ArrayMailTransport)manager.Driver("main")).Sent.Should().ContainSingle("the injected mailer follows the new default without being re-injected");
    }

    [Fact]
    public async Task A_transport_failure_is_reported_to_the_caller_and_never_swallowed_or_retried()
    {
        using var provider = Build(Config());
        var manager = provider.GetRequiredService<MailManager>();
        var failing = new CapturingTransport { Failure = new InvalidOperationException("smtp down") };
        manager.Extend("failing", _ => failing);

        var act = () => manager.Mailer("failing").SendAsync(new MailMessage("app@example.com", "a@example.com", "S", "T"));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("smtp down");
        failing.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_is_passed_to_the_transport_and_surfaces_as_OperationCanceledException()
    {
        using var provider = Build(Config());
        var manager = provider.GetRequiredService<MailManager>();
        manager.Extend("custom", _ => new CapturingTransport());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => manager.Mailer("custom").SendAsync(new MailMessage("app@example.com", "a@example.com", "S", "T"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task An_unknown_transport_name_fails_with_a_clear_error()
    {
        using var provider = Build(Config());
        var manager = provider.GetRequiredService<MailManager>();

        var act = () => manager.Mailer("missing").SendAsync(new MailMessage("app@example.com", "a@example.com", "S", "T"));

        await act.Should().ThrowAsync<Naravel.Foundation.DriverNotRegisteredException>();
    }

    [Fact]
    public async Task Queueing_without_a_queue_adapter_explains_what_to_install()
    {
        using var provider = Build(Config(("From", "app@example.com")));
        var mailer = provider.GetRequiredService<IMailer>();

        var act = () => mailer.To("a@example.com").QueueAsync(new WelcomeMail());

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Naravel.Mail.Queue");
    }

    [Fact]
    public async Task Queueing_hands_the_built_message_and_the_transport_name_to_the_registered_adapter()
    {
        var queue = new RecordingQueue();
        var services = new ServiceCollection();
        services.AddNaravelMail(Config(("From", "app@example.com"), ("Default", "array")));
        services.AddSingleton<IMailQueue>(queue);
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<MailManager>().Mailer("array").To("a@example.com").QueueAsync(new WelcomeMail());
        await provider.GetRequiredService<IMailer>().To("b@example.com").QueueAsync(new WelcomeMail());

        queue.Items.Should().HaveCount(2);
        queue.Items[0].Message.To.Should().Equal("a@example.com");
        queue.Items[0].Transport.Should().Be("array");
        queue.Items[1].Transport.Should().BeNull();
    }

    private sealed class RecordingQueue : IMailQueue
    {
        public List<(MailMessage Message, string? Transport)> Items { get; } = new();

        public Task EnqueueAsync(MailMessage message, string? transport, CancellationToken cancellationToken = default)
        {
            Items.Add((message, transport));
            return Task.CompletedTask;
        }
    }
}
