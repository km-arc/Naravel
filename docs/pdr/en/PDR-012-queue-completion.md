# PDR-012 — Queue completion A

**Native-first verdict:** .NET hosted services, cancellation, channels, and provider-native acknowledgements are the baseline. Naravel should add only durable queue operations and consistent cross-driver behavior that these primitives do not provide; it should not replace the native hosting model.

Status: **Approved 2026-10-06.** RabbitMQ.Client 7.x async migration is pre-approved by OD-02 for R07 only; verify the exact package version before changing central package management.

## Context

R07 completes operational gaps in the existing Naravel.Queue implementation: permanent-failure handling, reclaim attempt accounting and timeouts, persistent batches, worker controls, RabbitMQ's async API, observability, and a testing fake. The design must preserve R00's explicit job-alias registry and the Manager/driver ownership recorded in PDR-006. Queue middleware for uniqueness, overlap and rate limiting remains R08; encrypted jobs remain R18.

Laravel surface: failed-job provider and retry/forget operations, batches, worker controls, and queue job attempt/timeout behavior. Native baseline: `BackgroundService`, `CancellationToken`, `Activity`/`Meter`, and each broker's acknowledgement and visibility primitives.

## Proposed decision

### Ergonomic registration and dispatch

Keep the existing manager/driver model and expose one additive builder entry point rather than another resolver:

```csharp
services.AddNaravelQueue(configuration, queue => queue.AddRedis());
services.AddNaravelQueueWorker();
await dispatcher.DispatchAsync(new SendInvoice(invoiceId), cancellationToken);
```

The exact builder and dispatcher names require API review against the current registration surface before implementation. The common registration-and-dispatch path must remain at no more than three user-code lines; existing provider-specific registration must continue to work during migration or be documented as a pre-1.0 breaking change.

### Failed jobs

Introduce `IFailedJobStore` with persistent Database and Redis implementations and an in-memory implementation for tests. A terminal job failure is recorded with an idempotent failure ID, the original serialized envelope, alias, connection, queue, attempts, and failure details. Use the R00 alias registry; never resolve a CLR type from stored payload data. Record before acknowledging/removing the source reservation, accepting at-least-once behavior across the non-transactional broker/store boundary.

Provide `RetryAsync(id)`, `RetryAllAsync`, `ForgetAsync(id)`, and `FlushAsync`. Retry republishes to the original connection and queue using the R00 envelope and removes the failed record only after successful publication. `RetryAllAsync` processes records independently; partial success is reported and a failed publication leaves its record available. Forget removes one record; Flush removes all failed records but does not dispatch them. All storage and dispatch APIs are async and cancellable.

### Attempts and timeouts

A reservation reclaimed after its visibility/lease timeout increments `Attempts` before it is delivered again. Initial delivery retains the existing R00 attempt convention; the contract tests must pin down whether the visible count is one-based. After `MaxAttempts` deliveries/reclaims, the worker routes the job to permanent failure handling instead of retrying indefinitely.

Add nullable virtual `Job.Timeout`; null uses the worker default. The effective execution timeout is the smaller non-null value of job and worker timeout. A timeout cancels the job token and follows the normal attempt/failure policy; it must not silently acknowledge a job as successful. At startup, warn when configured visibility timeout is less than the maximum relevant job/worker timeout plus a configurable safety margin. Do not promise cancellation of user code that ignores its token.

### Persistent batches

Add `IBatchRepository` with Database and Redis implementations. Persist batch identity, pending/succeeded/failed counts, `AllowFailures`, cancellation state, and callback execution state. `CancelAsync` prevents not-yet-started batch jobs from running; already running jobs are not force-aborted. Without `AllowFailures`, the first failure prevents remaining undispatched jobs and marks the batch failed; with it, remaining jobs continue and the batch is terminal when all have settled.

Persist callback aliases and typed payloads, not delegates. Register typed then/catch/finally callbacks in an explicit alias registry; no type-name lookup or reflection from stored data. Persist callback progress before/after invocation so recovery is possible, and document callbacks as at-least-once across a process crash; callback implementations must be idempotent. Tests must recreate the repository and prove state survives.

### Worker controls

Add `StopWhenEmpty`, `MaxJobs`, `MaxRuntime`, and `Rest` to worker options. `StopWhenEmpty` exits after an empty poll; `MaxJobs` bounds completed reservations per run; `MaxRuntime` stops taking new work once elapsed while allowing the active job its configured cancellation/timeout behavior; `Rest` is the delay after an empty poll before polling again. Defaults preserve current continuous-worker behavior. `CancellationToken` remains the immediate host-shutdown signal.

### RabbitMQ, observability, and fake

Migrate the RabbitMQ provider to RabbitMQ.Client 7.x async APIs, remove the single-channel lock and all sync-over-async, and preserve ack/release/fail semantics. Check current supported 7.x package metadata before editing `Directory.Packages.props`; do not add another dependency.

Use `Meter` named `Naravel.Queue` with processed, failed and retried counters plus a processing-duration histogram. Use `ActivitySource` named `Naravel.Queue`; carry W3C trace context (`traceparent`/`tracestate`) in the message envelope and restore it when processing. Metric tags must remain low-cardinality and exclude job IDs and payload data.

Add `Naravel.Queue.Testing` with a Fake supporting `AssertDispatched<T>` and `AssertChained`. It must exercise the same public dispatch contract without starting a broker or worker.

## Dependencies and boundaries

No new dependency is approved beyond the RabbitMQ.Client 7.x migration already authorized by OD-02. Keep provider projects optional and package versions centralized. Do not implement R08 middleware, R18 encryption, events/mail adapters, custom cryptography, reflection-based job activation, or a replacement for `BackgroundService`.

## Verification required after approval

Add focused Memory tests for worker controls, timeout cancellation and attempt exhaustion; SQLite tests for failed-job retry and batch persistence; Redis tests for persistent stores; and the existing env-gated R01 RabbitMQ contract suite for the async migration. Verify that no `.GetAwaiter().GetResult()` remains in the RabbitMQ project. Add MeterListener and fake assertions, extend queue benchmarks, update EN/FA queue docs, parity tables, changelog, and the stage checklist. Run the full solution verification and `roadmap/check.py`. Report skipped external services as NOT RUN, not as passing.

## Alternatives considered

1. **Use hosted services alone for failed jobs and batches.** Rejected for these operations: hosting does not persist failure records, retry intent, or batch state across process restarts.
2. **Store CLR type names or delegates in serialized records.** Rejected by OD-07 and because delegates/type resolution are unsafe and brittle across deployments; use explicit aliases and typed callback registries.
3. **Retry a failed job on the current default connection.** Rejected; it can change routing after configuration changes. Preserve the original connection and queue from the envelope.
4. **Treat timeout as proof that user code stopped.** Rejected; cancellation is cooperative. Report timeout and apply queue retry/failure rules without claiming forced termination.
5. **Keep RabbitMQ.Client 6.x synchronous APIs behind async wrappers.** Rejected; it preserves blocking and channel-lock problems instead of completing the approved async migration.
6. **Exactly-once callback execution across crashes.** Rejected as an unsupported guarantee without a transaction shared by callback side effects and the repository. Persist progress and specify at-least-once/idempotent callbacks.

## Approval

Owner approved this proposal on 2026-10-06. The failed-record delivery semantics, attempt-count convention, timeout/visibility policy, batch failure and callback behavior, worker-control defaults, metric contract, fake API, and quickstart are approved for R07 implementation.
