using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Naravel.Notifications.Testing;

namespace Naravel.Notifications.Tests;

public sealed record InvoiceReady(int InvoiceId);

public sealed record User(string Email);

public sealed class NotificationSenderTests
{
    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddNaravelNotifications();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static IServiceCollection Record(IServiceCollection services, List<string> calls, string name) =>
        services.AddNotificationChannel<InvoiceReady>(name, (context, _, _) =>
        {
            lock (calls) calls.Add(context.Channel);
            return Task.CompletedTask;
        });

    [Fact]
    public async Task Channels_run_in_the_order_the_caller_names_them_not_the_registration_order()
    {
        var calls = new List<string>();
        await using var provider = Build(s => { Record(s, calls, "mail"); Record(s, calls, "log"); Record(s, calls, "sms"); });
        var sender = provider.GetRequiredService<INotificationSender>();

        await sender.SendAsync(new User("ada@example.com"), new InvoiceReady(7), ["sms", "mail", "log"]);

        calls.Should().Equal("sms", "mail", "log");
    }

    [Fact]
    public async Task Channel_names_are_matched_ignoring_case()
    {
        var calls = new List<string>();
        await using var provider = Build(s => Record(s, calls, "Mail"));

        await provider.GetRequiredService<INotificationSender>().SendAsync(new User("a@b.c"), new InvoiceReady(1), ["MAIL"]);

        calls.Should().Equal("Mail");
    }

    [Fact]
    public async Task A_missing_channel_fails_before_any_delivery()
    {
        var calls = new List<string>();
        await using var provider = Build(s => Record(s, calls, "mail"));
        var sender = provider.GetRequiredService<INotificationSender>();

        var act = () => sender.SendAsync(new User("a@b.c"), new InvoiceReady(1), ["mail", "sms"]);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*'sms'*InvoiceReady*mail*");
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_missing_channel_message_says_when_nothing_is_registered()
    {
        await using var provider = Build(_ => { });
        var act = () => provider.GetRequiredService<INotificationSender>().SendAsync(new User("a@b.c"), new InvoiceReady(1), ["mail"]);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Registered: none*");
    }

    [Fact]
    public async Task A_channel_name_registered_twice_for_one_notification_is_refused()
    {
        var calls = new List<string>();
        await using var provider = Build(s => { Record(s, calls, "mail"); Record(s, calls, "mail"); });
        var act = () => provider.GetRequiredService<INotificationSender>().SendAsync(new User("a@b.c"), new InvoiceReady(1), ["mail"]);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*more than once*");
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Channels_are_registered_per_notification_type()
    {
        var calls = new List<string>();
        await using var provider = Build(s => Record(s, calls, "mail"));
        var act = () => provider.GetRequiredService<INotificationSender>().SendAsync(new User("a@b.c"), new User("other"), ["mail"]);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*User*");
    }

    [Fact]
    public async Task Bad_arguments_are_rejected()
    {
        await using var provider = Build(_ => { });
        var sender = provider.GetRequiredService<INotificationSender>();
        var user = new User("a@b.c");
        var invoice = new InvoiceReady(1);

        await ((Func<Task>)(() => sender.SendAsync(user, invoice, []))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => sender.SendAsync(user, invoice, [" "]))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => sender.SendAsync(user, invoice, ["mail", "MAIL"]))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => sender.SendAsync(null!, invoice, ["mail"]))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sender.SendAsync<InvoiceReady>(user, null!, ["mail"]))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sender.SendAsync(user, invoice, null!))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task A_failing_channel_stops_the_dispatch_and_reports_what_was_delivered()
    {
        var calls = new List<string>();
        await using var provider = Build(s =>
        {
            Record(s, calls, "mail");
            s.AddNotificationChannel<InvoiceReady>("sms", (_, _, _) => throw new TimeoutException("gateway"));
            Record(s, calls, "log");
        });
        var act = () => provider.GetRequiredService<INotificationSender>().SendAsync(new User("a@b.c"), new InvoiceReady(1), ["mail", "sms", "log"]);

        var exception = (await act.Should().ThrowAsync<NotificationDeliveryException>()).Which;

        exception.Channel.Should().Be("sms");
        exception.DeliveredChannels.Should().Equal("mail");
        exception.InnerException.Should().BeOfType<TimeoutException>().Which.Message.Should().Be("gateway");
        exception.Message.Should().Contain("sms").And.Contain("mail");
        calls.Should().Equal("mail");
    }

    [Fact]
    public async Task A_first_channel_failure_says_nothing_was_delivered()
    {
        await using var provider = Build(s => s.AddNotificationChannel<InvoiceReady>("mail", (_, _, _) => throw new InvalidOperationException("down")));
        var act = () => provider.GetRequiredService<INotificationSender>().SendAsync(new User("a@b.c"), new InvoiceReady(1), ["mail"]);

        var exception = (await act.Should().ThrowAsync<NotificationDeliveryException>()).Which;

        exception.DeliveredChannels.Should().BeEmpty();
        exception.Message.Should().Contain("no channel had delivered");
    }

    [Fact]
    public async Task A_cancelled_token_runs_no_channel()
    {
        var calls = new List<string>();
        await using var provider = Build(s => Record(s, calls, "mail"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => provider.GetRequiredService<INotificationSender>().SendAsync(new User("a@b.c"), new InvoiceReady(1), ["mail"], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_inside_a_channel_is_not_wrapped_and_stops_the_dispatch()
    {
        var calls = new List<string>();
        using var cts = new CancellationTokenSource();
        await using var provider = Build(s =>
        {
            s.AddNotificationChannel<InvoiceReady>("first", async (_, _, _) =>
            {
                await cts.CancelAsync();
            });
            Record(s, calls, "second");
        });

        var act = () => provider.GetRequiredService<INotificationSender>().SendAsync(new User("a@b.c"), new InvoiceReady(1), ["first", "second"], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task The_channel_receives_the_explicit_recipient_and_its_own_name()
    {
        NotificationContext? seen = null;
        var user = new User("ada@example.com");
        await using var provider = Build(s => s.AddNotificationChannel<InvoiceReady>("mail", (context, _, _) =>
        {
            seen = context;
            return Task.CompletedTask;
        }));

        await provider.GetRequiredService<INotificationSender>().SendAsync(user, new InvoiceReady(1), ["mail"]);

        seen.Should().NotBeNull();
        seen!.Recipient.Should().BeSameAs(user);
        seen.Channel.Should().Be("mail");
        seen.GetRecipient<User>().Email.Should().Be("ada@example.com");
        var wrong = () => seen.GetRecipient<InvoiceReady>();
        wrong.Should().Throw<InvalidOperationException>().WithMessage("*InvoiceReady*User*");
    }

    private sealed class Counter
    {
        public int Sends;
    }

    private sealed class ScopedMailChannel(Counter counter) : INotificationChannel<InvoiceReady>
    {
        public string Name => "mail";

        public Task SendAsync(NotificationContext context, InvoiceReady notification, CancellationToken cancellationToken)
        {
            counter.Sends++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_channel_class_is_resolved_from_DI_with_its_dependencies()
    {
        var counter = new Counter();
        await using var provider = Build(s =>
        {
            s.AddSingleton(counter);
            s.AddNotificationChannel<InvoiceReady, ScopedMailChannel>();
            s.AddNotificationChannel<InvoiceReady, ScopedMailChannel>(); // registering the same class twice does not duplicate the channel
        });
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<INotificationSender>().SendAsync(new User("a@b.c"), new InvoiceReady(1), ["mail"]);

        counter.Sends.Should().Be(1);
    }

    [Fact]
    public async Task A_channel_added_to_the_collection_later_is_a_new_channel_for_the_next_provider()
    {
        // There is no manager to Extend: a channel is a DI registration, so "runtime Extend" means registering before the provider is built.
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddNaravelNotifications();
        Record(services, calls, "mail");
        await using (var first = services.BuildServiceProvider())
        {
            var act = () => first.GetRequiredService<INotificationSender>().SendAsync(new User("a@b.c"), new InvoiceReady(1), ["push"]);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        Record(services, calls, "push");
        await using var second = services.BuildServiceProvider();
        await second.GetRequiredService<INotificationSender>().SendAsync(new User("a@b.c"), new InvoiceReady(1), ["push"]);

        calls.Should().Equal("push");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Lines);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue(formatter(state, exception));
        }
    }

    [Fact]
    public async Task The_log_channel_writes_type_names_and_never_contents()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = Build(s =>
        {
            s.AddLogging(builder => builder.AddProvider(logs));
            s.AddLogNotificationChannel<InvoiceReady>();
        });

        await provider.GetRequiredService<INotificationSender>().SendAsync(new User("secret@example.com"), new InvoiceReady(424242), ["log"]);

        var line = logs.Lines.Should().ContainSingle().Which;
        line.Should().Contain("InvoiceReady").And.Contain("log").And.Contain("User");
        line.Should().NotContain("secret@example.com").And.NotContain("424242");
    }

    [Fact]
    public async Task The_log_channel_works_without_any_logging_registration()
    {
        await using var provider = Build(s => s.AddLogNotificationChannel<InvoiceReady>("audit"));
        var act = () => provider.GetRequiredService<INotificationSender>().SendAsync(new User("a@b.c"), new InvoiceReady(1), ["audit"]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Telemetry_counts_each_channel_outcome_with_bounded_tags()
    {
        var seen = new ConcurrentBag<(string Channel, string Outcome)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Naravel.Notifications") l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name != "naravel.notifications.channel.deliveries") return;
            string? channel = null;
            string? outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "channel") channel = tag.Value as string;
                if (tag.Key == "outcome") outcome = tag.Value as string;
            }

            seen.Add((channel ?? "?", outcome ?? "?"));
        });
        listener.Start();

        var good = "tele-good-" + Guid.NewGuid().ToString("N")[..6];
        var bad = "tele-bad-" + Guid.NewGuid().ToString("N")[..6];
        await using var provider = Build(s =>
        {
            s.AddNotificationChannel<InvoiceReady>(good, (_, _, _) => Task.CompletedTask);
            s.AddNotificationChannel<InvoiceReady>(bad, (_, _, _) => throw new InvalidOperationException("x"));
        });
        var act = () => provider.GetRequiredService<INotificationSender>().SendAsync(new User("a@b.c"), new InvoiceReady(1), [good, bad]);
        await act.Should().ThrowAsync<NotificationDeliveryException>();

        seen.Should().Contain((good, "sent"));
        seen.Should().Contain((bad, "failed"));
    }
}

public sealed class NotificationFakeTests
{
    [Fact]
    public async Task The_fake_records_recipient_notification_and_requested_channels()
    {
        var fake = new NotificationFake();
        var user = new User("ada@example.com");

        await fake.SendAsync(user, new InvoiceReady(1), ["mail", "log"]);
        await fake.SendAsync(user, new InvoiceReady(2), ["mail"]);

        fake.Count.Should().Be(2);
        var first = fake.AssertSent<InvoiceReady>();
        first.Recipient.Should().BeSameAs(user);
        first.Notification.InvoiceId.Should().Be(1);
        first.Channels.Should().Equal("mail", "log");
        fake.AssertSent<InvoiceReady>(2).Select(s => s.Notification.InvoiceId).Should().Equal(1, 2);
    }

    [Fact]
    public async Task The_fake_assertions_fail_with_a_clear_message()
    {
        var fake = new NotificationFake();
        fake.AssertNothingSent();
        fake.AssertNotSent<InvoiceReady>();
        ((Action)(() => fake.AssertSent<InvoiceReady>())).Should().Throw<InvalidOperationException>().WithMessage("*InvoiceReady*");

        await fake.SendAsync(new User("a@b.c"), new InvoiceReady(1), ["mail"]);

        ((Action)(() => fake.AssertNothingSent())).Should().Throw<InvalidOperationException>();
        ((Action)(() => fake.AssertNotSent<InvoiceReady>())).Should().Throw<InvalidOperationException>();
        ((Action)(() => fake.AssertSent<InvoiceReady>(2))).Should().Throw<InvalidOperationException>().WithMessage("*Expected 2*found 1*");
        fake.AssertNotSent<User>();
    }

    [Fact]
    public async Task The_fake_applies_the_same_argument_checks_as_the_real_sender()
    {
        var fake = new NotificationFake();
        await ((Func<Task>)(() => fake.SendAsync(new User("a@b.c"), new InvoiceReady(1), []))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => fake.SendAsync(new User("a@b.c"), new InvoiceReady(1), ["mail", "Mail"]))).Should().ThrowAsync<ArgumentException>();
        fake.Count.Should().Be(0);
    }

    [Fact]
    public async Task Reset_clears_the_record_and_a_cancelled_token_records_nothing()
    {
        var fake = new NotificationFake();
        await fake.SendAsync(new User("a@b.c"), new InvoiceReady(1), ["mail"]);
        fake.Reset();
        fake.AssertNothingSent();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await ((Func<Task>)(() => fake.SendAsync(new User("a@b.c"), new InvoiceReady(1), ["mail"], cts.Token))).Should().ThrowAsync<OperationCanceledException>();
        fake.Count.Should().Be(0);
    }
}
