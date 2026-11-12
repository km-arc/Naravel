# Naravel.Broadcasting

[![NuGet](https://img.shields.io/nuget/v/Naravel.Broadcasting.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Broadcasting)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Broadcasting.svg)](https://www.nuget.org/packages/Naravel.Broadcasting)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Typed real-time broadcasting over SignalR for Naravel: IBroadcastable events, channel authorization rules, signalr/log/null drivers and a test fake, built on Naravel.Foundation and Naravel.Events.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Broadcasting` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Foundation`, `Naravel.Cache` and `Naravel.Events`, which NuGet installs with it, and ASP.NET Core (the `Microsoft.AspNetCore.App` shared framework) |
| **Drivers** | `signalr` (the default), `log` and `null`, chosen by `Broadcasting:Default` |

## What you get

- **Events that name their channels.** An `IBroadcastable` event returns its `Channels()`, and `AddBroadcastOn<T>()` opts each event type in.
- **Who may join is a registered rule.** `Channels.Authorize("private-orders.{id}", ...)` decides which connections may join a channel.
- **Presence channels** tell their members who else is there, like Laravel's `Echo.join()`.
- **Swappable drivers.** Switch in configuration, or add your own at run time with `BroadcastManager.Extend`.
- **A test fake.** `BroadcastFake` is an `IBroadcaster` that records the message the real one would send.

## Quick start

```csharp
using Naravel.Broadcasting;
using Naravel.Events;   // AddNaravelEvents and IEventDispatcher come from Naravel.Events

builder.Services.AddNaravelEvents();
builder.Services.AddNaravelBroadcasting(
    builder.Configuration,
    o => o.Channels.Authorize("private-orders.{id}", (user, id) => user.FindFirst("order")?.Value == id));   // the "order" claim is your own
builder.Services.AddBroadcastOn<OrderShipped>();   // Naravel.Events dispatches by exact type, so opt each event type in

app.MapNaravelBroadcasting("/hubs/broadcast");

app.MapPost("/orders/{id}/ship", async (string id, IEventDispatcher events, CancellationToken ct) =>
{
    await events.DispatchAsync(new OrderShipped(id), ct);   // the listener broadcasts before this returns
    return Results.Accepted();
});

public sealed record OrderShipped(string OrderId) : IBroadcastable
{
    public IEnumerable<string> Channels() => [$"private-orders.{OrderId}"];
}
```

A client calls the hub method `Subscribe` with a channel name and listens under the event's `BroadcastAs()` name, which is the type name by default. A channel that starts with `private-` or `presence-` is refused unless the connection is authenticated and a registered rule allows it, so the host also needs ASP.NET Core authentication that SignalR connections can use; any other channel name is public.

Delivery is inline and at most once: it finishes before `BroadcastAsync` returns, nothing is replayed, and a client that is offline misses the message. With several instances, configure a SignalR backplane yourself. `Broadcasting:Default` is `signalr`; named drivers go under `Broadcasting:Stores:<name>:Driver` (`signalr`, `log` or `null`). Presence lists live in the application's cache, so they are shared between instances only when `AddNaravelCache` (from `Naravel.Cache`) is configured with a Redis or Memcached default store; otherwise they are per process. In tests, register `BroadcastFake` from `Naravel.Broadcasting.Testing` in place of the real `IBroadcaster`.

## Learn more

- Broadcasting: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/broadcasting.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
