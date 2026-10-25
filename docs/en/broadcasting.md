# Broadcasting

`Naravel.Broadcasting` is a thin layer over SignalR. SignalR already delivers messages (`IHubContext<T>`, groups, backplanes); this package adds what it does not have: an event class that says which channels it goes to, a registered rule for who may join `private-orders.{id}`, one place to switch the transport, and a fake for tests. It does not replace SignalR.

Decision and reasons: [PDR-018](../pdr/en/PDR-018-broadcasting.md). Persian version: [docs/fa/broadcasting.md](../fa/broadcasting.md).

## Quickstart

```csharp
builder.Services.AddNaravelBroadcasting(builder.Configuration,
    o => o.Channels.Authorize("private-orders.{id}", (user, id) => user.FindFirst("order")?.Value == id));
builder.Services.AddBroadcastOn<OrderShipped>();                  // optional: broadcast this event when Naravel.Events dispatches it
app.UseAuthentication(); app.UseAuthorization();
app.MapNaravelBroadcasting("/hubs/broadcast");                    // the one hub clients connect to

public sealed record OrderShipped(int OrderId) : IBroadcastable
{
    public IEnumerable<string> Channels() => [$"private-orders.{OrderId}"];
}

await broadcaster.BroadcastAsync(new OrderShipped(7), cancellationToken);   // broadcaster is an injected IBroadcaster
```

A browser client (`@microsoft/signalr`) connects to `/hubs/broadcast`, calls `connection.invoke("Subscribe", "private-orders.7")` and listens with `connection.on("OrderShipped", payload => ...)`. The method name is `BroadcastAs()` (the type name by default) and the payload is `BroadcastWith()` (the event itself by default) serialized as JSON in camelCase.

## Channels and authorization

| Name | Who may subscribe |
|---|---|
| `news`, `orders.7` (public) | Any connection |
| `private-...` | An authenticated user, and a rule from `Channels.Authorize` that matches and returns `true` |
| `presence-...` | An authenticated user, and a rule from `Channels.AuthorizePresence` that returns the member for this user (`null` refuses). The channel has a member list, see Presence below. |

- A protected channel with **no matching rule is refused**, and so is one whose rule throws. This is the safe default: in plain SignalR a hub method that adds a client to any group is open to every client.
- A pattern matches the whole channel name. A placeholder such as `{id}` matches one or more characters except `.` (as in Laravel). The first registered rule that matches decides.
- The single-placeholder form is `Authorize("private-orders.{id}", (user, id) => ...)`. `Authorize` is for `private-` patterns and `AuthorizePresence` for `presence-` patterns; the wrong prefix is refused when the rule is registered. For several placeholders, or a rule that awaits something (a database call), use `Authorize("private-teams.{team}.users.{user}", async (user, values, ct) => ...)`; `values` holds the placeholders by name and `ct` is cancelled when the connection closes. A rule is a delegate: capture what it needs when you register it.
- Names use letters, digits and `_ . : -`, at most 128 characters. Anything else is refused by the hub and by `BroadcastAsync`.
- A refusal is the same fixed `HubException("Subscription denied.")` for every cause, so a client cannot tell "no such rule" from "not allowed".

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

## Testing

`BroadcastFake` is an `IBroadcaster` that records what was sent: register it in place of the real one and call `AssertBroadcast(predicate)`, `AssertBroadcastCount(n)` or `AssertNothingBroadcast()`. To test authorization, use `Microsoft.AspNetCore.TestHost` and `Microsoft.AspNetCore.SignalR.Client` against `MapNaravelBroadcasting`, as this package's own tests do.

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
