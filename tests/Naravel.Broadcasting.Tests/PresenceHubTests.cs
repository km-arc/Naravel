using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Naravel.Broadcasting.Tests;

public class PresenceHubTests
{
    private static void Rules(BroadcastingOptions o) =>
        o.Channels.AuthorizePresence("presence-room.{id}", (user, _) =>
            new PresenceMember(user.FindFirst("order")!.Value, new { name = "user " + user.FindFirst("order")!.Value }));

    private static async Task<JsonElement> JoinAsync(HubConnection client, string channel = "presence-room.1") =>
        await client.InvokeAsync<JsonElement>("Subscribe", channel);

    [Fact]
    public async Task Joining_returns_the_members_and_the_others_are_told_once_per_user()
    {
        await using var app = await BroadcastApp.StartAsync(Rules);
        await using var ann = app.Connect(order: "7");
        await using var bob = app.Connect(order: "8");
        await using var bob2 = app.Connect(order: "8");
        var inbox = new Inbox();
        inbox.Listen(ann, "MemberJoined", "MemberLeft", "Announced");
        await ann.StartAsync();
        await bob.StartAsync();
        await bob2.StartAsync();

        var first = await JoinAsync(ann);
        first.GetArrayLength().Should().Be(1);
        first[0].GetProperty("userId").GetString().Should().Be("7");

        var second = await JoinAsync(bob);
        second.GetArrayLength().Should().Be(2);
        var joined = await inbox.NextAsync();
        joined.GetProperty("channel").GetString().Should().Be("presence-room.1");
        joined.GetProperty("member").GetProperty("userId").GetString().Should().Be("8");
        joined.GetProperty("member").GetProperty("info").GetProperty("name").GetString().Should().Be("user 8");

        // A second connection of the same user is not a new member: the next message Ann gets is the marker.
        (await JoinAsync(bob2)).GetArrayLength().Should().Be(2);
        await ann.InvokeAsync("Subscribe", "news");
        await app.Services.GetRequiredService<IBroadcaster>().BroadcastAsync(new Announcement("marker"));
        await inbox.NextAsync();
        inbox.Methods.Should().Equal("MemberJoined", "Announced");
    }

    [Fact]
    public async Task The_others_are_told_when_the_last_connection_of_a_user_closes()
    {
        await using var app = await BroadcastApp.StartAsync(Rules);
        await using var ann = app.Connect(order: "7");
        await using var bob = app.Connect(order: "8");
        var inbox = new Inbox();
        inbox.Listen(ann, "MemberJoined", "MemberLeft");
        await ann.StartAsync();
        await bob.StartAsync();
        await JoinAsync(ann);
        await JoinAsync(bob);
        await inbox.NextAsync();   // Bob joined

        await bob.StopAsync();

        var left = await inbox.NextAsync();
        left.GetProperty("member").GetProperty("userId").GetString().Should().Be("8");
        (await app.Services.GetRequiredService<IPresence>().MembersAsync("presence-room.1")).Select(m => m.UserId).Should().Equal("7");
    }

    [Fact]
    public async Task Unsubscribing_leaves_the_member_list()
    {
        await using var app = await BroadcastApp.StartAsync(Rules);
        await using var ann = app.Connect(order: "7");
        await using var bob = app.Connect(order: "8");
        var inbox = new Inbox();
        inbox.Listen(ann, "MemberJoined", "MemberLeft");
        await ann.StartAsync();
        await bob.StartAsync();
        await JoinAsync(ann);
        await JoinAsync(bob);
        await inbox.NextAsync();

        await bob.InvokeAsync("Unsubscribe", "presence-room.1");

        (await inbox.NextAsync()).GetProperty("member").GetProperty("userId").GetString().Should().Be("8");
    }

    [Fact]
    public async Task A_rule_that_returns_null_refuses_the_join_and_adds_no_member()
    {
        await using var app = await BroadcastApp.StartAsync(o =>
            o.Channels.AuthorizePresence("presence-vip.{id}", (_, _) => null));
        await using var client = app.Connect(order: "7");
        await client.StartAsync();

        var join = () => client.InvokeAsync<JsonElement>("Subscribe", "presence-vip.1");

        await join.Should().ThrowAsync<HubException>();
        (await app.Services.GetRequiredService<IPresence>().MembersAsync("presence-vip.1")).Should().BeEmpty();
    }

    [Fact]
    public async Task An_anonymous_connection_cannot_join_a_presence_channel()
    {
        await using var app = await BroadcastApp.StartAsync(Rules);
        await using var client = app.Connect();
        await client.StartAsync();

        var join = () => client.InvokeAsync<JsonElement>("Subscribe", "presence-room.1");

        await join.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task Joining_a_private_or_public_channel_returns_no_members()
    {
        await using var app = await BroadcastApp.StartAsync(o =>
        {
            Rules(o);
            o.Channels.Authorize("private-orders.{id}", (_, _) => true);
        });
        await using var client = app.Connect(order: "7");
        await client.StartAsync();

        (await client.InvokeAsync<JsonElement>("Subscribe", "news")).GetArrayLength().Should().Be(0);
        (await client.InvokeAsync<JsonElement>("Subscribe", "private-orders.7")).GetArrayLength().Should().Be(0);
    }
}
