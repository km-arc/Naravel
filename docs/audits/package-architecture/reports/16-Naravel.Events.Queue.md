# Review Report — Naravel.Events.Queue

## Review metadata

- Package ID: `Naravel.Events.Queue` (explicit `PackageId` in `src/Naravel.Events.Queue/Naravel.Events.Queue.csproj`).
- Source project: `src/Naravel.Events.Queue/Naravel.Events.Queue.csproj`.
- Source-review baseline: `main` at `44e957ca3db6d9f15665fc8fa070f70522c163c9`; rechecked against current `main` at `c6a7d58ffb84d885741998bb32d2082c54c4ab61`. Adapter source and tests are unchanged across that range.
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source and contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED — NO SOURCE-LEVEL FINDINGS (runtime verification pending).
- Files inspected: project metadata/references; adapter registration and public API; queued listener invoker/job; registration options and alias registry; source-generated job metadata; Events dispatcher and listener contracts; queue dispatch/worker integration; queued-listener and metadata tests; PDR-011; EN guide and NuGet README.
- Dependency boundary: this optional adapter references only the Events and Queue packages. The Events core remains independent of Queue.
- Verification performed: registration-to-registry lifecycle tracing; alias uniqueness and resolution; event serialization paths; per-listener dispatch options; Queue dispatch cancellation and worker invocation; DI listener lifetimes; AOT/trimming annotations and generated job metadata.
- Verification NOT RUN: `.NET restore/build/test`, live provider tests, full CI, mutation testing and independent review.

## Executive assessment

- The adapter preserves the core Events/Queue dependency boundary and uses an explicit listener alias rather than storing CLR type names in queued data.
- The singleton registry validates duplicate aliases and duplicate event/listener pairs before lookup. Dispatch resolves the registration from the exact event/listener pair; worker execution resolves only the stored alias from that allow-listed registry.
- Queued event payloads are serialized per registered event type. Reflection-based overloads carry trimming/AOT warnings; the `JsonTypeInfo<TEvent>` overload provides the source-generated path.
- Per-listener dispatch options are applied to a fresh `DispatchOptions` instance on every event, avoiding shared mutable routing state.
- The wrapper job is source-generated and registered under a fixed alias. It resolves the scoped listener at execution time and passes the worker cancellation token to the listener.
- The adapter does not pretend queued listeners can stop the original event propagation: they are enqueued and execute later with a fresh `EventDispatchContext`, as documented.
- No source-level architecture or correctness finding was identified in the reviewed adapter.

## Existing guarantees and coverage

- Tests cover a queued listener executing through the Memory worker, per-listener queue routing, source-generated event serialization, and the queued wrapper job's source-generated metadata and case-insensitive envelope behavior.
- The Events documentation example test covers inline and queued listeners in one dispatch path.
- The alias registry rejects duplicate aliases and duplicate event/listener registrations and rejects unknown aliases at execution.
- Queue worker retry/failure semantics apply to queued listener exceptions; the adapter does not implement a second retry system.
- The public API explicitly annotates reflection-based serialization overloads and exposes a generated-metadata overload for trimming/Native AOT.
- No runtime type loading, assembly scanning, or automatic listener discovery is introduced.

## Test assessment

- Reviewed: `QueuedEventListenerTests`, `QueuedEventJobMetadataTests`, `DocsExampleTests`, adapter source, PDR-011 and EN package documentation.
- Tests run for this review: NONE.
- Build/CI: NOT RUN.
- Live providers: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Owner decision gate

No owner decision is required for this package based on source inspection. Do not interpret this review as runtime verification or as closure of any roadmap stage.

## Plain-language explanation

This report found no source-level correctness or architecture issue in the reviewed Events Queue adapter. This is a source-review result, not a statement that live queue services or the full test suite were run.

- **What the adapter does:** It sends selected event listeners to a queue for later execution. It stores a registered listener alias in the queued job rather than storing an unrestricted CLR type name in the message.
- **What we checked:** The report inspected adapter registration, alias validation and lookup, per-listener queue options, source-generated serialization metadata, worker execution, dependency injection lifetimes, cancellation and the trimming/Native AOT annotations.
- **Why no source finding was recorded:** The adapter uses an allow-listed alias registry, rejects duplicate registrations, applies fresh dispatch options for each event, resolves scoped listeners when the worker runs, and leaves retry/failure handling to Queue core. The delayed listener receives a new dispatch context, so it cannot stop the original in-process event from continuing.
- **What is not yet proven:** Restore, build, tests, live providers, full CI, mutation tests and independent review were not run in this audit pass. The inspected tests cover Memory-worker execution and generated event metadata, but they were not executed here.
- **Safe outcome and proof:** Preserve the dependency boundary (Events core does not depend on Queue), alias allow-list and generated-metadata path. Run the adapter tests and supported provider checks before describing the package as runtime-verified.
