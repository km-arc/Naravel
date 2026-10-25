using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Events;

namespace Naravel.Broadcasting.Tests;

public class HubTests
{
    private static void Rules(BroadcastingOptions o) =>
        o.Channels.Authorize("private-orders.{id}", (user, id) => user.FindFirst("order")?.Value == id);

    [Fact]
    public async Task A_public_channel_needs_no_rule_and_no_sign_in_and_receives_the_event()
    {
        await using var app = await BroadcastApp.StartAsync(Rules);
        await using var client = app.Connect();
        var inbox = new Inbox();
        inbox.Listen(client, "Announced");
        await client.StartAsync();
        await client.InvokeAsync("Subscribe", "news");

        await app.Services.GetRequiredService<IBroadcaster>().BroadcastAsync(new Announcement("hello"));

        (await inbox.NextAsync()).GetProperty("text").GetString().Should().Be("hello");
    }

    [Fact]
    public async Task An_authorized_private_join_receives_the_event()
    {
        await using var app = await BroadcastApp.StartAsync(Rules);
        await using var client = app.Connect(order: "7");
        var inbox = new Inbox();
        inbox.Listen(client, "OrderShipped");
        await client.StartAsync();
        await client.InvokeAsync("Subscribe", "private-orders.7");

        await app.Services.GetRequiredService<IBroadcaster>().BroadcastAsync(new OrderShipped(7));

        (await inbox.NextAsync()).GetProperty("orderId").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task An_event_dispatched_through_Naravel_Events_reaches_the_client()
    {
        await using var app = await BroadcastApp.StartAsync(Rules, services => services.AddBroadcastOn<OrderShipped>());
        await using var client = app.Connect(order: "7");
        var inbox = new Inbox();
        inbox.Listen(client, "OrderShipped");
        await client.StartAsync();
        await client.InvokeAsync("Subscribe", "private-orders.7");

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEventDispatcher>().DispatchAsync(new OrderShipped(7));

        (await inbox.NextAsync()).GetProperty("orderId").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task A_rule_that_returns_false_refuses_the_join_and_nothing_is_received()
    {
        await using var app = await BroadcastApp.StartAsync(Rules);
        await using var client = app.Connect(order: "7");
        var inbox = new Inbox();
        inbox.Listen(client, "OrderShipped", "Announced");
        await client.StartAsync();
        await client.InvokeAsync("Subscribe", "news");

        var join = () => client.InvokeAsync("Subscribe", "private-orders.8");
        await join.Should().ThrowAsync<HubException>();

        var broadcaster = app.Services.GetRequiredService<IBroadcaster>();
        await broadcaster.BroadcastAsync(new OrderShipped(8));
        await broadcaster.BroadcastAsync(new Announcement("marker"));
        await inbox.NextAsync();   // the marker is the second send; the first would have arrived before it
        inbox.Methods.Should().Equal("Announced");
    }

    [Fact]
    public async Task An_anonymous_connection_cannot_join_a_private_channel()
    {
        await using var app = await BroadcastApp.StartAsync(Rules);
        await using var client = app.Connect();
        await client.StartAsync();

        var join = () => client.InvokeAsync("Subscribe", "private-orders.7");

        await join.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task A_private_channel_with_no_registered_rule_is_denied_even_when_signed_in()
    {
        await using var app = await BroadcastApp.StartAsync(Rules);
        await using var client = app.Connect(order: "7");
        await client.StartAsync();

        var private1 = () => client.InvokeAsync("Subscribe", "private-invoices.7");
        var presence = () => client.InvokeAsync("Subscribe", "presence-room.1");

        await private1.Should().ThrowAsync<HubException>();
        await presence.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task A_rule_that_throws_denies_the_join()
    {
        await using var app = await BroadcastApp.StartAsync(o =>
            o.Channels.Authorize("private-boom.{id}", (_, _) => throw new InvalidOperationException("rule bug")));
        await using var client = app.Connect(order: "7");
        await client.StartAsync();

        var join = () => client.InvokeAsync("Subscribe", "private-boom.1");

        await join.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task An_invalid_channel_name_is_refused()
    {
        await using var app = await BroadcastApp.StartAsync(Rules);
        await using var client = app.Connect();
        await client.StartAsync();

        var join = () => client.InvokeAsync("Subscribe", "has space");

        await join.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task Unsubscribe_stops_delivery()
    {
        await using var app = await BroadcastApp.StartAsync(Rules);
        await using var client = app.Connect();
        var inbox = new Inbox();
        inbox.Listen(client, "Announced", "Marker");
        await client.StartAsync();
        await client.InvokeAsync("Subscribe", "news");
        await client.InvokeAsync("Unsubscribe", "news");
        await client.InvokeAsync("Subscribe", "other");

        var broadcaster = app.Services.GetRequiredService<IBroadcaster>();
        await broadcaster.BroadcastAsync(new Announcement("gone"));
        await broadcaster.BroadcastAsync(new Marker());
        await inbox.NextAsync();   // the marker is the second send; the first would have arrived before it

        inbox.Methods.Should().Equal("Marker");
    }

    [Fact]
    public async Task Broadcasting_to_a_channel_nobody_joined_is_not_an_error()
    {
        await using var app = await BroadcastApp.StartAsync(Rules);

        await app.Services.GetRequiredService<IBroadcaster>().BroadcastAsync(new OrderShipped(1));
    }
}
