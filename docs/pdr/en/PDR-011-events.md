# PDR-011 — Events (`Naravel.Events`)

Status: **Approved 2026-10-07.**

## Context

Laravel delivers a lightweight event dispatcher: listeners are registered by type, dispatched by event instance, ordered by registration, and can stop propagation. Many apps use that pattern to decouple domain actions from side effects and to keep event handlers testable.

The native baseline is already strong: .NET has delegates, DI, `IHostedService`, and a very fast, typed way to compose handlers. The useful gap is not a full PHP-style event system; it is a small, async-friendly dispatcher with explicit ordering and a queue adapter for long-running listeners.

Laravel surface: `Illuminate\Contracts\Events\Dispatcher` plus listener/subscriber registration. Native baseline: DI, delegates, `CancellationToken`, and provider-native hosted services. Rule zero suggests a thin layer only where the Laravel API adds value beyond the .NET default.

## Native-first verdict

A tiny dispatcher built on typed listeners is the correct fit. It is simpler than trying to mimic PHP's dynamic runtime discovery and safer than forcing a heavy `MediatR`-style abstraction into the core package. The event system should support explicit registration, typed dispatch, ordering, and a queue adapter, while leaving native .NET patterns available to the app.

## Proposed decision

Add a new `Naravel.Events` package with these primary contracts:

- `IEventDispatcher`: dispatches a single event instance and resolves listeners through DI.
- `IEventListener<TEvent>`: handler contract for a typed event.
- `EventDispatcher` / default implementation: resolves listener plans once, caches them by event type, and dispatches in registration order.
- `EventFake`: in-memory fake for tests and assertions.

Subscriber-style groups are ordinary DI registrations or explicit `Listen<TEvent>` calls; the core does not infer
subscriber methods or scan assemblies.

Events is not a driver-based module, so Foundation's `Manager.Extend` is not applicable. Applications can replace
the dispatcher or listener invoker through normal DI registration.

Registration is explicit and DI-based. Common case:

```csharp
services.AddNaravelEvents();
events.Listen<OrderPaid>((evt, context, ct) => listener.HandleAsync(evt, context, ct));
await events.DispatchAsync(new OrderPaid(orderId));
```

The dispatcher accepts a typed event instance and invokes relevant listeners. `Listen<TEvent>` subscriptions are scoped
to that dispatcher instance, run in registration order after DI listeners, and return a disposable registration handle.
It supports:

- registration order (first registered, first invoked)
- stop propagation (`EventDispatchContext` or `bool` return pattern)
- per-listener exceptions stop dispatch and propagate to the caller
- `CancellationToken` overloads for async listeners
- a queued-listener adapter that sends selected listeners through the queue worker

A `Naravel.Events.Queue` adapter is optional and intentionally separate to avoid a dependency cycle with the Queue module. It uses the queue dispatcher to run a listener implementation after the app event is raised. The queue adapter must live in the optional adapter project, not in the core package.

Listener execution stops and propagates the first exception. This preserves normal .NET exception semantics and avoids reporting a partially successful dispatch as complete; applications that need independent best-effort handling should catch and record failures inside their listener.

Listener plans are built once and cached per event type; no reflection is needed on the hot path. This aligns with the definition of done and keeps dispatch fast.

## Contracts and quality

- All async entry points accept `CancellationToken`; no sync-over-async.
- Listener lookup is by exact event type and cached; no runtime type scanning or `Type.GetType` on hot paths.
- `EventFake` exposes deterministic assertions for dispatched event types, counts, dispatch order, and reset; stop-propagation is tested against the real dispatcher.
- Emit `Meter` counters plus `ActivitySource` spans for dispatched events and listener failures.
- The queue adapter uses the approved `IJobDispatcher` contract and is covered by a test that runs the listener in the Memory queue worker.
- Keep the core package dependency-free; the queued adapter only depends on the queue interfaces and the event package.

## Verification required after approval

Tests must cover registration order, multiple listeners, stop-propagation, failure policy, cancellation, `Extend` behavior (if applicable), and queued-listener execution through the Memory queue. Add a benchmark for a hot-path dispatch. Update the English and Persian docs, parity matrix, and final stage status once the package is implemented. Run the solution restore/build/test command before closing the stage.

## Alternatives considered

1. **Use only delegates / native DI.** Rejected as the common path is not Laravel-like enough and misses the explicit registration model and queue adapter.
2. **Add runtime reflection-based discovery.** Rejected; it is slower, brittle, and violates the speed and explicitness rules.
3. **Couple events directly to the Queue module.** Rejected; it creates a dependency cycle and mixes two separate concerns.
4. **Adopt a full MediatR-style abstraction.** Rejected as a heavy opinionated abstraction for a small core feature.

## Approval

The owner approved this scope on 2026-10-07: a thin event dispatcher with typed listeners, explicit ordering, stop propagation, a queue adapter, and a fake for tests. No PHP magic discovery or reflection-based event scanning is in scope.
