# Naravel.Events

Typed event dispatcher with listener ordering, stop propagation, and optional queue adapter support.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Events
```

Targets .NET 10.

## Use

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

- [`Naravel.Events.Queue`](https://www.nuget.org/packages/Naravel.Events.Queue): Optional Naravel.Events adapter for running selected event listeners through Naravel.Queue.

## Documentation

- Events: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/events.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
