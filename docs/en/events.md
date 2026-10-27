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

- By default dispatch uses the default Queue connection and the `default` queue, so run a worker that consumes them. To route a listener elsewhere, pass dispatch options: `AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt", o => o.OnConnection("redis").OnQueue("mail").WithMaxAttempts(5))`. The action runs on a fresh `DispatchOptions` for every dispatch, so nothing is shared between dispatches.
- By default the event is serialized with reflection-based `System.Text.Json`. For trimming and Native AOT, pass source-generated metadata instead: `AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt", MyJsonContext.Default.OrderPaid)` (optionally followed by the dispatch options action). Keep events small and serializable either way.
- A queued listener runs later with a fresh `EventDispatchContext`: it cannot stop propagation, the dispatching code does not wait for it, and its exceptions follow Queue retry and failed-job rules rather than the first-exception policy. Execution is at-least-once, so make listeners idempotent.
- A listener that implements `IQueuedEventListener<TEvent>` but was not registered with `AddQueuedEventListener` throws `InvalidOperationException` at dispatch because it has no alias.

## Complete example

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

## Testing a dispatcher call

```csharp
[Fact]
public async Task Paying_an_order_dispatches_OrderPaid()
{
    var fake = new EventFake();                                  // replaces IEventDispatcher in the test
    var handler = new PayOrderHandler(fake);                     // your code under test

    await handler.PayAsync("o-1", "ali@example.com", 120m);

    var evt = fake.AssertDispatched<OrderPaid>();                // throws if none was dispatched
    Assert.Equal("o-1", evt.OrderId);
    fake.AssertDispatched<OrderPaid>(expectedCount: 1);          // exact count
    fake.Reset();
}
```

`EventFake` records events and does not run listeners, so test listeners by calling `HandleAsync` directly.

## Observability and testing

`Meter` and `ActivitySource` are both named `Naravel.Events`. `Naravel.Events.Testing.EventFake` records dispatched events and supports type/count assertions and reset; it does not execute listeners. `AddNaravelEvents()` uses `TryAdd`, so an application can replace `IEventDispatcher` explicitly.
