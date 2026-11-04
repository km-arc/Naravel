using Microsoft.Extensions.DependencyInjection;
using Naravel.Notifications.Testing;

namespace Naravel.Notifications.Tests;

// Runs the quickstart and the error messages of docs/en/notifications.md and docs/fa/notifications.md.
// If a test here needs a change, the guide needs the same change.
public sealed class DocsExampleTests
{
    private sealed record InvoiceCancelled(int InvoiceId);

    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddNaravelNotifications();
        configure(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task The_quickstart_log_channel_delivers_without_any_logging_registration()
    {
        await using var provider = Build(s => s.AddLogNotificationChannel<InvoiceReady>());
        using var scope = provider.CreateScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationSender>();

        var send = () => notifications.SendAsync(new User("Ada"), new InvoiceReady(7), ["log"]);

        await send.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_argument_channel_and_delivery_errors_carry_the_messages_in_the_guide()
    {
        await using var provider = Build(s =>
        {
            s.AddLogNotificationChannel<InvoiceReady>();
            s.AddNotificationChannel<InvoiceReady>("boom", (_, _, _) => throw new InvalidOperationException("sms gateway is down"));
        });
        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
        var user = new User("Ada");
        var invoice = new InvoiceReady(7);

        await FluentActions.Awaiting(() => sender.SendAsync(user, invoice, []))
            .Should().ThrowAsync<ArgumentException>().WithMessage("At least one channel name is required.*");
        await FluentActions.Awaiting(() => sender.SendAsync(user, invoice, [" "]))
            .Should().ThrowAsync<ArgumentException>().WithMessage("A channel name must not be blank.*");
        await FluentActions.Awaiting(() => sender.SendAsync(user, invoice, ["log", "log"]))
            .Should().ThrowAsync<ArgumentException>().WithMessage("The channel 'log' is listed more than once.*");
        await FluentActions.Awaiting(() => sender.SendAsync(user, invoice, ["sms"]))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No notification channel 'sms' is registered for InvoiceReady. Registered: log, boom.");

        var first = await FluentActions.Awaiting(() => sender.SendAsync(user, invoice, ["boom"]))
            .Should().ThrowAsync<NotificationDeliveryException>();
        first.Which.Message.Should().Be("Notification channel 'boom' failed; no channel had delivered yet.");
        first.Which.Channel.Should().Be("boom");
        first.Which.InnerException.Should().BeOfType<InvalidOperationException>();

        var later = await FluentActions.Awaiting(() => sender.SendAsync(user, invoice, ["log", "boom"]))
            .Should().ThrowAsync<NotificationDeliveryException>();
        later.Which.Message.Should().Be("Notification channel 'boom' failed after delivering to: log.");
        later.Which.DeliveredChannels.Should().Equal("log");
    }

    [Fact]
    public async Task A_name_registered_twice_and_a_wrong_recipient_type_are_reported_with_the_documented_messages()
    {
        await using var provider = Build(s =>
        {
            s.AddNotificationChannel<InvoiceReady>("sms", (_, _, _) => Task.CompletedTask);
            s.AddNotificationChannel<InvoiceReady>("sms", (_, _, _) => Task.CompletedTask);
        });
        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();

        await FluentActions.Awaiting(() => sender.SendAsync(new User("Ada"), new InvoiceReady(7), ["sms"]))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The channel 'sms' is registered more than once for InvoiceReady; channel names must be unique per notification type.");

        FluentActions.Invoking(() => new NotificationContext("text", "sms").GetRecipient<User>())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Channel 'sms' expects a recipient of type User but got String.");
    }

    [Fact]
    public async Task NotificationFake_assertions_fail_with_the_messages_in_the_guide()
    {
        var fake = new NotificationFake();
        FluentActions.Invoking(() => fake.AssertSent<InvoiceReady>()).Should().Throw<InvalidOperationException>()
            .WithMessage("No InvoiceReady notification was sent.");

        await fake.SendAsync(new User("Ada"), new InvoiceReady(7), ["mail"]);

        FluentActions.Invoking(() => fake.AssertSent<InvoiceReady>(2)).Should().Throw<InvalidOperationException>()
            .WithMessage("Expected 2 InvoiceReady notifications to be sent, but found 1.");
        FluentActions.Invoking(() => fake.AssertNotSent<InvoiceReady>()).Should().Throw<InvalidOperationException>()
            .WithMessage("Expected no InvoiceReady notification, but 1 were sent.");
        FluentActions.Invoking(() => fake.AssertNothingSent()).Should().Throw<InvalidOperationException>()
            .WithMessage("Expected no notifications, but 1 were sent.");
        fake.AssertNotSent<InvoiceCancelled>();
    }
}
