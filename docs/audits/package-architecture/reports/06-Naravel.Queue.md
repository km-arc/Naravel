# Review Report — Naravel.Queue

## Review metadata

- Package ID: `Naravel.Queue` (source project: `src/Naravel.Queue/Naravel.Queue.csproj`).
- Source-review baseline commit: `44e957ca3db6d9f15665fc8fa070f70522c163c9` (`main`).
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source and contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; runtime verification pending).
- Files inspected: core project metadata and package references; shipped/unshipped public API; manager/driver registration; dispatcher and message creation; worker lifecycle, reservation/settlement, failure and retry paths; job type registry and serializer; envelope source generation; batches and callback registries; fake; telemetry; failed-job stores/managers; provider batch repositories; relevant worker/provider tests; Queue guide and PDR-012/012a/012b.
- Dependencies: core package references `Naravel.Foundation` and Microsoft.Extensions abstractions; no direct broker/client provider dependency. Provider projects remain optional.
- Verification performed: source-level control-flow tracing for batch dispatch, terminal batch callback delivery, failed-job retry and batch cancellation; test coverage mapping; direct clock inventory.
- Verification NOT RUN: `dotnet restore`, `dotnet build`, `dotnet test`, live Redis/RabbitMQ/Kafka service tests, full CI, mutation testing, and independent review. Do not treat source inspection as runtime proof.

## Executive assessment

- Queue's alias-based job deserialization boundary is sound at source level: stored aliases resolve only through the explicit registry; assembly-qualified names are not resolved from payload data.
- Worker reservation handling has explicit settle/hand-back states and pre-execution failure tests. The message is handed back when work fails before settlement; already-acknowledged messages are not sent through the retry path.
- The main architectural gap found in this pass is batch-dispatch failure handling: batch metadata is committed before sequential publishing, with no compensation/reconciliation on a mid-stream failure. This can leave durable batch records permanently incomplete.
- Failed-job retry retains the old batch association. For a cancelled batch, the worker acknowledges the retry without running it. For a non-cancelled but already-finished batch, the retry may run but cannot update the completed batch. The owner must define whether a retry is independent or reopens batch accounting.
- Callback recovery claims need to distinguish persistent repositories from the default in-memory repository. The in-memory implementation cannot retry a callback failure on worker restart.
- No runtime code, tests, package references, or dependencies were changed during this review.

## Findings

### [P2] QUE-001 — BatchAsync can leave an unreachable, permanently pending batch after a partial enqueue

- Remediation state: OPEN.
- Architecture disposition: VETO AV-009 — batch dispatch must have a recoverable, observable partial-publication lifecycle.
- Evidence status: CONFIRMED BY SOURCE-LEVEL CONTROL-FLOW ANALYSIS; injected partial-publish reproduction NOT RUN.
- Evidence: `src/Naravel.Queue/Dispatch/JobDispatcher.cs:115-136` materializes the jobs, creates the batch using the full `TotalJobs`, registers it through `IBatchRepository.RegisterAsync`, then builds and publishes each message sequentially. There is no catch/compensation path around the publishing loop. The batch ID is returned only after every publish succeeds.
- Affected contract: `IJobDispatcher.BatchAsync` with any driver or serializer failure after registration and before all jobs are published.
- User impact: if publishing job N fails after jobs 1..N-1 were accepted, the batch's `TotalJobs` still includes all planned jobs. No messages exist for the remaining members, so no worker can increment their terminal counts. The caller receives an exception and does not receive the generated batch ID. Persistent repositories retain a batch that can remain pending indefinitely; completion callbacks never fire.
- Trigger / failure scenario: a two-job batch registers `TotalJobs = 2`; the first `PushAsync` succeeds; the second `PushAsync` throws (broker outage, serialization error, configuration reload, cancellation, etc.). The first message can finish, leaving one permanently pending count.
- Why existing tests do not catch it: `tests/Naravel.Queue.Tests/WorkerTests.cs` covers successful batch completion, cancellation, AllowFailures and a throwing callback. The SQLite provider contract covers repository recreation and cancellation. Neither injects a failure into the Nth publish in `BatchAsync` and asserts that the batch can be recovered or its identifier reported.
- Required correction: define an explicit partial-dispatch contract before implementation. At minimum, the outcome must be observable and recoverable; a failure must not leave a batch record whose missing members can never be settled. Possible designs include a persisted dispatch manifest/state machine with idempotent publish/reconciliation, or an explicit partial-dispatch result/exception carrying the batch identity plus a deterministic terminal state for un-published members. Do not assume arbitrary brokers/connections can participate in one transaction. Preserve at-least-once duplicate handling.
- Regression tests required: fail the second and later publish; fail message construction/serialization after the first publish; cancel during publication; recreate persistent repository after failure; prove the batch does not remain invisibly pending and callbacks/state follow the documented policy.
- Public API / PDR / release impact: batch dispatch failure semantics and likely additive API surface. Owner decision AD-009 is required before selecting the public failure/result contract.

### [OBS — OWNER DECISION] QUE-002 — Failed-job retry preserves a terminal batch ID and can silently discard the retry

- Remediation state: OPEN — OWNER DECISION.
- Evidence status: CONFIRMED BY SOURCE-LEVEL CONTROL-FLOW ANALYSIS; dedicated regression NOT RUN.
- Evidence: `src/Naravel.Queue/Failed/FailedJobManager.cs:77-88` clones the failed envelope, assigns a new message ID, resets attempts and reservation fields, sets `AvailableAt`, republishes it and removes the failed record. It does not clear or otherwise reinterpret `BatchId`. The worker checks `IBatchRepository.IsCancelledAsync(message.BatchId)` at `src/Naravel.Queue/Worker/QueueWorkerService.cs:224-234` and acknowledges a cancelled batch member without executing the job. The batch repositories refuse to increment terminal batch counts after the batch is finished.
- Affected contract: `FailedJobManager.RetryAsync` / `RetryAllAsync` for failed envelopes that have a `BatchId`.
- User impact: a failed member of a fail-fast batch normally marks the batch cancelled. Retrying that failed record republishes the old `BatchId`; the worker sees the cancelled batch and acknowledges the retry without invoking the job. When `AllowFailures` is true and the batch is already terminal but not cancelled, the retry can run, but the finished batch ignores the later completion count. The API does not disclose either outcome.
- Owner decision required:
  - A. A failed-job retry is a new, independent queue operation: clear `BatchId` (and document that the terminal batch is historical and is not reopened).
  - B. A failed-job retry reopens/reconciles the original batch: define count rollback, cancellation reset, callback state, concurrent retry and durable multi-process semantics.
- Architect recommendation: A. It keeps terminal batch history immutable and makes the user's explicit retry request executable. A caller that wants batch semantics again can dispatch a new batch.
- Required proof after the decision: retry a failed member of a cancelled batch; retry a failed member of an AllowFailures batch; assert execution, original batch state, callback counts, and the new envelope's BatchId according to the chosen contract.
- Public API / PDR / release impact: behavior clarification for an existing retry API; owner decision AD-010 required.

### [OBS] QUE-003 — In-memory batch callback failures are not retried although the guide states that they remain pending

- Remediation state: OPEN — DOCUMENTATION / CONTRACT CLARIFICATION.
- Evidence status: CONFIRMED BY SOURCE.
- Evidence: `src/Naravel.Queue/Batching/QueueBatch.cs:277-294` increments the in-memory completion count and then invokes `OnCompleted`. If the callback throws, the batch is already finished; a later `MarkJobCompletedAsync` returns early because `IsFinished` is true. `InMemoryBatchRepository.ResumePendingCallbacksAsync` at lines 338-342 is a no-op. The guide at `docs/en/queue.md:237-257` says without qualification that a callback failure leaves completion pending so the next worker startup can retry it. Persistent Database/Redis repositories keep callback completion flags; the default in-memory repository does not.
- Affected contract: callback retry/recovery semantics when the default `InMemoryBatchRepository` is used.
- User impact: with the default repository, an `OnCompleted` callback that throws is logged by the worker but is never invoked again by a later worker startup. The job itself is correctly not rerun, but the callback can be permanently lost.
- Recommended correction: make the documentation explicit that callback progress recovery is provided by the persistent Database/Redis implementations only, while in-memory callbacks are best-effort for the current process; or implement equivalent in-process pending callback tracking/retry for the default repository. Do not claim restart recovery for in-memory state.
- Regression tests required: inject a one-time callback exception with the in-memory repository and with each persistent repository; assert the exact retry/restart guarantees and prove that a callback failure never re-enqueues the already-acknowledged job.
- Public API / PDR / release impact: clarify the documented contract; no public API change is assumed unless the owner wants retry state in the in-memory repository.

### [OBS — OWNER DECISION] QUE-004 — Empty batches are accepted but have no deterministic completion behavior

- Remediation state: OPEN — OWNER DECISION.
- Evidence status: CONFIRMED BY SOURCE; behavior reproduction NOT RUN.
- Evidence: `JobDispatcher.BatchAsync` accepts an empty job list and registers `QueueBatch.TotalJobs = 0`. It publishes no messages, so no worker calls a terminal batch transition. `QueueBatch.IsFinished` evaluates true for zero jobs, but the in-memory repository does not invoke completion callbacks during registration and its resume method is a no-op. Persistent repositories may process a finished record only on a later worker startup, so callback timing differs by provider.
- Affected contract: `BatchAsync(Array.Empty<IJob>())`.
- Owner decision required: reject an empty batch immediately with `ArgumentException`, or define it as immediately completed and run the terminal callbacks as part of registration.
- Architect recommendation: reject empty batches. It prevents a no-work request from creating persistent records and avoids provider-dependent callback timing.
- Status: WAITING FOR OWNER (AD-011).

### [OBS] QUE-005 — Queue runtime has direct wall-clock reads and delays instead of an injected TimeProvider

- Remediation state: OPEN — SCOPE / API DECISION.
- Evidence status: CONFIRMED BY SOURCE SEARCH.
- Evidence: `QueuedMessage.CreatedAt/AvailableAt` defaults use `DateTimeOffset.UtcNow` (`src/Naravel.Queue/Drivers/QueuedMessage.cs:35-38`); dispatch availability uses it (`src/Naravel.Queue/Dispatch/DispatchOptions.cs:38-41`); worker start/MaxRuntime use it (`src/Naravel.Queue/Worker/QueueWorkerService.cs:74,169`); failed-job retry sets it (`src/Naravel.Queue/Failed/FailedJobManager.cs:84`); Kafka polling compares `DateTime.UtcNow` and `Task.Delay` (`src/Naravel.Queue.Kafka/KafkaQueueDriver.cs:91-99`). The repository rule in `AGENTS.md` requires injected `TimeProvider` for application/runtime wall-clock decisions and deterministic clock tests; elapsed processing duration already uses monotonic `Stopwatch`.
- Affected behavior: delayed dispatch timestamps, retry timestamps, worker MaxRuntime and Kafka poll deadlines.
- Recommended correction: define a single clock-injection boundary for Queue and its providers. Use `TimeProvider.GetUtcNow()` for wall-clock timestamps and `TimeProvider.GetTimestamp()` / elapsed-time APIs for durations; route cancellable poll/rest delays through the provider. Preserve old-envelope defaults and public construction compatibility.
- Owner decision required: choose how the provider is registered and passed to dispatcher, worker, failed-job manager, queued-message creation and provider drivers; this is a cross-project API/DI decision (AD-012).
- Public API / PDR / release impact: potential additive constructors and DI registration; do not mechanically replace calls without an approved design.

## Existing guarantees and verified coverage

- Job aliases are explicit; unknown/assembly-qualified names are rejected before deserialization. Covered by `JobTypeRegistryTests` and worker tests.
- Source-generated JSON metadata is available for job payloads; envelope, batch callback and chain-link metadata are source-generated. Reflection paths are annotated at registration boundaries.
- Worker failure paths include pre-execution failures and settlement failures; `WorkerPreExecutionFailureTests` checks release/ack/failed-store failure and shutdown hand-back paths.
- Tests cover successful batch completion, cancellation, AllowFailures, a throwing callback without rerunning the acknowledged job, SQLite batch state across repository recreation, and Redis repository state across recreation.
- The telemetry contract uses a fixed meter/source and current core instruments have no job-ID or payload dimensions. Worker Activity tags include queue and connection names; their cardinality still depends on application configuration.

## Test assessment

- Reviewed: `WorkerTests`, `WorkerPreExecutionFailureTests`, `WorkerStartupValidationTests`, `WorkerDelayedDeliveryTests`, `QueueTelemetryTests`, `JobTypeRegistryTests`, `QueueEnvelopeJsonTests`, `QueueProviderContractTests`, fake tests, relevant Database/Redis provider sources and docs.
- Existing test gaps: Nth-publish failure during a batch; retrying a failed batch member; callback failure recovery using the default in-memory repository; empty batch behavior; deterministic Queue clock boundaries.
- Tests run for this review: NONE.
- Build/CI: NOT RUN.
- Live services: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Owner decision gate

See [OWNER-DECISIONS.md](../OWNER-DECISIONS.md): AD-009 through AD-012. No implementation, public API change, dependency change or roadmap-stage change is authorized by this source review.

