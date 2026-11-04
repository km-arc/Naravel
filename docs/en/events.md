# Events

`Naravel.Events` provides typed, asynchronous event dispatch with explicit listener registration, deterministic ordering, and stop propagation.

## What it is and when to use it

Use `Naravel.Events` to decouple "something happened" from "what should happen next": the code that marks an order as paid dispatches an `OrderPaid` event, and independent listeners update a read model, send a receipt or write an audit record. It is the equivalent of Laravel's event dispatcher with the parts .NET already does better removed: listeners are ordinary DI services (no discovery by naming convention), event types are compile-time types (no string event names), and exceptions are ordinary exceptions. If a single class calls a single collaborator, call it directly; events pay off when several independent reactions share one trigger.

## Quickstart

```sh
dotnet add package Naravel.Events
```

```csharp
using Naravel.Events;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelEvents();
builder.Services.AddScoped<IEventListener<OrderPaid>, WriteAuditLine>();   // a listener is an ordinary DI service

var app = builder.Build();

app.MapPost("/orders/{id}/pay", async (string id, IEventDispatcher events, CancellationToken ct) =>
{
    await events.DispatchAsync(new OrderPaid(id, 42m), ct);   // every listener of OrderPaid has run when this returns
    return Results.Ok($"Order {id} paid");
});

app.Run();

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

`POST /orders/7/pay` prints the audit line and then answers. That is the whole mechanism: an event is a record, a listener is a class with `HandleAsync`, and `DispatchAsync` calls every listener registered for that exact event type.

| Member | What it does |
|---|---|
| `AddNaravelEvents()` | Registers the scoped `IEventDispatcher`. It uses `TryAdd`, so an application can replace the dispatcher. |
| `IEventDispatcher.DispatchAsync<TEvent>(evt, ct)` | Runs every listener of exactly `TEvent`, in order. |
| `IEventDispatcher.Listen<TEvent>(callback)` | Adds a callback in the current DI scope and returns an `IDisposable`; dispose it to remove the callback. |
| `IEventListener<TEvent>.HandleAsync(evt, context, ct)` | The listener contract. |
| `IQueuedEventListener<TEvent>` | A listener that runs later on a queue worker (needs `Naravel.Events.Queue`). |
| `EventDispatchContext.Stop()` and `.StopPropagation` | Skip the remaining listeners, and tell whether someone did. |
| `Naravel.Events.Testing.EventFake` | A test double that records dispatched events and runs no listener. |

How a dispatch runs: DI-registered `IEventListener<TEvent>` services run first in their registration order; `Listen` callbacks run next, also in registration order. A listener can call `context.Stop()` to stop further listeners. The first listener exception propagates and stops dispatch.

## DI listeners

```csharp
services.AddScoped<IEventListener<OrderPaid>, UpdateOrderReadModel>();
```

The dispatcher is scoped and caches the resolved listener plan for each event type within that scope. This preserves scoped listener lifetimes. Dispatch accepts a `CancellationToken`; canceled work is propagated to the caller.

Listeners and `Listen` callbacks are matched by the exact `TEvent` at the call site. `DispatchAsync<OrderPaid>(...)` does not notify listeners registered for a base type or interface, and a variable typed as `object` is dispatched as `object`. Register a listener for each concrete event type; one class can implement several `IEventListener<T>` interfaces.

## A complete example

```csharp
// Events/OrderPaid.cs
public sealed record OrderPaid(string OrderId, decimal Total);

// Listeners/UpdateOrderReadModel.cs - runs inline, in the dispatching request
public sealed class UpdateOrderReadModel(IOrderReadModel readModel) : IEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default) =>
        readModel.MarkPaidAsync(evt.OrderId, evt.Total, cancellationToken);
}

// Listeners/SendReceipt.cs - queued: runs later on a worker
public sealed class SendReceipt(IReceiptMailer mailer) : IQueuedEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default) =>
        mailer.SendAsync(evt.OrderId, cancellationToken);
}
```

```csharp
// Program.cs
builder.Services.AddNaravelEvents();
builder.Services.AddScoped<IEventListener<OrderPaid>, UpdateOrderReadModel>();

// Only needed for queued listeners (Naravel.Events.Queue + Naravel.Queue + a driver):
builder.Services.AddQueue(builder.Configuration).AddMemoryDriver(builder.Configuration);
// usings: Naravel.Queue.Extensions (AddQueue, AddQueueWorker) and Naravel.Queue.Memory (AddMemoryDriver)
builder.Services.AddNaravelEventsQueue();
builder.Services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");
builder.Services.AddQueueWorker();
```

The complete example above is run as a test: `tests/Naravel.Events.Tests/DocsExampleTests.cs` (inline listener, queued listener on the worker, `context.Stop()`, `EventFake`). Change one, change the other.

```json
{
  "NaravelQueue": {
    "Default": "events",
    "Stores": { "events": { "Driver": "memory" } }
  }
}
```

```csharp
// Somewhere that takes an IEventDispatcher (a controller, a service, a minimal API handler)
app.MapPost("/orders/{id}/pay", async (string id, decimal total, IEventDispatcher events, CancellationToken ct) =>
{
    await events.DispatchAsync(new OrderPaid(id, total), ct);
    return Results.Accepted();
});
```

`UpdateOrderReadModel` runs inside the request, before `DispatchAsync` returns; if it throws, the exception reaches the caller and later listeners do not run. `SendReceipt` is published to the queue and returns immediately; the worker runs it later, at least once.

To stop later listeners from a listener, call `context.Stop()`:

```csharp
public sealed class RejectFraudulentOrders : IEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        if (evt.Total > 100_000m) context.Stop(); // listeners registered after this one are skipped
        return Task.CompletedTask;
    }
}
```

## Queued listeners

Install and register the optional `Naravel.Events.Queue` adapter alongside `Naravel.Queue`:

```csharp
services.AddNaravelEventsQueue();
services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");
```

Queued listeners are published through `IJobDispatcher`; the worker resolves the listener from an explicit stable alias. The core Events package has no Queue dependency and does not scan assemblies or resolve CLR types from queued data.

Adapter limits to know about:

- By default dispatch uses the default Queue connection and the `default` queue, so run a worker that consumes them. To route a listener elsewhere, pass dispatch options: `AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt", o => o.OnConnection("redis").OnQueue("mail").WithMaxAttempts(5))`. The action runs on a fresh `DispatchOptions` for every dispatch, so nothing is shared between dispatches.
- By default the event is serialized with reflection-based `System.Text.Json`. For trimming and Native AOT, pass source-generated metadata instead: `AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt", MyJsonContext.Default.OrderPaid)` (optionally followed by the dispatch options action). Keep events small and serializable either way.
- A queued listener runs later with a fresh `EventDispatchContext`: it cannot stop propagation, the dispatching code does not wait for it, and its exceptions follow Queue retry and failed-job rules rather than the first-exception policy. Execution is at-least-once, so make listeners idempotent.
- A listener that implements `IQueuedEventListener<TEvent>` but was not registered with `AddQueuedEventListener` throws `InvalidOperationException` at dispatch because it has no alias.

## The three listener kinds together

One event, a DI listener that can stop propagation, a queued listener and a request-scoped `Listen` callback. `IOrderReadStore` and `IMailer` stand for your own services. A runnable version is in `samples/Naravel.Sample` (`/events`).

```csharp
// 1. The event: a small serializable record (ids and scalars only).
public sealed record OrderPaid(string OrderId, string Email, decimal Total);

// 2. A DI listener. It runs inside DispatchAsync, in registration order.
public sealed class UpdateOrderReadModel(IOrderReadStore store) : IEventListener<OrderPaid>
{
    public async Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        await store.MarkPaidAsync(evt.OrderId, evt.Total, cancellationToken);
        if (evt.Total > 10_000m)
            context.Stop();                       // later listeners and Listen callbacks do not run
    }
}

// 3. A queued listener: dispatch only enqueues a job, the worker calls HandleAsync later (at-least-once).
public sealed class SendReceipt(IMailer mailer) : IQueuedEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
        => mailer.SendReceiptAsync(evt.Email, evt.OrderId, cancellationToken);   // make it idempotent
}

// 4. Program.cs
builder.Services.AddNaravelEventsQueue();                                          // also registers the dispatcher
builder.Services.AddScoped<IEventListener<OrderPaid>, UpdateOrderReadModel>();
builder.Services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");

// 5. Dispatch from an endpoint, and add a request-scoped callback.
app.MapPost("/orders/{id}/pay", async (string id, string email, decimal total, IEventDispatcher events, CancellationToken ct) =>
{
    using var audit = events.Listen<OrderPaid>((evt, _, _) =>
    {
        Console.WriteLine($"audit: {evt.OrderId}");      // runs after the DI listeners, then is unsubscribed on Dispose
        return Task.CompletedTask;
    });

    await events.DispatchAsync(new OrderPaid(id, email, total), ct);
    return Results.Accepted();
});
```

Order of execution for a total of 120: `UpdateOrderReadModel`, then the `Listen` callback; `SendReceipt` runs later in the queue worker (run a worker on the default connection and the `default` queue). For a total above 10,000 the first listener stops propagation, so nothing else runs.

## Errors

| When | What happens |
|---|---|
| `DispatchAsync(null)` | `ArgumentNullException`. |
| The token is cancelled before or between listeners | `OperationCanceledException`; no further listener runs and it is not counted as a failure. |
| A listener throws | The same exception reaches the caller of `DispatchAsync`; later listeners do not run, and the dispatch is counted as failed. |
| A queued listener was not registered with `AddQueuedEventListener` | `InvalidOperationException` at dispatch: `Queued listener 'MyApp.SendReceipt' for event 'MyApp.OrderPaid' has no explicit queue alias registration.` |
| A blank alias passed to `AddQueuedEventListener` | `ArgumentException`. |
| The same alias registered twice | `InvalidOperationException`: `Queued event listener alias 'order-paid.receipt' is registered more than once.` |
| The same listener registered twice for one event | `InvalidOperationException`: `Queued event listener 'MyApp.SendReceipt' for 'MyApp.OrderPaid' is registered more than once.` |
| A worker receives a job whose alias it does not know (for example from an older or newer deployment) | `InvalidOperationException`: `Queued event listener alias 'order-paid.receipt' is not registered.` The job then follows Queue's retry and failed-job rules. |
| A queued payload deserializes to `null` | `InvalidDataException`: `Queued event payload for alias 'order-paid.receipt' was null.` |

The two duplicate-registration errors are raised when the queued-listener registry is first created from the registrations, not when `AddQueuedEventListener` is called.

`EventFake` assertions throw `InvalidOperationException`:

| Assertion | Message |
|---|---|
| `AssertDispatched<OrderPaid>()` when none was dispatched | `No OrderPaid event was dispatched.` |
| `AssertDispatched<OrderPaid>(1)` when there were none | `Expected 1 OrderPaid events to be dispatched, but found 0.` |

## Observability and testing

`Meter` and `ActivitySource` are both named `Naravel.Events`. The metrics are `naravel.events.dispatched` and `naravel.events.failed` (counters) and `naravel.events.dispatch.duration` (a histogram in milliseconds); the span is `events.dispatch`. `Naravel.Events.Testing.EventFake` records dispatched events and supports type/count assertions and reset; it does not execute listeners. `AddNaravelEvents()` uses `TryAdd`, so an application can replace `IEventDispatcher` explicitly.

### Testing code that dispatches events

Inject `EventFake` where `IEventDispatcher` is expected, run the code under test, then assert on what was dispatched. The fake does not run listeners, so a test of the dispatching code needs no read model, mailer or queue:

```csharp
[Fact]
public async Task Paying_an_order_dispatches_OrderPaid()
{
    var events = new EventFake();
    var service = new CheckoutService(events);

    await service.PayAsync("order-7", 42m, CancellationToken.None);

    var evt = events.AssertDispatched<OrderPaid>();   // throws when none was dispatched
    Assert.Equal("order-7", evt.OrderId);
    events.AssertDispatched<OrderPaid>(1);            // exactly one
    events.Reset();                                   // forget everything recorded so far
}
```

Test a listener on its own by constructing it and calling `HandleAsync` with a new `EventDispatchContext`, then check `context.StopPropagation` if it may stop propagation.
