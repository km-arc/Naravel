# Review Report — Naravel.Queue.Coordination

## Review metadata

- Package ID: `Naravel.Queue.Coordination` (explicit `PackageId` in `src/Naravel.Queue.Coordination/Naravel.Queue.Coordination.csproj`).
- Source project: `src/Naravel.Queue.Coordination/Naravel.Queue.Coordination.csproj`.
- Source-review baseline: `main` at `44e957ca3db6d9f15665fc8fa070f70522c163c9`; rechecked against current `main` at `c6a7d58ffb84d885741998bb32d2082c54c4ab61`. Coordination source and project metadata are unchanged across that range; the current Queue Coordination guide and its example tests were included in this pass.
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source and contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; runtime verification pending).
- Files inspected: project metadata and references; all three middleware types; `CacheUniqueJobGate`; coordination key naming; DI registration and startup warning; public API baseline; unique-job and middleware tests; Queue and Queue Coordination guides; PDR-012b; Cache lock/store/rate-limiter contracts and implementations; Queue worker unique-key release flow.
- Dependency boundary: this is an optional adapter referencing `Naravel.Queue` and `Naravel.Cache`; Queue core does not reference this package. No new package dependency was introduced by this review.
- Verification performed: source-level race analysis of unique-key owner association/release; middleware-to-worker control-flow tracing; documentation/PDR contract comparison; test-coverage mapping.
- Verification NOT RUN: `.NET restore/build/test`, live Redis multi-worker tests, injected race reproduction, full CI, mutation testing and independent review.

## Executive assessment

- The optional package boundary is sound: it keeps Cache out of Queue core and registers the Queue unique gate through `IUniqueJobGate`.
- `WithoutOverlapping` uses token-owned Cache locks and releases them in `finally`; `RateLimited` delegates admission to the shared Cache rate limiter; `ThrottlesExceptions` uses expiring Cache counters and an open-circuit key.
- Memory-vs-shared coordination limits are documented. Startup registration fails clearly when Cache is missing; a warning is emitted for the detected Memory-cache/non-Memory-default-queue combination.
- The unique-job adapter has a race in its separate owner-metadata lifecycle: a stale releaser can remove a newer dispatch's owner record after the lock has been released and reacquired. A delayed owner write can also overwrite a newer owner record after its lease has expired. The lock token still prevents an old owner from releasing a newer lock, but the owner-record race can prevent the current owner from releasing its lock, leaving uniqueness unavailable until TTL expiry.
- No runtime code, tests, public API, package references or dependencies were changed by this review.

## Findings

### [P2] QCO-001 — Unique-job owner metadata is not atomically bound to the lock lease

- Remediation state: OPEN.
- Architecture disposition: VETO AV-013 — owner metadata must be atomically associated with the current lock lease, or updated/removed through a compare-and-set operation that cannot mutate a newer owner's record.
- Evidence status: CONFIRMED BY SOURCE-LEVEL INTERLEAVING ANALYSIS; deterministic concurrency reproduction NOT RUN.
- Evidence: `src/Naravel.Queue.Coordination/CacheUniqueJobGate.cs` acquires the lock at `TryAcquireAsync` and then writes `owner|token` as a separate `ICacheStore.SetAsync` operation with the same TTL. `ReleaseAsync` reads the owner record, validates the message id, calls token-checked `ICacheLock.ReleaseAsync`, ignores its boolean result, and then unconditionally calls `ICacheStore.RemoveAsync(ownerKey)`. `ICacheStore` has no compare-and-delete or compare-and-set operation. The lock release is token-fenced, but the owner metadata mutation is not.
- Affected contract: `IUniqueJobGate.TryAcquireAsync` and `ReleaseAsync`, used by `IUniqueJob` dispatch, worker completion, permanent failure, `Skip`, `UniqueUntilProcessing`, and failed-push cleanup.
- User impact: a stale completion can delete a newer dispatch's owner metadata. The newer dispatch still owns the Cache lock, so duplicate admission is not caused by this interleaving; instead, its eventual release cannot verify ownership and the key remains blocked until the lock TTL expires. A delayed owner write can similarly overwrite a newer owner's metadata and strand the current lease until TTL expiry.
- Trigger / failure scenario A (release race): owner A reads its owner record; A successfully releases its lock; owner B immediately acquires the now-free lock and stores `B|tokenB`; A then executes the unconditional `RemoveAsync(ownerKey)`, deleting B's metadata. When B completes, it cannot verify its ownership and therefore cannot release its token.
- Trigger / failure scenario B (late write): A acquires a short lease and begins the separate owner write; A's lease expires while that write is delayed; B acquires the lock and stores `B|tokenB`; A's delayed write completes afterwards and overwrites B's metadata. B can no longer release its lock through the gate.
- Why existing tests do not catch it: `UniqueJobTests` covers duplicate rejection, release after success/permanent failure, retries, `UniqueUntilProcessing`, and an env-gated two-host Redis path. It does not force the interleavings between owner read, lock release, owner write and owner removal. The tests exercise the gate through a worker; no deterministic gate-level race test is present.
- Required correction: make lock acquisition and owner association one atomic backend operation, or provide a token/owner-aware compare-and-set/delete primitive whose semantics are guaranteed by every supported backend. A stale owner must never overwrite or delete a newer lease's metadata. Do not rely on the result of a separate read as authorization for an unconditional delete. Preserve the current Queue/Cache package boundary.
- Regression tests required: deterministic controlled interleaving for A-release/B-acquire/A-cleanup; delayed A-owner-write after lease expiry and B-acquire; stale A release while B owns the key; successful release of B; verify that stale operations neither remove nor overwrite B's metadata. Run provider-specific tests for Memory and Redis; document guarantees for any additional provider.
- Public API / PDR / release impact: likely a Cache lock/gate capability or backend-specific atomic operation. Owner decision AD-021 is required before selecting the abstraction and supported-provider guarantee.

## Existing guarantees and coverage

- `WithoutOverlapping` releases its lock in a `finally` block and supports release-vs-skip behavior.
- `RateLimited` uses the Cache rate limiter and documents its fixed-window boundary caveat.
- `ThrottlesExceptions` excludes `OperationCanceledException` from its exception counter, opens a TTL-bounded circuit after the configured threshold, and resets the counter after success.
- Queue middleware order and short-circuit outcomes are handled by Queue core; this package does not reimplement worker settlement.
- Tests cover Memory-backed worker flows and contain env-gated Redis tests with two hosts. Those tests were not run in this review.
- No dedicated deterministic test covers the owner-metadata interleavings described in QCO-001.

## Test assessment

- Reviewed: `CoordinationMiddlewareTests`, `UniqueJobTests`, `DocsExampleTests`, `CacheUniqueJobGate`, `ICacheLock`, `ICacheStore`, `CacheRateLimiter`, `QueueWorkerService`, Queue guide, Queue Coordination guide and PDR-012b.
- Tests run for this review: NONE.
- Build/CI: NOT RUN.
- Live Redis tests: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Owner decision gate

See [OWNER-DECISIONS.md](../OWNER-DECISIONS.md), AD-021. No runtime implementation, Cache public API change, dependency change or roadmap-stage change is authorized by this source review.

## Plain-language explanation

### QCO-001 — An old unique-job owner can erase the newer owner's information

- **In everyday words:** The package stores the lock and the identity of the owner in two separate operations. An old job can remove or replace the newer job's owner record after the newer job has taken the lock.
- **A realistic example:** Job A reads its owner record and releases its lock. Job B immediately takes the now-free lock and writes its owner record. A then runs its separate delete and removes B's record. When B finishes, it can no longer prove that it owns the lock, so the lock may remain blocked until its timeout expires.
- **What we checked:** `src/Naravel.Queue.Coordination/CacheUniqueJobGate.cs`, `TryAcquireAsync` and `ReleaseAsync`. The code writes `owner|token` after acquiring the lock, reads the record later, releases the token-checked lock, then unconditionally removes the owner key.
- **Why that proves the risk:** The lock check protects the lock itself, but the separate owner-record write/delete is not conditional on still owning the current lock.
- **What is not yet proven here:** The interleaving was analysed from source. The report does not claim a deterministic race test or live Redis reproduction.
- **Safe outcome and proof:** A stale job must never overwrite or delete the current owner's information. Make the lock and owner record one atomic operation, or use a guaranteed owner-aware compare-and-set/delete. Test A-release/B-acquire/A-cleanup and A-delayed-write/B-acquire, and prove B can still release its own lock. Owner decision AD-021 is required before choosing the Cache/Queue boundary.

### Overall plain-language status

This report contains one open P2 finding. It is a source-level concurrency analysis; no deterministic concurrency reproduction or live provider test was run in this review.
