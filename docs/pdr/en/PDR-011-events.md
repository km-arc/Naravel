# PDR-011 — Events

**Native-first verdict:** .NET has no built-in, DI-aware application event dispatcher. CLR `event`/delegates couple the publisher to concrete subscriber instances, and `System.Diagnostics.DiagnosticListener` is a diagnostics channel, not an application bus. MediatR-style libraries are third-party dependencies (AGENTS.md rule 8 forbids adding one without a PDR) and bring request/response and pipeline concepts Naravel does not need. A tiny typed dispatcher on top of `Microsoft.Extensions.DependencyInjection` adds real value and stays small.

Status: **Proposed — awaiting owner approval.** No code may be written for R06.T02–T04 until the owner approves this PDR (Gate in `roadmap/stages/R06-events.md`).

## Context

Laravel surface: `Illuminate\Contracts\Events\Dispatcher` — `listen`, `dispatch`, `subscribe`, `until`, wildcard listeners, listener discovery, queued listeners (`ShouldQueue`), `Event::fake()`. Native baseline: delegates, DI and hosted services. Value in .NET: decoupled application events with an explicit, testable listener lifecycle, DI-resolved listeners, telemetry and a fake — without PHP mechanics (magic strings, class-name wildcards, directory scanning/reflection discovery).

Constraints that shape the design:
- **Rule 3 (one-way dependencies):** the core `Naravel.Events` must not depend on `Naravel.Queue`. Queued listeners live in an optional adapter project.
- **S2:** no reflection on the dispatch hot path; listener plans per event type are built once and cached.
- **OD-07:** nothing may resolve a CLR type from stored data except through the explicit alias registry. Queued listener payloads must follow the job type registry from R00.
- Events has no interchangeable drivers, so it does **not** use `Manager<TDriver, TOptions>` (AGENTS.md rule 2 applies to driver-based modules only). This is recorded as a deliberate non-use, not an omission.

## Proposed decision

### Packages

- `Naravel.Events` — contracts, dispatcher, options, telemetry, `EventFake`; depends on `Microsoft.Extensions.*` abstractions only (it needs no Foundation manager, see above).
- `Naravel.Events.Queue` (optional) — dispatches queued listeners through `IJobDispatcher`; references `Naravel.Events` and `Naravel.Queue`.
- `tests/Naravel.Events.Tests`; both new source projects and the test project are added to `Naravel.slnx`.

### Quickstart (S1: three lines of user code)

```csharp
services.AddNaravelEvents();
services.AddEventListener<OrderPaid, SendReceipt>();          // class listener resolved from DI
await events.DispatchAsync(new OrderPaid(orderId));           // IEventDispatcher injected
// or, without a class:
using var subscription = events.Listen<OrderPaid>((e, ct) => ValueTask.CompletedTask);
```

### Contracts

- `IEventDispatcher` — `ValueTask DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)` and `IDisposable Listen<TEvent>(Func<TEvent, CancellationToken, ValueTask> listener)`. Events are plain .NET types (records); no base class or marker is required.
- `IEventListener<in TEvent>` — `ValueTask HandleAsync(TEvent @event, CancellationToken ct)`.
- `IEventSubscriber` — `void Subscribe(IEventRegistrar events)`; one class registers several listeners (Laravel subscribers). Registered with `AddEventSubscriber<T>()`.
- `IStoppableEvent` (optional, PSR-14 style) — `bool PropagationStopped { get; }`; see stop-propagation below.
- `EventsOptions` — exception policy (below). No store/driver configuration.

### Behavior

1. **Matching.** A listener for `TEvent` receives events whose runtime type is `TEvent` or derives from / implements it (base classes and interfaces). The set of listeners for each concrete event type is computed once on first dispatch and cached as an array (the only reflection use, never repeated per dispatch). Wildcard/string-pattern listeners are not ported.
2. **Ordering.** Listeners run sequentially in registration order: DI-registered listeners in service registration order, then runtime `Listen` registrations in call order. No priority numbers in v1.
3. **Runtime registration.** `Listen` and the returned `IDisposable` use a copy-on-write snapshot; changing the listener set invalidates only the affected cached plans. Dispatch never takes a lock on the hot path.
4. **Stop propagation.** If the event implements `IStoppableEvent`, the dispatcher checks `PropagationStopped` before invoking each listener and stops once it is true. This replaces Laravel's "listener returns `false`" convention with an explicit, typed signal.
5. **Exception policy.** Default `Propagate`: the first exception is rethrown to the caller and remaining listeners do not run (Laravel behavior). Opt-in `ContinueAndAggregate`: all listeners run and an `AggregateException` is thrown at the end. Cancellation always propagates immediately.
6. **Lifetimes.** `IEventDispatcher` is a singleton. Listeners registered Singleton or Transient are resolved without allocating a scope; a listener registered Scoped is resolved inside a new `IServiceScope` created for that dispatch. The lifetime is known at registration, so the plan decides this once.
7. **No response collection / `until`.** `DispatchAsync` returns no listener results. Laravel's `until` (first non-null response) is not ported.
8. **Telemetry (S3).** `Meter` and `ActivitySource` named `Naravel.Events`: dispatch count, listener count, listener duration, and failures, tagged by event type. No per-dispatch allocation beyond the listener invocations themselves is the steady-state goal (validated in R06.T04).
9. **Fake (S3).** `EventFake : IEventDispatcher` records dispatched events and can run or skip listeners; assertions `AssertDispatched<T>(predicate?)`, `AssertNotDispatched<T>()`, `AssertDispatchedTimes<T>(n)`, `AssertNothingDispatched()`.

### Queued listeners (adapter, `Naravel.Events.Queue`)

- `AddQueuedEventListener<TEvent, TListener>(Action<DispatchOptions>? configure = null)` registers a core listener that builds a queue job and sends it through `IJobDispatcher`; the worker later resolves `TListener` and calls `HandleAsync`. The core never references the queue.
- The job and the event payload must be registered in the existing job type/alias registry; unregistered types fail at registration time, not at dispatch time (OD-07). The event must be serializable by the queue's serializer.
- Dispatch happens at the time of `DispatchAsync`; after-commit semantics are out of scope (no database transaction abstraction yet).

### Explicitly not ported

Model/observer events (R06 Out of scope); wildcard listeners; automatic listener discovery by scanning assemblies (an explicit `AddEventListener` call is the supported path; a source-generator or assembly-scan helper may be proposed later in its own PDR); `until`; `ShouldDispatchAfterCommit`; global `event()` helper and static facade.

## Verification required after approval

Tests cover: order and multiple listeners; base-type and interface matching; runtime `Listen`/unsubscribe and plan invalidation; stop propagation; both exception policies and cancellation; each listener lifetime; subscribers; `EventFake` assertions; Meter/ActivitySource emission. `Naravel.Events.Queue` has a test with the Memory queue proving the listener runs inside the worker. A benchmark measures dispatches/s and allocations in steady state (R06.T04). English and Persian guides, parity tables, status and changelog are updated; both PDR languages exist.

## Alternatives considered

1. **CLR `event`/delegates only.** Rejected as the whole answer: no DI-resolved listeners, no ordering/exception policy, no telemetry or fake. Delegates remain available inside the dispatcher via `Listen`.
2. **Adopt MediatR (or similar).** Rejected: third-party dependency and different problem (request/response, pipelines); AGENTS.md rule 8.
3. **Build events on `Manager<TDriver, TOptions>`.** Rejected: there is nothing to switch between; a manager would add configuration without value.
4. **Put queued listeners in the core.** Rejected: creates a dependency from Events to Queue (rule 3) and from there a potential cycle with Queue-based features.
5. **Reflection/attribute discovery of listeners.** Rejected: PHP mechanic (rule 6), conflicts with S2 and trimming/AOT; explicit registration is simple enough.
6. **Return `bool` from listeners to stop propagation.** Rejected in favor of the typed `IStoppableEvent` signal; a magic return value is easy to get wrong and cannot be expressed uniformly for delegates and async listeners.

## Approval requested

The owner is asked to approve, amend or reject each of the following. Defaults above apply if approved without comment.

1. Tiny dispatcher on DI; no Manager/driver layer; no MediatR dependency.
2. Matching by runtime type including base types and interfaces; no wildcard listeners.
3. Registration-order execution with no priority numbers in v1.
4. `IStoppableEvent` for stop-propagation.
5. Default exception policy `Propagate`, opt-in `ContinueAndAggregate`.
6. Singleton dispatcher; Scoped listeners get a per-dispatch scope.
7. Queued listeners only in `Naravel.Events.Queue`, using the existing job type registry (OD-07).
8. Explicit registration only (no assembly-scan discovery) in R06.

Owner approval: _pending_.
