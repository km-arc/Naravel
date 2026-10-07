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

A first delivery has `Attempts == 1`. Every reclaimed reservation increments `Attempts` before redelivery. Contract tests must cover the first delivery and repeated reclaim boundaries. After `MaxAttempts` deliveries/reclaims, the worker routes the job to permanent failure handling instead of retrying indefinitely.

Add nullable virtual `Job.Timeout`; null uses the worker default. The effective execution timeout is the smaller non-null value of job and worker timeout. A timeout cancels the job token and follows the normal attempt/failure policy; it must not silently acknowledge a job as successful. At startup, fail configuration when a store's visibility timeout is below the worker's default timeout. Also warn when visibility timeout is below the maximum configured job timeout plus a configurable safety margin. Do not promise cancellation of user code that ignores its token.

### Persistent batches

Add `IBatchRepository` with Database and Redis implementations. Persist batch identity, pending/succeeded/failed counts, `AllowFailures`, cancellation state, and callback execution state. `CancelAsync` prevents not-yet-started batch jobs from running; already running jobs are not force-aborted. Without `AllowFailures`, the first failure prevents remaining undispatched jobs and marks the batch failed; with it, remaining jobs continue and the batch is terminal when all have settled.

Database schema changes are never applied automatically at startup or service registration. Ship SQL scripts for the queue tables and an opt-in schema helper restricted to development/test use; production schema changes remain application-managed.

Persist callback aliases and typed payloads, not delegates. Register typed then/catch/finally callbacks in an explicit alias registry; no type-name lookup or reflection from stored data. Persist callback progress before/after invocation so recovery is possible, and document callbacks as at-least-once across a process crash; callback implementations must be idempotent. Tests must recreate the repository and prove state survives.

### Worker controls

Add `StopWhenEmpty`, `MaxJobs`, `MaxRuntime`, and `Rest` to worker options. `StopWhenEmpty` exits after an empty poll; `MaxJobs` bounds completed reservations per run; `MaxRuntime` stops taking new work once elapsed while allowing the active job its configured cancellation/timeout behavior; `Rest` is the delay after an empty poll before polling again. Defaults preserve current continuous-worker behavior. `CancellationToken` remains the immediate host-shutdown signal.

### RabbitMQ, observability, and fake

Migrate the RabbitMQ provider to RabbitMQ.Client 7.x async APIs, remove the single-channel lock and all sync-over-async, and preserve ack/release/fail semantics. Check current supported 7.x package metadata before editing `Directory.Packages.props`; do not add another dependency.

Use `Meter` named `Naravel.Queue` with processed, failed and retried counters plus a processing-duration histogram. Use `ActivitySource` named `Naravel.Queue`; add nullable `traceparent` and `tracestate` fields to the message envelope without changing or removing existing fields, then restore them when processing. Envelopes written before these fields existed must still deserialize; pin that compatibility with a pre-change envelope regression test. Metric tags must remain low-cardinality and exclude job IDs and payload data.

Add `Naravel.Queue.Testing` with a Fake supporting `AssertDispatched<T>` and `AssertChained`. It must exercise the same public dispatch contract without starting a broker or worker.

## Dependencies and boundaries

No new dependency is approved beyond the RabbitMQ.Client 7.x migration already authorized by OD-02. Keep provider projects optional and package versions centralized. Do not implement R08 middleware, R18 encryption, events/mail adapters, custom cryptography, reflection-based job activation, or a replacement for `BackgroundService`.

## Verification required after approval

Add focused Memory tests for worker controls, timeout cancellation and one-based attempt exhaustion; a pre-change envelope deserialization regression test; SQLite tests for failed-job retry and batch persistence; SQL scripts and opt-in development/test schema-helper tests; Redis tests for persistent stores; and the existing env-gated R01 RabbitMQ contract suite for the async migration. Verify that no `.GetAwaiter().GetResult()` remains in the RabbitMQ project. Add MeterListener and fake assertions, extend queue benchmarks, update EN/FA queue docs, parity tables, changelog, and the stage checklist. Run the full solution verification and `roadmap/check.py`. Report skipped external services as NOT RUN, not as passing.

## Alternatives considered

1. **Use hosted services alone for failed jobs and batches.** Rejected for these operations: hosting does not persist failure records, retry intent, or batch state across process restarts.
2. **Store CLR type names or delegates in serialized records.** Rejected by OD-07 and because delegates/type resolution are unsafe and brittle across deployments; use explicit aliases and typed callback registries.
3. **Retry a failed job on the current default connection.** Rejected; it can change routing after configuration changes. Preserve the original connection and queue from the envelope.
4. **Treat timeout as proof that user code stopped.** Rejected; cancellation is cooperative. Report timeout and apply queue retry/failure rules without claiming forced termination.
5. **Keep RabbitMQ.Client 6.x synchronous APIs behind async wrappers.** Rejected; it preserves blocking and channel-lock problems instead of completing the approved async migration.
6. **Exactly-once callback execution across crashes.** Rejected as an unsupported guarantee without a transaction shared by callback side effects and the repository. Persist progress and specify at-least-once/idempotent callbacks.

## Approval

Owner approved this proposal on 2026-10-06. The failed-record delivery semantics, one-based attempt count, timeout/visibility policy, batch failure and callback behavior, worker-control defaults, metric contract, fake API, and quickstart are approved for R07 implementation. The owner-approved clarifications are: startup error when visibility timeout is below the worker default; warning when it is below maximum job timeout plus margin; additive nullable trace fields with a pre-change-envelope regression test; and no automatic DDL, with SQL scripts and an opt-in development/test schema helper.

## Owner decisions — implementation record

This section records the implementation and tests found in the repository. It does not add or imply owner approval beyond the approval stated above.

a. **NOT IMPLEMENTED across all drivers.** `JobContext.Attempt` presents the initial attempt as 1, and Database, File, and Redis increment stored attempts on expired reservation reclaim. Kafka and RabbitMQ broker redelivery do not persist an increment to the envelope, and there is no test proving one-based attempt values on broker redelivery. Evidence: `src/Naravel.Queue/Jobs/JobContext.cs:25`, `src/Naravel.Queue/Worker/QueueWorkerService.cs:305`, `src/Naravel.Queue.Database/DatabaseQueueDriver.cs:45`, `src/Naravel.Queue.File/FileQueueDriver.cs:132`, `src/Naravel.Queue.Redis/RedisQueueDriver.cs:74`, `src/Naravel.Queue.Kafka/KafkaQueueDriver.cs:93`, `src/Naravel.Queue.RabbitMQ/RabbitMqQueueDriver.cs:114`, `tests/Naravel.Queue.Providers.Tests/QueueProviderContractTests.cs:40`, `tests/Naravel.Queue.Tests/DriverTests.cs:168`.
b. **NOT IMPLEMENTED as approved.** The worker logs a warning for a short visibility timeout against its default/effective job timeout; it does not fail startup when visibility is below the worker default, nor calculate the maximum configured job timeout plus margin. Evidence: `src/Naravel.Queue/Worker/QueueWorkerService.cs:87`, `src/Naravel.Queue/Worker/QueueWorkerService.cs:343`; no corresponding validation test was found.
c. **IMPLEMENTED.** Trace fields are nullable/additive, and the worker restores trace context. `Old_chain_envelope_without_connection_continues_on_the_worker_connection` processes an envelope serialized without nullable fields, including the trace fields; it does not explicitly assert their absence. Evidence: `src/Naravel.Queue/Drivers/QueuedMessage.cs:49`, `src/Naravel.Queue/Drivers/QueuedMessage.cs:52`, `src/Naravel.Queue/Worker/QueueWorkerService.cs:157`, `tests/Naravel.Queue.Tests/WorkerTests.cs:229`.
d. **IMPLEMENTED policy; SQL-script deliverable NOT IMPLEMENTED.** The Database provider supplies EF model mappings, documents that the application creates/applies migrations, and does not create schema automatically. No checked-in SQL scripts or provider migration files were found. Evidence: `src/Naravel.Queue.Database/QueueModelBuilderExtensions.cs:13`, `docs/en/queue.md:224`, `docs/en/queue.md:231`, `docs/en/queue.md:236`.
e. **NOT IMPLEMENTED; D-21 remains open.** Kafka `PopAsync` still calls synchronous `IConsumer.Consume` with a 200 ms poll timeout. The limitation is recorded in `roadmap/AUDIT.md:26`; `docs/en/queue.md` does not mention the blocking poll. Evidence: `src/Naravel.Queue.Kafka/KafkaQueueDriver.cs:93`.
f. **IMPLEMENTED package bump; behavior/test coverage NOT IMPLEMENTED.** `MessagePack` is 3.1.7 centrally; the changelog says the bump from 2.5.301 was required by `EnyimMemcachedCore` 3.5.1. No C# source usage or tests of MessagePack were found; restore/build can validate package resolution, not serializer behavior. Evidence: `Directory.Packages.props:23`, `CHANGELOG.md:55`.
