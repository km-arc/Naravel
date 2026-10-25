# PDR-018 — Broadcasting (`Naravel.Broadcasting`)

**Native-first verdict:** SignalR already delivers real-time messages: `IHubContext<TaskHub>` plus `Clients.Group("orders.7").SendAsync("OrderShipped", payload)` is one line, with `[Authorize]` on the hub and Redis or Azure SignalR backplanes built in. What it does not give is Laravel's **typed event contract and channel authorization**: an event class that says which channels it goes to, a registered rule for who may join `private-orders.{id}`, one place to switch the transport, and a fake for tests. **Proposed outcome: accepted as a thin layer over SignalR, and nothing that replaces SignalR.**

Status: **Approved 2026-10-25.**

## Context

Laravel: an event implements `ShouldBroadcast` with `broadcastOn()` (channels), `broadcastAs()` and `broadcastWith()`; `Broadcast::channel('orders.{id}', fn ($user, $id) => ...)` authorizes private and presence channels (names start `private-` or `presence-`); drivers `pusher`, `ably`, `reverb`, `redis`, `log`, `null`; `Broadcast::routes()` exposes `/broadcasting/auth`. Broadcasting is queued by default.

What ASP.NET Core SignalR provides (part of the shared framework `Microsoft.AspNetCore.App`, which `Naravel.Routing` already references): hubs, groups, users, `IHubContext<T>` for sending from outside a hub, hub filters, `[Authorize]`, backplanes (Redis, Azure SignalR Service), and the client libraries. SignalR **delivery is at most once**: a client that is offline misses the message.

Gaps this PDR targets:

- The group name is a free string, and **joining a group is not authorized by default**: a hub method like `JoinGroup(name)` is application code, and forgetting the check lets any connected client listen to any group.
- No typed event: payload shape and event name are scattered across call sites.
- No test double: tests need a SignalR client and a server.

Constraint from Naravel.Events (verified in its docs): listeners match the **exact event type**, with no base-type or interface dispatch. A generic "broadcast everything that implements `IBroadcastable`" listener would not be called, so the event integration must be explicit per event type.

## Proposed quickstart (S1)

```csharp
builder.Services.AddNaravelBroadcasting(o => o.Channels.Authorize("private-orders.{id}", (user, id) => user.FindFirst("order")?.Value == id));
app.MapNaravelBroadcasting("/hubs/broadcast");                      // the one hub that clients connect to

public sealed record OrderShipped(int OrderId) : IBroadcastable
{
    public IEnumerable<string> Channels() => [$"private-orders.{OrderId}"];
}

await broadcaster.BroadcastAsync(new OrderShipped(7), cancellationToken);   // broadcaster is an injected IBroadcaster
```

## Proposed decision

1. New package `Naravel.Broadcasting` over `Manager<,>` (drivers `signalr`, `log`, `null`; PDR-004 and PDR-005 configuration shape), referencing the ASP.NET Core shared framework like `Naravel.Routing`. A Redis backplane stays a native SignalR option the application configures (out of scope).
2. **`IBroadcastable`:** `Channels()` is required; `BroadcastAs()` (default: the type name) and `BroadcastWith()` (default: the event serialized with `System.Text.Json`) are optional. **`IBroadcaster.BroadcastAsync(event, ct)`** sends to every channel returned. Events integration is explicit, matching exact-type dispatch: `AddBroadcastOn<TEvent>()` registers an `IEventListener<TEvent>` that broadcasts, so the user states which events go out.
3. **One hub, `BroadcastHub`,** mapped with `MapNaravelBroadcasting(path)`. Clients call `Subscribe(channel)`. A channel that starts with `private-` or `presence-` runs the registered authorization callbacks (`Channels.Authorize("private-orders.{id}", (user, id) => ...)` with route-style placeholders) and is **refused when no callback matches**: unregistered private channels are denied, not allowed. Public channels need no rule. The callback gets the authenticated `ClaimsPrincipal`; the hub requires authentication for private and presence channels.
4. **Presence (decision D4, option C):** a `presence-` channel has a member list kept in the **shared cache** (`ICacheStore` and `ICacheLock` from `Naravel.Cache`), so every instance sees the same members. A rule registered with `Channels.AuthorizePresence` returns a `PresenceMember` (user id and small info) or `null` to refuse. Members are users: several connections of one user are one member. `Subscribe` returns the list; the others receive `MemberJoined` and `MemberLeft`. Each change is a read-modify-write of one entry per channel under a per-channel lock; each instance refreshes its own connections' entries every third of `PresenceEntryTtl` (90 seconds), so entries of a crashed instance expire (without a `MemberLeft`). `IPresence.MembersAsync` reads the list from server code. With a non-shared lock the module logs one warning.
5. **Delivery contract, stated in the docs:** at most once, no replay, no durable queue. The first version sends inline; broadcasting through `Naravel.Queue` (Laravel's default) is an optional later adapter, not part of this PDR.
6. **Failure behaviour:** an exception from a transport is surfaced to the caller of `BroadcastAsync` (not swallowed); sending to a channel with no listeners is not an error.
7. **S3:** `BroadcastFake` records broadcasts (event, channels, name, payload) with assertions, like the other fakes; a `Meter` `Naravel.Broadcasting` with counter `naravel.broadcasting.broadcasts` tagged by `driver` and `outcome` only (never channel names that embed user ids, payloads or tokens), and an `ActivitySource`.
8. **S2:** a broadcast is I/O-bound; no benchmark. Channel-rule matching is resolved once into compiled patterns, not by reflection per call.
9. **Out of scope:** replacing SignalR, Pusher/Ably/Reverb drivers, a `/broadcasting/auth` HTTP endpoint (SignalR authorizes on join instead), the Redis backplane, client libraries.

## Alternatives considered

1. **Decline: use `IHubContext` and hub methods directly.** The honest native answer and a valid choice. It leaves the unauthorized-group trap, the scattered event contract and the missing fake to every application.
2. **A broadcasting driver per vendor (Pusher, Ably, Reverb).** Rejected for now: each is a separate protocol and dependency; SignalR is the native transport.
3. **Auto-broadcast every event that implements the interface.** Rejected: Events dispatches by exact type, and a reflection scan at startup or dispatch is exactly the hidden behaviour the project avoids.
4. **Queue by default, as Laravel does.** Deferred: it needs the Queue adapter and a payload contract; at-most-once inline delivery is explicit and simple.

## Decisions for the owner

- **D1:** accept the thin layer (recommended), or `DECLINED` with a recipe (alternative 1).
- **D2:** unregistered `private-`/`presence-` channels are **denied** (recommended: safe by default), or allowed.
- **D3:** explicit `AddBroadcastOn<TEvent>()` event integration (recommended), or only direct `BroadcastAsync`.
- **D4:** presence. Options were A no member list in the first version, B an in-process list (wrong with several instances, silently) and C a list in the shared cache (decided: C).
- **D5:** inline, at-most-once delivery now; a queue adapter later (decided: A).

## Contracts and quality (if accepted)

Authorization callbacks run before any group join; a callback that throws denies the join and is logged without the user's claims; channel names and payloads are never put in metric tags or log templates beyond the channel pattern; async with `CancellationToken`. Tests: a TestHost with a real SignalR client receives the event on a public channel; an authorized private join receives it; an unauthorized and an unregistered private join are rejected and receive nothing; `BroadcastFake` assertions; `Extend` and driver switch tests; the exact-type event integration.

## Approval

The owner decided on 2026-10-25: D1 to D3 as recommended, **D4 option C** and **D5 option A**.

- **D1** build the thin `Naravel.Broadcasting`: the typed event contract, channel authorization and a fake are what SignalR lacks.
- **D2** unregistered `private-` and `presence-` channels are refused, because an unchecked group join lets any client listen to any group.
- **D3** explicit `AddBroadcastOn<TEvent>()`, because `Naravel.Events` dispatches by exact type.
- **D4 option C:** a presence member list in the shared cache. Option A (no list) was the recommendation; the owner chose C because the list is wanted now and an in-process list (B) is silently wrong with several instances. The cost is the design in point 4: a per-channel lock, a heartbeat, and expiry of entries after a crash without a `MemberLeft`.
- **D5 option A:** inline, at-most-once delivery. The caller sees a transport failure; a queue adapter is a later, separate package if it is needed.
