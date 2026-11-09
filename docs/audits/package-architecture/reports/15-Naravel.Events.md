# Review Report — Naravel.Events

## Review metadata

- Package ID: `Naravel.Events` (explicit `PackageId` in `src/Naravel.Events/Naravel.Events.csproj`).
- Source project: `src/Naravel.Events/Naravel.Events.csproj`.
- Source-review baseline: `main` at `44e957ca3db6d9f15665fc8fa070f70522c163c9`; rechecked against current `main` at `c6a7d58ffb84d885741998bb32d2082c54c4ab61`. Events source, tests and benchmark are unchanged across that range.
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source and contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED — NO SOURCE-LEVEL FINDINGS (runtime verification pending).
- Files inspected: project metadata and package references; dispatcher/context/contracts; direct listener invoker and DI registration; telemetry; fake; dispatcher, telemetry, docs-example, queued-listener and metadata tests; hot-path benchmark; PDR-011; English guide and NuGet README.
- Dependency boundary: core Events depends only on DI abstractions. Queue support remains in the separate optional `Naravel.Events.Queue` adapter; no Queue dependency is introduced into the core package.
- Verification performed: listener resolution/lifetime tracing; exact-type matching; registration order and stop-propagation control flow; cancellation propagation; subscription synchronization/disposal; exception and telemetry paths; fake and benchmark review.
- Verification NOT RUN: `.NET restore/build/test`, benchmark execution, full CI, mutation testing and independent review.

## Executive assessment

- The core package is small and native-first: typed DI listeners, explicit per-scope callbacks, deterministic ordering and a typed stop-propagation context.
- Listener plans are cached by exact event type within the scoped dispatcher. The resolved listener instances remain owned and disposed by the DI scope; tests pin scoped listener identity within one scope and separation across scopes.
- Subscription mutation is protected by a lock and dispatch snapshots the callback list before invocation. Subscription disposal is idempotent.
- Cancellation is checked before dispatch and between listeners, and is passed to both DI listeners and scoped callbacks. The fake also honors a pre-cancelled token before recording an event.
- The first listener exception stops dispatch and propagates; successful dispatches and failures have separate low-cardinality counters, and each dispatch records duration and an Activity.
- The optional Queue adapter is kept out of the core dependency graph and has separate worker-level tests.
- No source-level architecture or correctness finding was identified in the reviewed core package.

## Existing guarantees and coverage

- Tests cover DI registration order, stop propagation, scoped listener lifetimes, callback ordering/unsubscription, exception propagation, pre-cancelled dispatch, fake assertions/reset, DI override, telemetry and activity, and the complete English documentation example.
- Queue adapter tests cover execution through the Memory worker, per-listener queue routing and source-generated event metadata.
- A warm-dispatch BenchmarkDotNet benchmark with `MemoryDiagnoser` exists.
- Exact event-type matching (no base/interface fan-out) is explicitly documented and tested through the documented contract.
- No reflection-based discovery, global mutable event state, synchronous listener API or implicit subscriber convention is introduced.

## Test assessment

- Reviewed: `EventDispatcherTests`, `EventTelemetryTests`, `DocsExampleTests`, `QueuedEventJobMetadataTests`, `QueuedEventListenerTests`, `EventDispatchBenchmarks`, PDR-011 and EN documentation.
- Tests run for this review: NONE.
- Build/CI: NOT RUN.
- Benchmark execution: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Owner decision gate

No owner decision is required for this package based on source inspection. Do not interpret this review as runtime verification or as closure of any roadmap stage.

## Plain-language explanation

This report found no source-level correctness or architecture issue in the reviewed core Events package. That means the inspected code paths matched the intended design; it does **not** mean the package was fully runtime-tested in this audit.

- **What the package does:** It sends an event to registered listeners. Listeners are resolved through dependency injection, run in a predictable order, and can stop further listeners through the dispatch context.
- **What we checked:** The report inspected listener registration and lifetime, exact event-type matching, callback subscriptions and disposal, cancellation, exception handling, telemetry, the in-memory fake, documentation examples and the benchmark. It also checked that the optional Queue adapter stays outside the core Events dependency graph.
- **Why no source finding was recorded:** The source has a clear listener ownership model, copies subscription callbacks before dispatch, passes cancellation through, stops on the first listener exception, and records separate success/failure telemetry. The tests inspected cover the corresponding contracts.
- **What is not yet proven:** The audit did not run restore, build, tests, the benchmark, full CI, mutation tests or an independent review. The exact documentation example was inspected but not executed in this pass.
- **Safe outcome and proof:** Keep the current core/optional-adapter boundary and the existing tests for ordering, scope lifetimes, cancellation, callback removal, exception propagation and telemetry. Run the package and solution verification before treating the package as runtime-verified.
