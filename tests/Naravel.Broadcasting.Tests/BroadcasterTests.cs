using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Naravel.Broadcasting.Testing;
using Naravel.Events;

namespace Naravel.Broadcasting.Tests;

public class BroadcasterTests
{
    private sealed class RecordingDriver : IBroadcastDriver
    {
        public List<BroadcastMessage> Sent { get; } = new();
        public Exception? Failure { get; set; }

        public Task SendAsync(BroadcastMessage message, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider Build(Action<IServiceCollection>? configure = null, params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values.ToDictionary(v => $"Broadcasting:{v.Key}", v => v.Value));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelEvents();
        services.AddNaravelBroadcasting(configuration);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Options_bind_the_presence_ttl_and_keep_the_defaults_when_it_is_missing()
    {
        await using (var configured = Build(null, ("PresenceEntryTtl", "00:02:00")))
        {
            var options = configured.GetRequiredService<IOptionsMonitor<BroadcastingOptions>>().CurrentValue;
            options.PresenceEntryTtl.Should().Be(TimeSpan.FromMinutes(2));
            options.Default.Should().Be("signalr");
        }

        await using var plain = Build();
        var defaults = plain.GetRequiredService<IOptionsMonitor<BroadcastingOptions>>().CurrentValue;
        defaults.PresenceEntryTtl.Should().Be(TimeSpan.FromSeconds(90));
        defaults.Default.Should().Be("signalr");
    }

    [Fact]
    public async Task Options_follow_a_configuration_change()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Broadcasting:Default"] = "log" });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelEvents();
        services.AddNaravelBroadcasting(configuration);
        await using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<BroadcastingOptions>>();
        monitor.CurrentValue.Default.Should().Be("log");

        configuration["Broadcasting:Default"] = "null";

        monitor.CurrentValue.Default.Should().Be("null");
    }

    [Fact]
    public async Task The_message_carries_the_event_name_the_channels_and_the_payload()
    {
        await using var provider = Build();
        var manager = provider.GetRequiredService<BroadcastManager>();
        var driver = new RecordingDriver();
        manager.Extend("signalr", _ => driver);

        await provider.GetRequiredService<IBroadcaster>().BroadcastAsync(new Mixed("hi"));

        var message = driver.Sent.Should().ContainSingle().Subject;
        message.Name.Should().Be("Mixed");
        message.Channels.Should().Equal("news", "private-orders.7");   // the duplicate is dropped
        message.Payload.Should().BeEquivalentTo(new { text = "hi" });
    }

    [Fact]
    public async Task The_event_name_defaults_to_the_type_name_and_can_be_overridden()
    {
        await using var provider = Build();
        var driver = new RecordingDriver();
        provider.GetRequiredService<BroadcastManager>().Extend("signalr", _ => driver);
        var broadcaster = provider.GetRequiredService<IBroadcaster>();

        await broadcaster.BroadcastAsync(new OrderShipped(7));
        await broadcaster.BroadcastAsync(new Announcement("x"));

        driver.Sent.Select(m => m.Name).Should().Equal("OrderShipped", "Announced");
    }

    [Fact]
    public async Task An_event_without_channels_sends_nothing()
    {
        await using var provider = Build();
        var driver = new RecordingDriver();
        provider.GetRequiredService<BroadcastManager>().Extend("signalr", _ => driver);

        await provider.GetRequiredService<IBroadcaster>().BroadcastAsync(new NoChannels());

        driver.Sent.Should().BeEmpty();
    }

    private sealed record NoChannels : IBroadcastable
    {
        public IEnumerable<string> Channels() => [];
    }

    private sealed record BadChannel : IBroadcastable
    {
        public IEnumerable<string> Channels() => ["orders.7", "has space"];
    }

    [Fact]
    public async Task An_invalid_channel_name_is_refused_without_echoing_the_name()
    {
        await using var provider = Build();

        var act = () => provider.GetRequiredService<IBroadcaster>().BroadcastAsync(new BadChannel());

        var error = (await act.Should().ThrowAsync<ArgumentException>()).Which;
        error.Message.Should().NotContain("has space").And.NotContain("orders.7");
    }

    [Fact]
    public async Task A_transport_failure_reaches_the_caller()
    {
        await using var provider = Build();
        provider.GetRequiredService<BroadcastManager>().Extend("signalr", _ => new RecordingDriver { Failure = new IOException("down") });

        var act = () => provider.GetRequiredService<IBroadcaster>().BroadcastAsync(new Announcement("x"));

        await act.Should().ThrowAsync<IOException>().WithMessage("down");
    }

    [Fact]
    public async Task A_cancelled_token_is_honoured_before_anything_is_sent()
    {
        await using var provider = Build();
        var driver = new RecordingDriver();
        provider.GetRequiredService<BroadcastManager>().Extend("signalr", _ => driver);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => provider.GetRequiredService<IBroadcaster>().BroadcastAsync(new Announcement("x"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        driver.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task The_default_driver_follows_configuration_and_a_named_broadcaster_picks_its_own()
    {
        await using var provider = Build(null, ("Default", "audit"), ("Stores:audit:Driver", "null"), ("Stores:dev:Driver", "log"));
        var manager = provider.GetRequiredService<BroadcastManager>();
        var audit = new RecordingDriver();
        var dev = new RecordingDriver();
        manager.Extend("audit", _ => audit);
        manager.Extend("dev", _ => dev);

        await provider.GetRequiredService<IBroadcaster>().BroadcastAsync(new Announcement("a"));
        await manager.Broadcaster("dev").BroadcastAsync(new Announcement("b"));

        audit.Sent.Should().ContainSingle();
        dev.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task The_built_in_log_and_null_drivers_accept_a_broadcast()
    {
        await using var provider = Build();
        var manager = provider.GetRequiredService<BroadcastManager>();

        await manager.Broadcaster("log").BroadcastAsync(new Announcement("a"));
        await manager.Broadcaster("null").BroadcastAsync(new Announcement("a"));
    }

    [Fact]
    public async Task An_unknown_driver_name_is_reported_with_the_known_names()
    {
        await using var provider = Build();

        var act = () => provider.GetRequiredService<BroadcastManager>().Broadcaster("pusher").BroadcastAsync(new Announcement("a"));

        var error = (await act.Should().ThrowAsync<Naravel.Foundation.DriverNotRegisteredException>()).Which;
        error.Message.Should().Contain("pusher").And.Contain("signalr");
    }

    [Fact]
    public async Task A_listener_registered_for_the_exact_event_type_broadcasts_it_and_other_types_are_not_broadcast()
    {
        var fake = new BroadcastFake();
        await using var provider = Build(s =>
        {
            s.AddSingleton<IBroadcaster>(fake);
            s.AddBroadcastOn<OrderShipped>();
            s.AddBroadcastOn<OrderShipped>();   // registering twice does not duplicate the listener
        });
        await using var scope = provider.CreateAsyncScope();
        var events = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();

        await events.DispatchAsync(new OrderShipped(7));
        await events.DispatchAsync(new Announcement("not opted in"));

        fake.AssertBroadcastCount(1)[0].Name.Should().Be("OrderShipped");
    }

    [Fact]
    public async Task The_fake_records_and_asserts()
    {
        var fake = new BroadcastFake();
        fake.AssertNothingBroadcast();

        await fake.BroadcastAsync(new OrderShipped(7));

        fake.AssertBroadcast(m => m.Channels.Contains("private-orders.7")).Name.Should().Be("OrderShipped");
        var missing = () => fake.AssertBroadcast(m => m.Name == "Nope");
        missing.Should().Throw<InvalidOperationException>();
        var nothing = () => fake.AssertNothingBroadcast();
        nothing.Should().Throw<InvalidOperationException>();
        var count = () => fake.AssertBroadcastCount(2);
        count.Should().Throw<InvalidOperationException>();
        fake.Reset();
        fake.Broadcasts.Should().BeEmpty();
    }
}
