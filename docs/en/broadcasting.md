# Broadcasting

`Naravel.Broadcasting` is a thin layer over SignalR. SignalR already delivers messages (`IHubContext<T>`, groups, backplanes); this package adds what it does not have: an event class that says which channels it goes to, a registered rule for who may join `private-orders.{id}`, one place to switch the transport, and a fake for tests. It does not replace SignalR.

Decision and reasons: [PDR-018](../pdr/en/PDR-018-broadcasting.md).

> Build and tests are NOT RUN locally (no .NET SDK in the authoring environment); CI is the check.

## Quickstart

A server that broadcasts an announcement to a public channel, and a console client that listens. It needs the .NET 10 SDK and nothing else: no cache, no sign-in and no `Naravel.Events` registration.

```sh
dotnet new web -o BroadcastDemo && cd BroadcastDemo
dotnet add package Naravel.Broadcasting
```

Replace `Program.cs`:

```csharp
using Naravel.Broadcasting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelBroadcasting(builder.Configuration);
var app = builder.Build();

app.MapNaravelBroadcasting("/hubs/broadcast");                 // the one hub clients connect to
app.MapPost("/announce", async (string text, IBroadcaster broadcaster, CancellationToken ct) =>
{
    await broadcaster.BroadcastAsync(new Announcement(text), ct);
    return Results.Accepted();
});

app.Run();

public sealed record Announcement(string Text) : IBroadcastable
{
    public IEnumerable<string> Channels() => ["news"];          // a public channel: anyone may subscribe
}
```

`dotnet run` prints the address, such as `http://localhost:5000`. In a second terminal create the client and use that address in `Program.cs`:

```sh
dotnet new console -o BroadcastClient && cd BroadcastClient
dotnet add package Microsoft.AspNetCore.SignalR.Client
```

```csharp
using Microsoft.AspNetCore.SignalR.Client;

var connection = new HubConnectionBuilder().WithUrl("http://localhost:5000/hubs/broadcast").Build();
connection.On<Announcement>("Announcement", a => Console.WriteLine($"announced: {a.Text}"));
await connection.StartAsync();
await connection.InvokeAsync("Subscribe", "news");
Console.ReadLine();

public sealed record Announcement(string Text);
```

With the client running, `curl -X POST "http://localhost:5000/announce?text=hello"` makes it print `announced: hello`. A client that is not subscribed at that moment gets nothing: delivery is not stored.

The method name a client listens under is `BroadcastAs()` (the type name by default, here `Announcement`) and the payload is `BroadcastWith()` (the event itself by default) serialized as JSON in camelCase, here `{ "text": "hello" }`. A browser client (`@microsoft/signalr`) does the same with `connection.invoke("Subscribe", "news")` and `connection.on("Announcement", payload => ...)`. Private channels need a signed-in user and a rule: see [Channels and authorization](#channels-and-authorization) and the complete example below.

## Channels and authorization

| Name | Who may subscribe |
|---|---|
| `news`, `orders.7` (public) | Any connection |
| `private-...` | An authenticated user, and a rule from `Channels.Authorize` that matches and returns `true` |
| `presence-...` | An authenticated user, and a rule from `Channels.AuthorizePresence` that returns the member for this user (`null` refuses). The channel has a member list, see Presence below. |

- A protected channel with **no matching rule is refused**, and so is one whose rule throws. This is the safe default: in plain SignalR a hub method that adds a client to any group is open to every client.
- The package does not set up authentication. The hub accepts every connection; a `private-` or `presence-` channel needs a connection whose user your own ASP.NET Core scheme has authenticated. With no scheme every client is anonymous, and every protected `Subscribe` is refused with the fixed message below.
- A pattern matches the whole channel name. A placeholder such as `{id}` matches one or more characters except `.` (as in Laravel). The first registered rule that matches decides.
- The single-placeholder form is `Authorize("private-orders.{id}", (user, id) => ...)`. `Authorize` is for `private-` patterns and `AuthorizePresence` for `presence-` patterns; the wrong prefix is refused when the rule is registered. For several placeholders, or a rule that awaits something (a database call), use `Authorize("private-teams.{team}.users.{user}", async (user, values, ct) => ...)`; `values` holds the placeholders by name and `ct` is cancelled when the connection closes. A rule is a delegate: capture what it needs when you register it.
- Names use letters, digits and `_ . : -`, at most 128 characters. Anything else is refused by the hub and by `BroadcastAsync`.
- A refusal is the same fixed `HubException("Subscription denied.")` for every cause, so a client cannot tell "no such rule" from "not allowed". SignalR wraps the message for the client: on the wire the error text is `An unexpected error occurred invoking 'Subscribe' on the server. HubException: Subscription denied.`, so a client should test for the substring `Subscription denied.` rather than compare the whole string.

## Presence

A `presence-` channel tells its members who else is there, like Laravel's `Echo.join()`.

```csharp
builder.Services.AddNaravelBroadcasting(builder.Configuration, o =>
    o.Channels.AuthorizePresence("presence-room.{id}", (user, id) =>
        new PresenceMember(user.FindFirst("sub")!.Value, new { name = user.Identity!.Name })));
```

```js
const members = await connection.invoke("Subscribe", "presence-room.1");   // everyone there now, including you: [{ userId, info }]
connection.on("MemberJoined", e => ...);   // { channel, member } when a user first appears
connection.on("MemberLeft",   e => ...);   // { channel, member } when a user's last connection leaves
```

- **Members are users, not connections.** Two tabs of one user are one member; `MemberJoined` fires for the first connection and `MemberLeft` for the last. The user id is the one the rule gives in `PresenceMember`. `info` is anything the rule adds (a name, an avatar), at most 2048 characters of JSON; it is sent to every member, so put nothing private in it.
- **The list lives in the shared cache** (`ICacheStore` and `ICacheLock` from `Naravel.Cache`), so every instance sees the same members. Register a Redis or Memcached cache when you run several instances. With no cache, or with the memory cache, each process keeps its own list and the module logs one warning.
- **Correctness across instances:** each change is a read-modify-write of one cache entry per channel under a per-channel lock, so two instances cannot overwrite each other. A change waits up to about two seconds for the lock and then fails with the join refused (`Presence is unavailable.`).
- **Crashed instances:** each instance refreshes the entries of its own connections every third of `PresenceEntryTtl` (90 seconds by default, at least 3). The entries of a connection whose instance died disappear from the list after at most that long. **No `MemberLeft` is sent for such an expiry**, because nothing is left to send it: the list is right, a client that wants certainty can call `Subscribe` again.
- Server code reads the list through `IPresence.MembersAsync(channel)`.

## Sending

`IBroadcaster.BroadcastAsync(event, ct)` sends the event to each channel it lists (duplicates are dropped; an event with no channels sends nothing).

- **Delivery is inline and at most once.** It happens before `BroadcastAsync` returns. There is no replay and no durable queue: a client that is offline misses the message. Broadcasting through `Naravel.Queue` is not part of this package.
- A transport failure is **thrown to the caller**; a channel nobody listens to is not an error.
- Several instances need a SignalR backplane (Redis or Azure SignalR Service). That is a native SignalR option you configure yourself.

### From `Naravel.Events`

`Naravel.Events` dispatches by **exact event type**, with no base-type or interface dispatch, so "broadcast everything that is `IBroadcastable`" cannot be one listener. Opt each type in: `services.AddBroadcastOn<OrderShipped>()`. Registering the same type twice is harmless.

## Drivers

`Broadcasting:Default` (default `signalr`) and named drivers under `Broadcasting:Stores:<name>:Driver`, as for Mail and Cache. Names can be added at runtime with `BroadcastManager.Extend(name, factory)`; `manager.Broadcaster("log")` gives a broadcaster bound to one driver.

| Driver | What it does |
|---|---|
| `signalr` | Sends to the SignalR group named like the channel |
| `log` | Logs the event name and the number of channels. Channel names and payloads are not logged: they often embed ids. |
| `null` | Discards |

## Errors

The messages below are the ones in the source. .NET adds ` (Parameter 'pattern')` to the message of an argument exception, which the tables leave out. A mistake shows up at one of four moments: when you register rules and options (the call throws), when you send (`BroadcastAsync` throws), when a client calls the hub (the client gets one fixed `HubException`), and later in the background, where it is only logged. The hub never tells a client why it refused.

**Registering and mapping.**

| When | Exception and message |
|---|---|
| `AddNaravelBroadcasting` with a null `services`, `configuration` or `sectionName`, or an empty or blank `sectionName`; `MapNaravelBroadcasting` with a null builder or a null, empty or blank `path` | `ArgumentNullException` for null, `ArgumentException` for empty or blank; the framework's message |
| `Authorize` or `AuthorizePresence` with a null rule | `ArgumentNullException`, parameter `rule` |
| A null, empty or blank pattern | `ArgumentNullException` for null, `ArgumentException` for the others; the framework's message |
| A pattern that does not start with the prefix of the method | `ArgumentException`, parameter `pattern`: `A rule pattern for this method must start with 'private-'; public channels need no rule.` (`'presence-'` for `AuthorizePresence`) |
| A `{` without a `}`, or a `}` without a `{` | `ArgumentException`, parameter `pattern`: `Unmatched '{' in the channel pattern.` or `Unmatched '}' in the channel pattern.` |
| A placeholder that is empty, starts with a digit, holds a character other than letters, digits and `_`, or appears twice | `ArgumentException`, parameter `pattern`: `A placeholder must be a unique identifier such as {id}.` |
| The overload with `(user, id)` and a pattern with no placeholder or with two or more | `ArgumentException`, parameter `pattern`: `This overload needs exactly one placeholder; use the overload that receives a dictionary for any other number.` |
| `new PresenceMember` or `PresenceMember.FromJson` with a null, empty or blank user id | `ArgumentNullException` for null, `ArgumentException` for the others; the framework's message |
| Member information whose JSON is longer than 2048 characters | `ArgumentException`, parameter `info`: `The member information is larger than 2048 characters.` |
| `Broadcasting:PresenceEntryTtl` that is not a time span (two minutes is `00:02:00`) | `FormatException` from `TimeSpan.Parse`, when the options are first read (for example by `IOptionsMonitor<BroadcastingOptions>.CurrentValue`). The module reads them in its presence heartbeat, a hosted service; no test starts a host with a bad value, so what the host does then is not documented here |

**Sending** (`IBroadcaster.BroadcastAsync`; `BroadcastFake` throws the same first five).

| When | Exception and message |
|---|---|
| A null event | `ArgumentNullException`, parameter `evt` |
| A token that is already cancelled | `OperationCanceledException`, before anything is built or sent |
| A name in `Channels()` that is empty, longer than 128 characters or has a character outside `A-Z a-z 0-9 _ . : -` | `ArgumentException`, parameter `evt`: `Channel number 1 of OrderShipped is not a valid channel name.` The number counts from 0 and the name is not in the message, because names often embed ids. Nothing is sent, not even to the valid channels |
| `BroadcastAs()` that returns null, or empty or blank text | `ArgumentNullException` for null, `ArgumentException` for the others, parameter `name` |
| `BroadcastWith()` that returns null | `ArgumentNullException`, parameter `payload` |
| `Broadcasting:Default`, or the name given to `Broadcaster(name)`, is not a registered driver. A `Stores` entry whose `Driver` is not `signalr`, `log` or `null` (compared without regard to case) is never registered, so its store name is the one that is unknown | `DriverNotRegisteredException`, an `InvalidOperationException` of `Naravel.Foundation`: `Driver 'pusher' is not registered for IBroadcastDriver. Available drivers: log, null, signalr.` The names of drivers you added with `Extend` are in the list |
| `Broadcasting:Default` set to an empty string | `InvalidOperationException`: `No default driver is configured for IBroadcastDriver. Set the 'Default' option or pass a driver name explicitly.` |
| `Broadcaster(name)` with a blank name | `ArgumentException`, parameter `name`: `Driver name must not be blank.` when the event is sent |
| The transport fails | what the driver throws, unchanged |

The driver is looked up only after the channels are checked and only for an event that has channels. An event with no channels returns at once, so a wrong `Default` stays hidden until the first event that has one.

**A client calls the hub** (`HubException`; the client sees `An unexpected error occurred invoking 'Subscribe' on the server. HubException: ` followed by the text, with the name of the method it called).

| When | Message |
|---|---|
| `Subscribe` or `Unsubscribe` with a name that is not valid; `Subscribe` on a `private-` or `presence-` channel for an anonymous connection, with no matching rule, with a rule that returns `false` (or a presence rule that returns `null`), or with a rule that throws | `Subscription denied.` The same text for every cause |
| `Subscribe` on a `presence-` channel when the list cannot be updated: the cache or its lock fails, the lock stays busy for about two seconds (`The presence list of the channel is busy; try again.` is the exception in the log), or `PresenceEntryTtl` is under 3 seconds (`BroadcastingOptions.PresenceEntryTtl must be at least 3 seconds.`) | `Presence is unavailable.` The connection is taken out of the group again |

Leaving a channel that was never joined is not an error. `IPresence.MembersAsync` with a name that is not valid throws `ArgumentException`, parameter `channel`: `The channel name is not valid.` from the call itself. A valid name that is not a presence channel simply has no members.

**Logged, never thrown.**

| Level | Message | When |
|---|---|---|
| Warning | `A channel authorization rule threw; the subscription was denied.`, with the exception | A rule throws. The client sees `Subscription denied.`; the channel and the claims are not logged |
| Error | `The presence list could not be updated; the subscription was undone.`, with the exception | The presence cases above. The client sees `Presence is unavailable.` |
| Warning | `Presence member lists are kept per process because the cache lock is not shared; with several instances each one sees only its own members. Register a Redis or Memcached cache.` | Once per process, at the first presence change, when no cache is registered or its lock is process-local |
| Warning | `Removing a closed connection from the presence lists failed.`, with the exception | A connection closes and the cache fails. The entry expires after `PresenceEntryTtl`, with no `MemberLeft` |
| Warning | `Refreshing the presence entries failed; the next tick tries again.`, with the exception | The heartbeat cannot reach the cache |
| Information | `Broadcast {Event} to {ChannelCount} channel(s).` | The `log` driver, for every event with channels |

The category is `Naravel.Broadcasting` for the third row and for the `log` driver, `Naravel.Broadcasting.BroadcastHub` for the first, second and fourth, and `Naravel.Broadcasting.PresenceHeartbeatService` for the fifth. No test makes the cache or its lock fail, so the busy-lock and cache-failure causes of `Presence is unavailable.` and the fourth and fifth rows are read from the source only.

**The fake.**

| When | Exception and message |
|---|---|
| `AssertBroadcast()` with nothing recorded | `InvalidOperationException`: `No broadcast was recorded.` |
| `AssertBroadcast(predicate)` that nothing matches | `InvalidOperationException`: `No matching broadcast was recorded (2 in total).` |
| `AssertBroadcastCount(2)` with one recorded | `InvalidOperationException`: `Expected 2 broadcast(s), but found 1.` |
| `AssertNothingBroadcast()` with one recorded | `InvalidOperationException`: `Expected no broadcast, but 1 were recorded.` |

**No error at all.** A channel nobody listens to, an event that lists a channel twice (it is sent once), `Unsubscribe` from a channel the connection never joined, a second connection of a user who is already a member (no `MemberJoined`), and the entries of a crashed instance expiring (no `MemberLeft`).

## Testing

`BroadcastFake` is an `IBroadcaster` that records what was sent: register it in place of the real one and call `AssertBroadcast(predicate)`, `AssertBroadcastCount(n)` or `AssertNothingBroadcast()`. To test authorization, use `Microsoft.AspNetCore.TestHost` and `Microsoft.AspNetCore.SignalR.Client` against `MapNaravelBroadcasting`, as this package's own tests do. The presence list is also tested on a real Redis with two connections that stand for two servers (`PresenceRedisTests`); those tests run only when `NARAVEL_TEST_REDIS` is set, which the CI `services` job does.

## Complete example

An event with a private channel and an authorization rule, the dispatch from an endpoint, the browser client and a test with `BroadcastFake`. `ShipOrderHandler` is your own code that takes an `IBroadcaster`.

```csharp
// 1. The event lists its channels. A "private-" channel needs an authorization rule.
public sealed record OrderShipped(int OrderId, string Carrier) : IBroadcastable
{
    public IEnumerable<string> Channels() => [$"private-orders.{OrderId}"];
}

// 2. Program.cs
builder.Services.AddNaravelBroadcasting(builder.Configuration,
    o => o.Channels.Authorize("private-orders.{id}", (user, id) => user.FindFirst("order")?.Value == id));
builder.Services.AddBroadcastOn<OrderShipped>();             // broadcast it when Naravel.Events dispatches it
app.UseAuthentication(); app.UseAuthorization();
app.MapNaravelBroadcasting("/hubs/broadcast");

// 3. Send it: through the events dispatcher (opted in above) or straight through IBroadcaster.
app.MapPost("/orders/{id:int}/ship", async (int id, IEventDispatcher events, CancellationToken ct) =>
{
    await events.DispatchAsync(new OrderShipped(id, "DHL"), ct);
    return Results.Accepted();
});
// or: await broadcaster.BroadcastAsync(new OrderShipped(id, "DHL"), ct);   // IBroadcaster, inline and at most once
```

```js
// 4. Browser (@microsoft/signalr): the user must hold the "order" claim for id 7.
const connection = new signalR.HubConnectionBuilder().withUrl("/hubs/broadcast").build();
connection.on("OrderShipped", message => console.log("shipped", message));
await connection.start();
await connection.invoke("Subscribe", "private-orders.7");
```

```csharp
// 5. Test: register the fake in place of the real broadcaster.
var fake = new BroadcastFake();
await new ShipOrderHandler(fake).ShipAsync(7);
fake.AssertBroadcastCount(1);
```

## Telemetry

Meter and activity source `Naravel.Broadcasting`; counter `naravel.broadcasting.broadcasts` tagged with `driver` and `outcome` (`succeeded`, `failed`, `cancelled`) only. Channel names, payloads and tokens are never tags or log values.

## Laravel to .NET

| Laravel | Naravel |
|---|---|
| `ShouldBroadcast`, `broadcastOn()`, `broadcastAs()`, `broadcastWith()` | `IBroadcastable`, `Channels()`, `BroadcastAs()`, `BroadcastWith()` |
| `broadcast($event)` | `IBroadcaster.BroadcastAsync(event)` |
| `Broadcast::channel('orders.{id}', fn ($user, $id) => ...)` | `Channels.Authorize("private-orders.{id}", (user, id) => ...)` |
| `/broadcasting/auth` | The hub authorizes when the client calls `Subscribe` |
| Drivers `pusher`, `ably`, `reverb`, `redis`, `log`, `null` | `signalr`, `log`, `null` (a backplane replaces `redis`) |
| Queued by default | Inline, at most once |
| `Broadcast::channel('presence-room.{id}', fn ... => ['id' => ..., 'name' => ...])`, `Echo.join()` | `Channels.AuthorizePresence(..., (user, id) => new PresenceMember(userId, info))`, `Subscribe`, `MemberJoined`, `MemberLeft`, `IPresence` |

## What this does not cover

Replacing SignalR, Pusher/Ably/Reverb drivers, queued broadcasting, the client libraries, "typing" whispers (client events), and a `MemberLeft` for entries that expire after a crash.
