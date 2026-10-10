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

Listeners and `Listen` callbacks are matched by the exact `TEvent` at the call site. `DispatchAsync<OrderPaid>(...)` does not notify listeners registered for a base type or interface, and a variable typed as `object` is dispatched as `object`. Register a listener for each concrete event type; one class can implement several `IEventListener<T>` interfaces.

## Queued listeners

Install and register the optional `Naravel.Events.Queue` adapter alongside `Naravel.Queue`:

```csharp
services.AddNaravelEventsQueue();
services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");
```

Queued listeners are published through `IJobDispatcher`; the worker resolves the listener from an explicit stable alias. The core Events package has no Queue dependency and does not scan assemblies or resolve CLR types from queued data.

Adapter limits to know about:

- Dispatch uses the default Queue connection and the `default` queue. Run a worker that consumes that connection and queue; the adapter has no per-listener connection, queue, priority, or delay options yet.
- The event is serialized with default `System.Text.Json` options (reflection based), so keep events small and serializable.
- A queued listener runs later with a fresh `EventDispatchContext`: it cannot stop propagation, the dispatching code does not wait for it, and its exceptions follow Queue retry and failed-job rules rather than the first-exception policy. Execution is at-least-once, so make listeners idempotent.
- A listener that implements `IQueuedEventListener<TEvent>` but was not registered with `AddQueuedEventListener` throws `InvalidOperationException` at dispatch because it has no alias.

## Observability and testing

`Meter` and `ActivitySource` are both named `Naravel.Events`. `Naravel.Events.Testing.EventFake` records dispatched events and supports type/count assertions and reset; it does not execute listeners. `AddNaravelEvents()` uses `TryAdd`, so an application can replace `IEventDispatcher` explicitly.
