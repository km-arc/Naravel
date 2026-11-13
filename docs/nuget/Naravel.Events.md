# Naravel.Events

[![NuGet](https://img.shields.io/nuget/v/Naravel.Events.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Events)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Events.svg)](https://www.nuget.org/packages/Naravel.Events)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Typed event dispatcher with listener ordering, stop propagation, and optional queue adapter support.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Events` |
| **Targets** | .NET 10 |
| **Needs** | No other Naravel package |
| **Add-on** | [`Naravel.Events.Queue`](https://www.nuget.org/packages/Naravel.Events.Queue) runs selected listeners through Naravel.Queue |

## What you get

- **Typed, asynchronous dispatch.** `DispatchAsync(new OrderPaid(...))` runs every listener of exactly that event type, in order, before it returns.
- **Listeners are ordinary DI services.** You implement `IEventListener<T>` and register it explicitly.
- **Deterministic order and stop propagation.** `EventDispatchContext.Stop()` skips the remaining listeners.
- **Queued listeners.** With `Naravel.Events.Queue`, selected listeners run through `Naravel.Queue`.
- **Observable.** A `Meter` and an `ActivitySource`, both named `Naravel.Events`, report dispatched and failed events and the dispatch duration.

## Quick start

```csharp
using Naravel.Events;

builder.Services.AddNaravelEvents();
builder.Services.AddScoped<IEventListener<OrderPaid>, WriteAuditLine>();   // a listener is an ordinary DI service

app.MapPost("/orders/{id}/pay", async (string id, IEventDispatcher events, CancellationToken ct) =>
{
    await events.DispatchAsync(new OrderPaid(id, 42m), ct);   // every listener of OrderPaid has run when this returns
    return Results.Ok($"Order {id} paid");
});

public sealed record OrderPaid(string OrderId, decimal Total);

public sealed class WriteAuditLine : IEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"audit: order {evt.OrderId} paid, total {evt.Total}");
        return Task.CompletedTask;
    }
}
```

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Events.Queue`](https://www.nuget.org/packages/Naravel.Events.Queue) | Runs selected event listeners through Naravel.Queue |

## Learn more

- Events: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/events.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
