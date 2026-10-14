# PDR-012b — Queue coordination middleware (R08.T01)

**Native-first verdict:** .NET has no job-level coordination primitive: `System.Threading.RateLimiting` is in-process, `SemaphoreSlim`/`lock` do not cross processes, and broker acknowledgements say nothing about *which* jobs may run together. Cross-process coordination therefore needs a shared store, which Naravel already has: `ICacheLock` and `IRateLimiter` from PDR-007/007a (R04). Naravel should add only the thin job-level policy on top of them, not a second locking or limiting engine.

Status: **APPROVED 2026-10-13.** Owner pre-decisions given in conversation on 2026-10-13: the module boundary is a **separate adapter project**, and **debounce is moved to `roadmap/BACKLOG.md`** (out of R08).

## Context

R07 delivered the worker, retries, failed jobs and `IJobMiddleware`. Laravel's coordination features are `ShouldBeUnique`/`ShouldBeUniqueUntilProcessing`, `WithoutOverlapping`, `RateLimited` and `ThrottlesExceptions`. R04 delivered the atomic building blocks.

Verified facts in the current code (`QueueWorkerService.cs`, `IJobMiddleware.cs`, `IJob.cs`):

1. `IJobMiddleware` is registered **globally** in DI and wraps **every** job. There is no per-job middleware list.
2. A middleware that does not call `next` makes the worker treat the job as **successful**: the chain continues, `AckAsync` runs and `Processed` is counted. There is no way for middleware to say "release this job later" or "drop it". Without a fix, `WithoutOverlapping` and `RateLimited` would silently lose jobs.
3. `ICacheLock.AcquireAsync(name, ttl)` returns an owner token or null; `ReleaseAsync(name, token)` is token-fenced. `IRateLimiter.AttemptAsync(limiter, subject, max, window)` is a fixed window that may admit up to 2x at a boundary (PDR-007a).
4. The memory cache store's lock is single-process.

## Decision

### 1. Module boundary: `Naravel.Queue.Coordination`

New optional project `src/Naravel.Queue.Coordination`. It references `Naravel.Queue` and `Naravel.Cache`. `Naravel.Queue` itself gains **no** Cache reference (rule 3 of AGENTS.md: dependencies point one way; Queue-only users do not pull Cache). Registration: `services.AddQueueCoordination()`; it needs a registered Cache lock/limiter store and fails at startup with a clear message when none exists. Tests live in `tests/Naravel.Queue.Coordination.Tests`; the project is added to `Naravel.slnx`.

### 2. Small core change in `Naravel.Queue` (needed by every middleware)

- **Per-job middleware.** Add `IHasJobMiddleware` (name subject to API review) with `IEnumerable<IJobMiddleware> Middleware(JobContext context)`. A job type that implements it gets those middleware **inside** the global ones. Middleware instances are created by the job's code, so no reflection or type lookup is involved.
- **Explicit outcomes.** Add `JobContext.Release(TimeSpan delay)` and `JobContext.Skip()`. If a middleware calls either and does not call `next`, the worker honors it: *Release* calls `driver.ReleaseAsync(message, delay)` **without** counting an attempt, runs no chain and no success telemetry; *Skip* acknowledges the message but counts it as `skipped` (new low-cardinality counter tag), runs no chain and no `Processed` count. A middleware that neither calls `next` nor sets an outcome is a programming error: the worker fails the job with a clear exception rather than treating it as success (today's behavior). This is a behavior change for custom middleware that already short-circuits; it is called out in CHANGELOG as pre-1.0 breaking.
- Drivers that cannot delay (Kafka, PDR-012a) release with no delay; the existing capability warning covers this.

### 3. Middleware semantics (all in `Naravel.Queue.Coordination`)

| Middleware | Key | Behavior |
|---|---|---|
| `WithoutOverlapping(key)` | `key` (required, caller-supplied) | At run time: `AcquireAsync(key, lockTtl)`. Not acquired: `Release(releaseAfter)` (default) or `Skip()` (`DontRelease`). Always release the lock with its token in `finally`, even when the job throws. `lockTtl` defaults to the job's effective timeout plus a margin so a crashed worker cannot hold it forever. |
| `UniqueJob(key, lockTtl)` | `key` | Same lock, but acquired at **dispatch** by a dispatch hook as well as checked at run time: a second dispatch while the first is pending or running is **rejected** (returns the existing job id or a documented `null`; exact shape for API review). Default scope is *until processed* (lock released after the job finishes or permanently fails); `UntilProcessing` releases when the job starts. The TTL is the safety net, not the primary release. |
| `RateLimited(limiter, max, window, subject)` | `limiter` + `subject` | `IRateLimiter.AttemptAsync`; when not allowed, `Release(retryAfter)` with the decision's retry-after. Inherits PDR-007a's fixed-window caveat and documents it. A rate-limited release does **not** consume an attempt. |
| `ThrottlesExceptions(maxExceptions, window, backoff)` | per job type (default) or caller key | Counts consecutive exceptions in a shared counter. After the limit it releases subsequent jobs for `backoff` without running them (circuit-open) instead of failing them; a success resets the counter. Exceptions are rethrown to the normal retry policy while under the limit. |

All keys are caller-supplied strings, namespaced with a `naravel:queue:` prefix. Nothing is derived from a CLR type name stored in data (OD-07).

### 4. Honest guarantees

- With a **Memory** lock or limiter, coordination holds **within one process only**. Docs (EN/FA) and the registration path say this plainly; `AddQueueCoordination()` logs a warning when the resolved store is the memory driver and the queue connection is not Memory.
- With **Redis** (or another shared store) the lock is cross-process. The lock TTL is a bound, not a guarantee: a job running longer than `lockTtl` loses mutual exclusion. The token-fenced release prevents a late finisher from freeing someone else's lock.
- Lock acquisition and the job are not one transaction: a crash after acquire leaves the lock until TTL. This is stated, not hidden.
- Uniqueness is **best effort at dispatch** and **fenced at run time**; it is not exactly-once.

### 5. Observability and fake

The worker's existing `Naravel.Queue` meter gains a low-cardinality `outcome` tag value `released`/`skipped` (no job ids, no keys). `Naravel.Queue.Testing` Fake gains `AssertNotDispatched` for rejected unique dispatches. Middleware are unit-testable with a fake `ICacheLock`.

## Out of scope

Debounce (moved to BACKLOG), distributed semaphores with N permits, a new lock/limiter engine, Cache internals (R04), Redis topology (R03), encryption (R18).

## Alternatives considered

1. **Reference Cache directly from Naravel.Queue.** Rejected by owner: forces Cache on every Queue user.
2. **Keep global-only middleware.** Rejected: unique/overlap keys are per job; a global middleware would need type lookups or attributes read by reflection.
3. **Let short-circuiting middleware mean success (today).** Rejected: silently loses released or rate-limited jobs.
4. **Throw a special exception to release.** Rejected: exceptions as control flow cost allocations on the hot path and collide with the retry policy; explicit outcomes are cheaper and clearer.
5. **Roll our own lock inside Queue.** Rejected: duplicates R04 (AGENTS.md hard rule 2 spirit).
6. **Promise cross-process uniqueness from memory locks.** Rejected by the roadmap decision for R08.

## Verification required after approval

Regression test first for the core change: a middleware that returns without `next` and without an outcome fails the job; `Release` does not count an attempt and runs no chain; `Skip` acks without `Processed`. Unit tests per middleware with the Memory lock (including lock release when the job throws, TTL expiry, release-after delay). **Cross-process** uniqueness and overlap proven by an env-gated Redis test with two workers (`NARAVEL_TEST_REDIS`) added to the CI services job. Public API entries, EN/FA docs, parity table, CHANGELOG, stage checklist, Fake support, `roadmap/check.py`; report anything not run as NOT RUN.

## Approval

Owner approved on 2026-10-13 with these answers to the open questions:

- **(a) Rejected unique dispatch returns `null`.** `IJobDispatcher.DispatchAsync` becomes `Task<string?>`; `null` means the dispatch was rejected as a duplicate. Implemented with `IUniqueJob` (2026-10-14).
- **(b) The short-circuit behavior change is accepted as pre-1.0 breaking.** A middleware that neither calls `next` nor sets an outcome fails the job. The core change (section 2) was already merged (PR #31); it is listed in `CHANGELOG.md`.

## Implementation record

- 2026-10-13: `Naravel.Queue.Coordination` with `WithoutOverlapping`, `RateLimited`, `ThrottlesExceptions` and `AddQueueCoordination()`; tests in `tests/Naravel.Queue.Coordination.Tests` (Memory through a real worker, plus an env-gated Redis test with two workers); EN/FA `docs/*/queue-coordination.md`. Tests were NOT RUN locally (no .NET SDK); CI is the verification.
- Deviations from section 3, all small: the default lock lifetime is a fixed 15 minutes because middleware cannot see the job's timeout (set `ExpireAfter` above the longest run); `AddQueueCoordination()` fails at startup when Cache is missing but does not yet warn about a Memory store combined with a non-Memory queue connection (the docs state the limit).
- 2026-10-14: unique jobs. Deviations from section 3 and the reasons:
  - **`IUniqueJob` (an interface on the job) instead of a `UniqueJob` middleware.** Uniqueness must be decided at dispatch, before a message exists, and middleware only runs on a worker. The job declares `UniqueKey`, `UniqueFor` (default 1 hour) and `UniqueUntilProcessing`; the dispatcher enforces it.
  - **`IUniqueJobGate` in `Naravel.Queue`, implemented by `CacheUniqueJobGate` in `Naravel.Queue.Coordination`.** Keeps Queue free of a Cache reference (section 1). `JobDispatcher` takes the gate as an optional constructor argument; a unique job dispatched without a gate throws, naming `AddQueueCoordination()`.
  - **The lock owner is the message id.** The worker that releases the key knows the message id but not the Cache lock token, and adding the token to `QueuedMessage` would need a new column in every Database queue table. The gate stores `messageId|token` beside the lock and re-checks it on release (fenced: a stale worker cannot free a newer dispatch's key).
  - **Release points:** success, permanent failure, `Skip`, and job start for `UniqueUntilProcessing`. A retry or a `Release` keeps the key; a failed push releases it at once. The release never throws on the success path (it runs after the acknowledgement).
  - **Chains and batches reject unique jobs** (`ArgumentException`): a rejected link would stall a chain and a rejected batch member would leave the batch incomplete. This is stricter than section 3, which did not say.
  - `IJobDispatcher.DispatchAsync` / `QueueFake.DispatchAsync` now return `Task<string?>` (decision (a)); `DispatchChainAsync` and `JobChain.DispatchAsync` still return `Task<string>`. `QueueFake` mimics the gate and gains `AssertNotDispatched` and `AssertDispatchedCount`.
  - Tests: `tests/Naravel.Queue.Coordination.Tests/UniqueJobTests.cs` (Memory through a real worker: rejection and re-dispatch, different keys, `UntilProcessing`, key held during a retry, key freed after permanent failure, missing gate, chain/batch, fake; env-gated Redis test with two hosts). Docs: unique-jobs section and a "why it is built this way" table in EN/FA `queue-coordination.md`, a note in `queue.md`. NOT RUN locally (no .NET SDK); CI is the verification.
- Already present, so not repeated: the `outcome` meter tag (`released`/`skipped`) shipped with the core change in PR #31.
- Still open (R08.T03): the Memory-store warning in `AddQueueCoordination()`, the parity table and the final verification.
