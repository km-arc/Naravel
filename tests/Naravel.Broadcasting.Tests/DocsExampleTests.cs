using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naravel.Broadcasting.Testing;
using Naravel.Foundation;
using LogEntry = (Microsoft.Extensions.Logging.LogLevel Level, string Category, string Message, string? Error);

namespace Naravel.Broadcasting.Tests;

// Runs the "Quickstart" and "Errors" sections of docs/en/broadcasting.md.
public class DocsQuickstartTests
{
    // The types of the quickstart server and its client; the two projects share only the JSON shape.
    public sealed record Announcement(string Text) : IBroadcastable
    {
        public IEnumerable<string> Channels() => ["news"];
    }

    private sealed record ClientAnnouncement(string Text);

    [Fact]
    public async Task The_quickstart_delivers_a_posted_announcement_to_a_subscribed_client_without_events_or_sign_in()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();   // the guide runs on Kestrel
        builder.Services.AddNaravelBroadcasting(builder.Configuration);
        var app = builder.Build();
        app.MapNaravelBroadcasting("/hubs/broadcast");
        app.MapPost("/announce", async (string text, IBroadcaster broadcaster, CancellationToken ct) =>
        {
            await broadcaster.BroadcastAsync(new Announcement(text), ct);
            return Results.Accepted();
        });
        await app.StartAsync();
        try
        {
            var server = (TestServer)app.Services.GetRequiredService<IServer>();
            await using var client = new HubConnectionBuilder()
                .WithUrl(server.BaseAddress + "hubs/broadcast", o =>
                {
                    o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                    o.Transports = HttpTransportType.LongPolling;
                })
                .Build();
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.On<ClientAnnouncement>("Announcement", a => received.TrySetResult(a.Text));
            await client.StartAsync();
            await client.InvokeAsync("Subscribe", "news");

            using var http = server.CreateClient();
            var response = await http.PostAsync("/announce?text=hello", null);

            response.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);
            (await received.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("hello");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}

public class DocsErrorsTests
{
    // One event whose contract is broken one member at a time; its name is the one the guide's message shows.
    public sealed record OrderShipped(string[] Names, string? As = "OrderShipped", bool NullPayload = false) : IBroadcastable
    {
        public IEnumerable<string> Channels() => Names;

        public string BroadcastAs() => As!;

        public object BroadcastWith() => NullPayload ? null! : this;
    }

    private sealed class Recording : IBroadcastDriver
    {
        public List<BroadcastMessage> Sent { get; } = [];

        public Task SendAsync(BroadcastMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static void Check(Exception error, string? param, string? message)
    {
        if (param is not null) ((ArgumentException)error).ParamName.Should().Be(param);
        if (message is not null) error.Message.Should().StartWith(message);
    }

    private static void Refused<T>(Action act, string? param = null, string? message = null) where T : Exception =>
        Check(act.Should().ThrowExactly<T>().Which, param, message);

    private static async Task RefusedAsync<T>(Func<Task> act, string? param = null, string? message = null) where T : Exception =>
        Check((await act.Should().ThrowExactlyAsync<T>()).Which, param, message);

    private static ServiceProvider Build(Action<IServiceCollection>? configure, params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values.ToDictionary(v => $"Broadcasting:{v.Key}", v => v.Value));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelBroadcasting(configuration);   // no Naravel.Events registration is needed
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static LogEntry[] Own(List<LogEntry> log)
    {
        lock (log) return log.Where(e => e.Category.StartsWith("Naravel.Broadcasting", StringComparison.Ordinal)).ToArray();
    }

    [Fact]
    public async Task Registration_and_mapping_check_their_arguments()
    {
        var config = new ConfigurationManager();
        var services = new ServiceCollection();
        Refused<ArgumentNullException>(() => ((IServiceCollection)null!).AddNaravelBroadcasting(config), "services");
        Refused<ArgumentNullException>(() => services.AddNaravelBroadcasting(null!), "configuration");
        Refused<ArgumentNullException>(() => services.AddNaravelBroadcasting(config, null, null!), "sectionName");
        Refused<ArgumentException>(() => services.AddNaravelBroadcasting(config, null, " "), "sectionName");

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddNaravelBroadcasting(builder.Configuration);
        await using var app = builder.Build();
        Refused<ArgumentNullException>(() => ((IEndpointRouteBuilder)null!).MapNaravelBroadcasting("/x"), "endpoints");
        Refused<ArgumentNullException>(() => app.MapNaravelBroadcasting(null!), "path");
        Refused<ArgumentException>(() => app.MapNaravelBroadcasting(" "), "path");
    }

    [Fact]
    public void Rule_patterns_and_member_values_are_checked_when_they_are_registered()
    {
        var rules = new BroadcastingOptions().Channels;
        Func<ClaimsPrincipal, string, bool> yes = (_, _) => true;
        Func<ClaimsPrincipal, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<bool>> yesAsync = (_, _, _) => ValueTask.FromResult(true);
        const string Prefix = "A rule pattern for this method must start with '";
        const string Placeholder = "A placeholder must be a unique identifier such as {id}.";
        const string OnePlaceholder = "This overload needs exactly one placeholder; use the overload that receives a dictionary for any other number.";
        const string TooLarge = "The member information is larger than 2048 characters.";

        Refused<ArgumentNullException>(() => rules.Authorize("private-a.{id}", (Func<ClaimsPrincipal, string, bool>)null!), "rule");
        Refused<ArgumentNullException>(() => rules.Authorize(null!, yes), "pattern");
        Refused<ArgumentException>(() => rules.Authorize(" ", yes), "pattern");
        Refused<ArgumentException>(() => rules.Authorize("orders.{id}", yes), "pattern", Prefix + "private-'; public channels need no rule.");
        Refused<ArgumentException>(() => rules.AuthorizePresence("private-room.{id}", (_, _) => null), "pattern", Prefix + "presence-'; public channels need no rule.");
        Refused<ArgumentException>(() => rules.Authorize("private-a.{id", yesAsync), "pattern", "Unmatched '{' in the channel pattern.");
        Refused<ArgumentException>(() => rules.Authorize("private-a.id}", yesAsync), "pattern", "Unmatched '}' in the channel pattern.");
        foreach (var bad in new[] { "private-a.{}", "private-a.{1id}", "private-a.{a-b}", "private-a.{id}.{id}" })
        {
            Refused<ArgumentException>(() => rules.Authorize(bad, yesAsync), "pattern", Placeholder);
        }

        foreach (var bad in new[] { "private-lobby", "private-a.{x}.{y}" })
        {
            Refused<ArgumentException>(() => rules.Authorize(bad, yes), "pattern", OnePlaceholder);
        }

        Refused<ArgumentNullException>(() => new PresenceMember(null!), "userId");
        Refused<ArgumentException>(() => new PresenceMember(" "), "userId");
        Refused<ArgumentException>(() => new PresenceMember("1", new string('x', 3000)), "info", TooLarge);
        Refused<ArgumentException>(() => PresenceMember.FromJson(" ", default), "userId");
        Refused<ArgumentException>(() => PresenceMember.FromJson("1", JsonSerializer.SerializeToElement(new string('x', 3000))), "info", TooLarge);
    }

    [Fact]
    public async Task A_presence_lifetime_that_is_not_a_time_span_fails_when_the_options_are_read()
    {
        await using var provider = Build(null, ("PresenceEntryTtl", "two minutes"));

        var read = () => provider.GetRequiredService<IOptionsMonitor<BroadcastingOptions>>().CurrentValue;

        read.Should().Throw<FormatException>();
    }

    [Fact]
    public async Task The_real_broadcaster_and_the_fake_refuse_a_broken_event_with_the_documented_exceptions()
    {
        var driver = new Recording();
        await using var provider = Build(null);
        provider.GetRequiredService<BroadcastManager>().Extend("signalr", _ => driver);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        foreach (var broadcaster in new[] { provider.GetRequiredService<IBroadcaster>(), new BroadcastFake() })
        {
            await RefusedAsync<ArgumentNullException>(() => broadcaster.BroadcastAsync(null!), "evt");
            Func<Task> cancel = () => broadcaster.BroadcastAsync(new OrderShipped(["news"]), cancelled.Token);
            await cancel.Should().ThrowAsync<OperationCanceledException>();
            await RefusedAsync<ArgumentException>(() => broadcaster.BroadcastAsync(new OrderShipped(["orders.7", "has space"])), "evt", "Channel number 1 of OrderShipped is not a valid channel name.");
            await RefusedAsync<ArgumentNullException>(() => broadcaster.BroadcastAsync(new OrderShipped(["news"], As: null)), "name");
            await RefusedAsync<ArgumentException>(() => broadcaster.BroadcastAsync(new OrderShipped(["news"], As: " ")), "name");
            await RefusedAsync<ArgumentNullException>(() => broadcaster.BroadcastAsync(new OrderShipped(["news"], NullPayload: true)), "payload");
        }

        driver.Sent.Should().BeEmpty();   // not even the valid first channel was sent
    }

    [Fact]
    public async Task An_unknown_driver_is_reported_with_the_known_names_and_only_for_an_event_that_has_channels()
    {
        const string Known = "Driver '{0}' is not registered for IBroadcastDriver. Available drivers: {1}.";
        var log = new List<LogEntry>();
        await using var provider = Build(s => s.AddSingleton<ILoggerProvider>(new Capture(log)), ("Default", "pusher"));
        var broadcaster = provider.GetRequiredService<IBroadcaster>();
        var manager = provider.GetRequiredService<BroadcastManager>();

        await broadcaster.BroadcastAsync(new OrderShipped([]));   // no channels: the driver is never looked up
        await RefusedAsync<DriverNotRegisteredException>(() => broadcaster.BroadcastAsync(new OrderShipped(["news"])), null, string.Format(Known, "pusher", "log, null, signalr"));
        manager.Extend("audit", _ => new Recording());
        await RefusedAsync<DriverNotRegisteredException>(() => broadcaster.BroadcastAsync(new OrderShipped(["news"])), null, string.Format(Known, "pusher", "audit, log, null, signalr"));
        await RefusedAsync<ArgumentException>(() => manager.Broadcaster(" ").BroadcastAsync(new OrderShipped(["news"])), "name", "Driver name must not be blank.");

        await using var typo = Build(null, ("Default", "audit"), ("Stores:audit:Driver", "signall"));
        await RefusedAsync<DriverNotRegisteredException>(() => typo.GetRequiredService<IBroadcaster>().BroadcastAsync(new OrderShipped(["news"])), null, string.Format(Known, "audit", "log, null, signalr"));
        await using var quiet = Build(null, ("Default", "quiet"), ("Stores:quiet:Driver", "NULL"));
        await quiet.GetRequiredService<IBroadcaster>().BroadcastAsync(new OrderShipped(["news"]));   // the value of Driver ignores case
        await using var empty = Build(null, ("Default", ""));
        await RefusedAsync<InvalidOperationException>(() => empty.GetRequiredService<IBroadcaster>().BroadcastAsync(new OrderShipped(["news"])), null, "No default driver is configured for IBroadcastDriver. Set the 'Default' option or pass a driver name explicitly.");

        await manager.Broadcaster("log").BroadcastAsync(new OrderShipped(["news", "other"]));
        LogEntry line = (LogLevel.Information, "Naravel.Broadcasting", "Broadcast OrderShipped to 2 channel(s).", null);
        Own(log).Should().ContainSingle().Which.Should().Be(line);
    }

    [Fact]
    public async Task Every_refusal_is_the_same_hub_exception_and_a_rule_that_throws_is_logged_without_the_channel()
    {
        var log = new List<LogEntry>();
        await using var app = await BroadcastApp.StartAsync(
            o =>
            {
                o.Channels.Authorize("private-orders.{id}", (user, id) => user.FindFirst("order")?.Value == id);
                o.Channels.Authorize("private-boom.{id}", (_, _) => throw new InvalidOperationException("rule bug"));
                o.Channels.AuthorizePresence("presence-vip.{id}", (_, _) => null);
            },
            services => services.AddSingleton<ILoggerProvider>(new Capture(log)));
        await using var anonymous = app.Connect();
        await using var signedIn = app.Connect(order: "7");
        await anonymous.StartAsync();
        await signedIn.StartAsync();

        (HubConnection Client, string Channel)[] refused =
        [
            (anonymous, "private-orders.7"), (signedIn, "has space"), (signedIn, "private-invoices.7"),
            (signedIn, "private-orders.8"), (signedIn, "private-boom.1"), (signedIn, "presence-vip.1"),
        ];
        foreach (var (client, channel) in refused)
        {
            var join = () => client.InvokeAsync("Subscribe", channel);
            (await join.Should().ThrowAsync<HubException>()).Which.Message
                .Should().Be("An unexpected error occurred invoking 'Subscribe' on the server. HubException: Subscription denied.");
        }

        var leave = () => signedIn.InvokeAsync("Unsubscribe", "has space");
        (await leave.Should().ThrowAsync<HubException>()).Which.Message
            .Should().Be("An unexpected error occurred invoking 'Unsubscribe' on the server. HubException: Subscription denied.");
        await signedIn.InvokeAsync("Unsubscribe", "news");   // never joined: not an error

        LogEntry threw = (LogLevel.Warning, "Naravel.Broadcasting.BroadcastHub", "A channel authorization rule threw; the subscription was denied.", "rule bug");
        Own(log).Should().ContainSingle().Which.Should().Be(threw);
    }

    [Fact]
    public async Task A_presence_lifetime_under_three_seconds_refuses_the_join_and_logs_why()
    {
        var log = new List<LogEntry>();
        await using var app = await BroadcastApp.StartAsync(
            o =>
            {
                o.PresenceEntryTtl = TimeSpan.FromSeconds(2);
                o.Channels.AuthorizePresence("presence-room.{id}", (_, _) => new PresenceMember("7"));
            },
            services => services.AddSingleton<ILoggerProvider>(new Capture(log)));
        await using var client = app.Connect(order: "7");
        await client.StartAsync();

        var join = () => client.InvokeAsync<JsonElement>("Subscribe", "presence-room.1");

        (await join.Should().ThrowAsync<HubException>()).Which.Message.Should().EndWith("HubException: Presence is unavailable.");
        LogEntry failed = (LogLevel.Error, "Naravel.Broadcasting.BroadcastHub", "The presence list could not be updated; the subscription was undone.", "BroadcastingOptions.PresenceEntryTtl must be at least 3 seconds.");
        Own(log).Should().ContainSingle().Which.Should().Be(failed);
    }

    [Fact]
    public async Task The_per_process_warning_is_logged_once_and_the_list_is_read_through_IPresence()
    {
        var log = new List<LogEntry>();
        await using var app = await BroadcastApp.StartAsync(
            o => o.Channels.AuthorizePresence("presence-room.{id}", (user, _) => new PresenceMember(user.FindFirst("order")!.Value)),
            services => services.AddSingleton<ILoggerProvider>(new Capture(log)));
        await using var first = app.Connect(order: "7");
        await using var second = app.Connect(order: "8");
        await first.StartAsync();
        await second.StartAsync();
        await first.InvokeAsync<JsonElement>("Subscribe", "presence-room.1");
        await second.InvokeAsync<JsonElement>("Subscribe", "presence-room.1");

        var presence = app.Services.GetRequiredService<IPresence>();

        (await presence.MembersAsync("presence-room.1")).Select(m => m.UserId).Should().Equal("7", "8");
        (await presence.MembersAsync("news")).Should().BeEmpty();   // a valid name that is not a presence channel has no members
        Refused<ArgumentException>(() => presence.MembersAsync("has space"), "channel", "The channel name is not valid.");
        LogEntry warning = (LogLevel.Warning, "Naravel.Broadcasting", "Presence member lists are kept per process because the cache lock is not shared; with several instances each one sees only its own members. Register a Redis or Memcached cache.", null);
        Own(log).Should().ContainSingle().Which.Should().Be(warning);
    }

    [Fact]
    public async Task The_fake_fails_its_assertions_with_the_documented_messages()
    {
        var fake = new BroadcastFake();
        Refused<InvalidOperationException>(() => fake.AssertBroadcast(), null, "No broadcast was recorded.");

        await fake.BroadcastAsync(new OrderShipped(["news"]));
        Refused<InvalidOperationException>(() => fake.AssertBroadcastCount(2), null, "Expected 2 broadcast(s), but found 1.");
        Refused<InvalidOperationException>(() => fake.AssertNothingBroadcast(), null, "Expected no broadcast, but 1 were recorded.");

        await fake.BroadcastAsync(new OrderShipped(["news"]));
        Refused<InvalidOperationException>(() => fake.AssertBroadcast(m => m.Name == "Nope"), null, "No matching broadcast was recorded (2 in total).");
    }

    private sealed class Capture(List<LogEntry> log) : ILoggerProvider
    {
        public ILogger CreateLogger(string category) => new Sink(log, category);

        public void Dispose()
        {
        }

        private sealed class Sink(List<LogEntry> log, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel level) => true;

            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
            {
                lock (log) log.Add((level, category, format(state, error), error?.Message));
            }
        }
    }
}
