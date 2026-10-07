# Events

`Naravel.Events` provides typed, asynchronous event dispatch with explicit listener registration, deterministic ordering, and stop propagation.

## Quickstart

```csharp
services.AddNaravelEvents();
events.Listen<OrderPaid>((evt, context, ct) => listener.HandleAsync(evt, context, ct));
await dispatcher.DispatchAsync(new OrderPaid(orderId));
```

`Listen<TEvent>` registers a callback in the current DI scope and returns an `IDisposable` subscription. DI-registered `IEventListener<TEvent>` services run first in their registration order; `Listen` callbacks run next, also in registration order. A listener can call `context.Stop()` to stop further listeners. The first listener exception propagates and stops dispatch.

## DI listeners

```csharp
services.AddScoped<IEventListener<OrderPaid>, UpdateOrderReadModel>();
```

The dispatcher is scoped and caches the resolved listener plan for each event type within that scope. This preserves scoped listener lifetimes. Dispatch accepts a `CancellationToken`; canceled work is propagated to the caller.

## Queued listeners

Install and register the optional `Naravel.Events.Queue` adapter alongside `Naravel.Queue`:

```csharp
services.AddNaravelEventsQueue();
services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");
```

Queued listeners are published through `IJobDispatcher`; the worker resolves the listener from an explicit stable alias. The core Events package has no Queue dependency and does not scan assemblies or resolve CLR types from queued data.

## Observability and testing

`Meter` and `ActivitySource` are both named `Naravel.Events`. `Naravel.Events.Testing.EventFake` records dispatched events and supports type/count assertions and reset; it does not execute listeners. `AddNaravelEvents()` uses `TryAdd`, so an application can replace `IEventDispatcher` explicitly.
