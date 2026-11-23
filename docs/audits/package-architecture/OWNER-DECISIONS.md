# Owner Architecture Decisions — Package Revalidation

Date: 2026-11-08  
Branch: `audit/package-architecture-revalidation`  
Baseline: `main` at `198b36f9e2753ccfb68354a02480cdc25e7eabc7`

This register separates **architectural recommendations** from **owner-approved decisions**. Nothing in this file is approved by implication. The principal-architect vetoes AV-001 through AV-019 remain active, except AV-006, which the owner superseded on 2026-11-10 (D-84); the questions below are not silently resolved by this review.

## AD-001 — Explicit zero TTL for `ICacheStore.SetAsync`

**Observed source behavior:** Memcached maps an explicit `TimeSpan.Zero` to protocol expiration zero (no expiry); `CacheFake` rejects explicit zero; the public interface documentation does not define the zero case. The shared provider contract does not test it.

**Decision required:**
- A. Explicit zero means immediate expiry, and every provider/fake must implement that behavior.
- B. Explicit zero is invalid and every provider/fake must reject it; `null` alone means no expiry.

**Architect recommendation:** B. It aligns with the existing fake and the TTL-counter overload's non-positive validation, and prevents an accidental zero from becoming a permanent Memcached entry.

**Status:** DECIDED on 2026-11-21: the owner chose option A in the working session. An explicit zero or negative TTL means the value is already expired, so nothing is stored and any value under the key is removed; only `null` never expires. The reasons given for A: it matches the `put` method of the Laravel cache repository (checked on a snapshot of `Repository.php` whose date is unknown), a lifetime worked out as "time until a deadline" ends in "not cached" instead of an exception once the deadline has passed, and for a cache the loss of an entry is acceptable while a permanent entry is not. The cost of A is that a misconfigured zero switches caching off for that key without an error. The architect recommendation of B above is kept as written; on review the architect found A at least as good, because the one coordination caller that was feared, `CacheUniqueJobGate`, already rejects a non-positive TTL itself. The `IncrementAsync` overload with a `ttl` is unchanged and still rejects a non-positive value. Implemented by PR #319 (merge `aa01b45`) for the Memory, Redis and Memcached stores and `CacheFake`; the finding MCD-011 and the row D-100 are closed in these documents.

## AD-002 — The guarantee promised by `ICacheLock`

**Observed source behavior:** `ICacheLock.AcquireAsync(name, ttl)` returns a token for a finite lease. The interface has no renewal or fencing token. Redis release is atomic token-checked, but a holder that continues executing after its lease expires is not prevented by Redis from mutating an unrelated protected resource. Memcached has the additional GET/compare/DELETE release race. `CacheRateLimiter` protects a multi-await critical section with a fixed 30-second lease.

**Decision required:**
- A. Define `ICacheLock` explicitly as a best-effort lease valid only while its TTL remains live; callers must not assume stale work is fenced. Remove or redesign consumers that need stronger correctness.
- B. Require correctness-grade distributed critical sections and add an enforceable fencing/lease capability; a protected resource must reject stale fencing tokens. Backends without that capability cannot provide that guarantee.

**Architect recommendation:** B for any cross-process critical section that protects durable/shared state. In either case, redesign `CacheRateLimiter` around one atomic backend-side state transition rather than a multi-call operation whose safety depends on a fixed lock TTL. Do not describe token-checked release alone as fencing.

**Status:** DECIDED on 2026-11-22: the owner chose a layered plan instead of option A alone (documentation only). The owner also asked for the lock to be solved for good whether or not a given caller needs a token, so fencing is built as an optional capability and no internal caller is forced to use it. The plan has five steps, each its own pull request within the size limit of `AGENTS.md`:

1. `CacheRateLimiter` stops taking a lock. Each attempt is one atomic `IncrementAsync` on the selected store, the "one atomic backend-side state transition" that the architect recommendation above asks for. A second key only records when the window began, to work out `RetryAfter`; the decision to admit or reject is the increment alone. This step adds no public API.
2. An optional lock capability to renew a lease and to release a lock by force, implemented by the Memory and Redis locks.
3. A lease handle that renews the lease by itself, has a mandatory maximum hold time, and carries a token that is cancelled when the lease is lost.
4. `WithoutOverlapping`, the scheduler locks and `BlockAsync` use the handle, so a slow holder keeps its lease.
5. An optional fencing token, Redis only, for code that needs correctness-grade exclusion. The protected resource must reject a stale token.

Memcached stays a best-effort lock: it does not implement the capabilities of steps 2, 3 and 5, and the guides say so. `ICacheLock` itself does not change, because a new member on a shipped interface would break custom drivers added with `LockManager.Extend`; the capabilities are separate optional interfaces. Steps 2, 3 and 5 add public API, so the owner approves the exact names and signatures before each of them is written.

Limits that come with this plan. Without a token that the protected resource checks, no lease lock can stop a holder that keeps running after its lease ended; the plan makes that case rare (renewal) and visible (the lost-lease token), and only step 5 makes it impossible, and only for resources that check the token. A Redis failover can still lose a lock or a fence counter. Automatic renewal would turn a hung holder into a lock that never ends, which is why the maximum hold time is mandatory. The fence counter key must not expire, because an expired counter would hand out old numbers again; that costs one long-lived key per lock name, and how to bound it is left to the decision of step 5.

Progress: step 1 is done: PR #324 (merge `a1cf34b`) took the lock out of `CacheRateLimiter`, and the CI of `main` at the merge is green, so the finding CCH-004 is closed. Step 2 is done: PR #330 (merge `5004bd5`) added the optional capabilities `IRefreshableCacheLock` and `IForceReleasableCacheLock` to `Naravel.Cache.Abstractions`, implemented by the Memory and Redis locks and not by the Memcached lock, and the CI of `main` at the merge is green. Steps 3 to 5 are not merged. The finding AV-002 and the finding MCD-007 stay OPEN. How MCD-007 (the Memcached release race) is closed, by accepting the risk or in another way, is not decided here and will be put to the owner.

## AD-003 — Scope/store flush semantics and Memcached administration

**Observed source behavior:** `ICacheStore.FlushAsync` is documented as flushing the store namespace. `ScopedCacheStore.FlushAsync` delegates to the backing store and escapes the scope. Memcached's implementation invokes cluster-wide `FlushAllAsync`.

**Decision required:**
- A. Preserve namespace semantics. Scope flush invalidates only that scope; provider implementations unable to do so fail explicitly. A separate, explicitly named administrative cluster-flush API may be introduced only if the owner wants it.
- B. Narrow the public contract so unsupported providers expose no flush on scoped views, while retaining an explicit store-level operation with its limitations.

**Architect recommendation:** A, with no implicit cluster-wide behavior. If Memcached cannot safely invalidate one namespace, ordinary flush should fail clearly. A destructive admin operation must be separate and explicitly named, permissioned/documented, and tested.

**Status:** DECIDED on 2026-11-16: option A, in the design that the owner approved in the conversation: an optional interface `IPrefixFlushableCacheStore.FlushPrefixAsync(keyPrefix, ct)` (deletion by prefix, not an epoch counter), a scope flush that removes only that scope and the scopes nested under it, `NotSupportedException` for a store without the capability, a Redis flush that throws when a primary is not connected and never removes the keys of locks, and the three behaviour changes listed in the `Fixed` entry of `CHANGELOG.md`. The owner merged PR #298 (merge `e196546`), PR #299 (merge `0ae187f`) and PR #305 (merge `f1efc6f`), which implemented it; CCH-001, CRD-002 and MCD-006 are VERIFIED CLOSED, and `MemcachedCacheStore.FlushAsync` called directly now throws `NotSupportedException` too (2026-11-18). Not decided: a separately named administrative cluster flush exists only if the owner asks for one, and the Memcached epoch question stays open.

## AD-004 — Redis connection initialization boundary

**Observed source behavior:** `AddNaravelRedis` registers a factory using synchronous `ConnectionMultiplexer.Connect`; Cache and Queue resolve managers synchronously on first use. PDR-010/D-66 records this as an intentional deviation.

**Decision required:**
- A. Pre-warm all configured Redis connections asynchronously during host startup; synchronous consumers may only retrieve already-created connections. Runtime-added/reloaded stores need an explicit asynchronous initialization path.
- B. Introduce async resolution through Cache/Queue and any consumer that can trigger first connection creation, retaining synchronous access only for already initialized instances.

**Architect recommendation:** A as the smallest safe migration compatible with the current synchronous consumer surface, provided startup failure/cancellation and reload behavior are explicitly defined. B is cleaner if async store resolution is acceptable across the framework.

**Status:** WAITING FOR OWNER.

## AD-005 — Bounded lifecycle for tag-version metadata

**Observed source behavior:** each distinct flushed tag leaves a no-TTL version key. Expiring the version blindly can reset it to zero and make an older still-live tagged value reachable.

**Decision required:**
- A. Require finite, bounded TTLs for tagged values and design version-key retention/reclamation around an enforced maximum tagged-value lifetime.
- B. Keep non-expiring tagged values and add durable generation/reclamation state whose lifecycle cannot be reset by cache eviction/restart.
- C. Remove tag support for providers that cannot offer a provably safe bounded lifecycle.

**Architect recommendation:** A if the owner accepts a documented maximum lifetime for tagged entries; otherwise B requires a durable control plane and more operational complexity. A plain TTL on version keys is not acceptable.

**Status:** WAITING FOR OWNER.

## AD-006 — TimeProvider injection surface for Cache

**Observed source behavior:** several production Cache paths use system wall-clock calls or `Task.Delay`; the current constructors/registrations do not consistently expose a testable `TimeProvider`.

**Decision required:** choose whether the clock is supplied through a new optional constructor parameter on time-dependent implementations, a package-level options/DI registration, or a shared internal clock service.

**Architect recommendation:** one package-level `TimeProvider` registration with `TimeProvider.System` as the default, injected into time-dependent implementations; add public overloads only where direct construction is supported and needs deterministic tests. Distinguish wall-clock timestamps from monotonic elapsed-time measurement.

**Status:** WAITING FOR OWNER.

## AD-007 — Retention of implicit Redis connection strings

**Observed source behavior:** `RedisManager.FromConnectionString` registers a factory closure that captures the raw connection string for the lifetime of the registry entry, including after successful connection creation. The string is not used as the generated connection name or emitted in logs, but it remains reachable in memory.

**Decision required:** is retention for retry-after-failed-open behavior acceptable, or should successful creation replace the retained factory with one that no longer captures the raw secret?

**Architect recommendation:** stop retaining the raw string after successful creation while preserving a retry path before success; document the actual in-memory secret lifetime. Do not claim the string is never stored.

**Status:** WAITING FOR OWNER.

## AD-008 — Scheduling the remediation work

**Observed repository state:** current `ROADMAP.md` says `NEXT: none`; the roadmap audit policy says open findings must be owned by roadmap stages. No remediation stage exists for AV-001 through AV-008.

**Decision required:** approve a new architecture-remediation stage, or direct these findings into separately named stages. The audit branch must remain separate until the decision register and review are complete.

**Architect recommendation:** a dedicated, dependency-ordered architecture hardening stage, with AV-001 first, AV-002 second, then the P2 items. Do not start runtime fixes until AD-001 through AD-008 are decided where they affect their scope.

**Status:** WAITING FOR OWNER.


## AD-009 — Partial batch enqueue failure semantics

**Observed source behavior:** `JobDispatcher.BatchAsync` records the full `TotalJobs` before sequentially publishing each message. A later publish/build/cancellation error leaves earlier messages queued, un-published members counted as pending, and no batch ID returned to the caller.

**Decision required:** choose the public failure contract and recovery model:
- A. Return/throw a typed partial-dispatch result carrying the batch identity and per-member state, while a durable dispatch state machine reconciles or terminally accounts for every planned member.
- B. Use a transactional outbox/dispatcher owned by one durable store and document that batch members must share its transaction boundary; reject unsupported cross-connection combinations.
- C. Another explicit strategy that guarantees no invisible permanently-pending batch.

**Architect recommendation:** A is more general because the current API allows per-job connection selection. Use idempotent publication/reconciliation and a typed exception/result that exposes the batch ID. Do not claim atomic publication across arbitrary brokers.

**Status:** DECIDED on 2026-11-22: the owner chose option A in the working session. `BatchAsync` will fail with a typed exception (or result) that carries the batch identity and the state of each planned member, and a reconciliation step will publish again or terminally account for every planned member, so that no batch stays pending without anyone seeing it. No atomic publication across arbitrary brokers is claimed. Not implemented yet: the type is new public API, so its exact name and shape are proposed to the owner and approved before the code is written.

## AD-010 — Retrying a failed job that belonged to a terminal batch

**Observed source behavior:** `FailedJobManager.RequeueAsync` assigns a new message ID and resets attempts, but preserves `BatchId`. The worker acknowledges and skips messages whose original batch is cancelled; a finished non-cancelled batch also ignores later count updates.

**Decision required:**
- A. A failed-job retry is an independent operation: clear `BatchId`, preserve the terminal batch as historical state, and document that the retry does not alter the old batch report.
- B. Retry reopens/reconciles the original batch, with a defined policy for counts, cancellation, callbacks, concurrent retries and persistent repositories.

**Architect recommendation:** A. It preserves an immutable terminal batch record and makes explicit operator retry requests actually execute. A new batch can be created when the caller wants new batch-level accounting.

**Status:** DECIDED on 2026-11-21: the owner chose the architect recommendation. A retry of a failed job is independent work: the retried message does not rejoin its original batch, so `BatchId` is cleared and the terminal batch keeps its historical counts. Implementation is in a separate pull request.

## AD-011 — Empty batch behavior

**Observed source behavior:** `BatchAsync` accepts an empty sequence and creates a zero-job batch. No worker message triggers completion; callback behavior differs between in-memory and persistent repositories.

**Decision required:** reject empty batches immediately, or define them as completed at registration and run terminal callbacks before returning.

**Architect recommendation:** reject empty batches with `ArgumentException` before registering any repository record.

**Status:** DECIDED on 2026-11-21: the owner chose to reject an empty batch. `BatchAsync` throws `ArgumentException` before any repository record is registered. Implementation is in a separate pull request.

## AD-012 — TimeProvider injection for Queue and provider drivers

**Observed source behavior:** Queue runtime and providers use direct wall-clock reads and delays in message defaults, dispatch scheduling, worker MaxRuntime, failed-job retries and Kafka polling. `AGENTS.md` requires injected `TimeProvider` and deterministic tests.

**Decision required:** approve the injection boundary (DI singleton with `TimeProvider.System` default, explicit constructor parameters, or another package-internal abstraction) and how that reaches core and provider packages without breaking public constructors.

**Architect recommendation:** register a shared `TimeProvider` through `AddQueue` with `TimeProvider.System` default and inject it into dispatcher/worker/failed-job manager/provider clock-dependent code. Prefer explicit message creation timestamps over system-clock initializers where compatibility permits. Use monotonic timestamps for elapsed-time measurement.

**Status:** WAITING FOR OWNER.


## AD-013 — Atomic registry snapshot for synchronous Manager.Driver

**Observed source behavior:** PR #244 fixed the sync-to-async factory replacement race by making the sync creation path call `IDriverRegistry.Create`, which reads and validates the selected factory in one registry operation. A narrower race remains: a `DriverAsync` call has already started an async factory; another thread replaces it with a sync factory; before the manager receives the `Registered` event, `Driver` sees the new sync registration but joins the old in-flight async entry and blocks.

**Decision required:** approve whether the public `IDriverRegistry<TDriver>` contract may add an atomic factory snapshot/creation-kind operation (or equivalent versioned registration token) so the Manager can distinguish the entry's factory generation from the registry's current generation and never synchronously wait on an async-created entry after replacement.

**Architect recommendation:** approve a backward-compatible additive snapshot/version API if possible; otherwise define a safe failure behavior for `Driver` during that window rather than waiting. The exact public contract and compatibility impact require owner approval.

**Status:** DECIDED by the owner on 2026-11-09 (`roadmap/AUDIT.md` D-83): `IDriverRegistry` is not changed, and the remaining window is an accepted limit that `docs/en/foundation.md` documents; it is reopened as a new row if a consumer meets it. The architect recommendation above was not adopted.


## AD-014 — File queue name mapping and existing on-disk queue compatibility

**Observed source behavior:** `FileQueueDriver.Sanitize` removes invalid filename characters rather than validating or injectively encoding queue names. The literal queue name `..` remains `..` and escapes the configured base directory when combined with `pending`, `reserved` or `failed`. Different queue names can collapse to one physical folder. The driver currently stores pending/reserved/failed files under `{basePath}/{sanitizedQueue}/...`.

**Decision required:** choose the compatibility contract for fixing this P1 path-boundary issue:
- A. Preserve the existing folder mapping for names already in a safe, documented portable subset, reject unsafe names before filesystem access, and document the accepted queue-name grammar. This avoids moving existing safe queues but becomes a validation restriction.
- B. Encode all queue names injectively (or use an ID/hash mapping) and introduce a versioned on-disk layout with a migration/dual-read policy for existing pending, reserved and failed files.
- C. Another explicit migration approach that guarantees path containment and no queue aliasing without silently stranding existing messages.

**Architect recommendation:** A as the least disruptive safe correction if the owner accepts a portable queue-name grammar; keep existing safe folder names unchanged and reject unsafe names. If arbitrary Unicode/special queue names are a required public feature, choose B and provide a tested migration path. Do not silently sanitize and collapse names.

**Status:** DECIDED on 2026-11-15: option A, the architect recommendation. The owner merged PR #293 (merge `6052441`), whose description said that it implemented option A and that it was not an owner decision until confirmed. `docs/en/queue.md` states the grammar; a queue folder whose name is inside it keeps its name, nothing was migrated, and a name outside it must be renamed by the user (the `Fixed` entry of `CHANGELOG.md` says how). The residual case of letter case and reserved device names, FILE-007, is not part of this decision; PR #308 (merge `76e5433`, 2026-11-19) rejected the reserved device names, and the owner accepted the letter-case limit (D-96, 2026-11-18).


## AD-015 — Transactional DatabaseJobEnqueuer connection identity

**Observed source behavior:** `DatabaseJobEnqueuer.Enqueue` delegates envelope construction to the ordinary `IJobMessageFactory`. That factory copies `Job.Connection`, the caller's `DispatchOptions.OnConnection`, or the QueueManager default into `QueuedMessage.Connection`. The enqueuer persists this value unchanged even though PDR-023 says Connection is ignored because the job is stored in the Database context. The worker uses the persisted connection for chain follow-ups, and `FailedJobManager.RetryAsync` uses it to select the retry destination.

**Decision required:** how should the enqueuer identify the exact named Database queue connection that owns the DbContext/table, especially when multiple Database stores or a non-Database Queue default are configured?
- A. Bind each enqueuer instance/registration to one explicit named Database connection and force that identity into the stored envelope; reject incompatible job/configure connection overrides.
- B. Persist no connection and require a single documented default Database connection; the worker and retry tooling must both be guaranteed to resolve that same store.
- C. Another explicit binding contract that preserves the DbContext/queue-connection relationship across chain and failed-job retry.

**Architect recommendation:** A. A named database connection identity is needed to make chain and retry routing deterministic when more than one queue backend/store is configured. Do not simply set Connection to null unless the default-store constraint is explicit and enforced.

**Status:** DECIDED on 2026-11-22: the owner chose option A in the working session. Each transactional enqueuer is bound to one named Database queue connection, that name is written into the stored envelope, and a job or dispatch option that names a different connection is rejected. Not implemented yet: the way the name is given at registration is new public API, so it is proposed to the owner and approved before the code is written.

## AD-016 — Approved SQL DDL deliverables for Database queue schema

**Observed source behavior:** `Naravel.Queue.Database` currently ships one embedded/plain-file SQLite DDL script covering QueueJobs, QueueFailedJobs and QueueBatches, plus a helper restricted to Development/Test/Testing. Production setup for every provider is documented as consumer-managed EF Core migrations. PDR-012's implementation record still says no SQL scripts exist, which is now factually stale. The PDR wording does not explicitly say whether scripts are needed for every supported relational provider or only a development/test quickstart.

**Decision required:**
- A. Accept the SQLite-only idempotent helper/script for local Development/Test/Testing; production schema remains consumer-managed EF Core migrations for SQLite, SQL Server, PostgreSQL and MySQL.
- B. Require checked-in provider-specific SQL scripts for SQL Server, PostgreSQL and MySQL in addition to SQLite, with versioning and integration validation for each dialect.

**Architect recommendation:** A. The package already documents EF Core migrations as the production path for every provider, and duplicated vendor DDL creates a second schema-maintenance surface. The PDR implementation record should be corrected to state SQLite DDL/helper implemented and provider-specific scripts not part of the accepted deliverable, if the owner approves this boundary.

**Status:** DECIDED on 2026-11-22: the owner chose option A in the working session. The accepted deliverable is the SQLite script and the helper for Development, Test and Testing; production schema for SQLite, SQL Server, PostgreSQL and MySQL stays with the application's own EF Core migrations, and no vendor-specific SQL scripts are part of the deliverable. The implementation record of PDR-012 (item d) was corrected to say so in the same change that records this decision.


## AD-017 — Positive visibility timeout policy for all reclaim-capable queue drivers

**Observed source behavior:** Database, File and Redis driver constructors accept a zero or negative visibility timeout, and their reclaim algorithms can then consider an active reservation immediately stale. The corresponding configuration paths do not consistently reject non-positive values.

**Decision required:** confirm that every driver with visibility-based reservation reclaim must reject `VisibilityTimeout <= TimeSpan.Zero`, with no "disable reclaim" meaning attached to zero. If disabling reclaim is ever required, it should be represented by a separate explicit option/capability, not an invalid lease duration.

**Architect recommendation:** require a strictly positive timeout for reclaim-capable drivers. Drivers that do not implement visibility reclaim should report `null`.

**Status:** DECIDED on 2026-11-20: the owner confirmed the recommended option in the working session, a strictly positive timeout for every reclaim-capable driver, with no "disable reclaim" meaning attached to zero. A separate explicit option for turning reclaiming off was not designed. Implemented for the File driver by PR #311 (it was merged before this decision was asked for, and the owner merged it), and for the Database and Redis drivers by PR #313 (constructor and registration checks; the findings FILE-004, DBQ-001 and RQR-001 and the veto AV-012 are closed in these documents).


## AD-018 — Redis Queue driver's native failed list

**Observed source behavior:** `RedisQueueDriver.FailScript` pushes each terminal message envelope onto `netqueue:{queue}:failed`. No RedisQueueDriver API reads or removes this list. The optional `RedisFailedJobStore` stores the same failed envelope separately in a hash and provides the public List/Forget/Flush operations. The core `IFailedJobStore` contract describes its records as a secondary record independent of driver-native failed queues.

**Decision required:** is the per-queue list intended as an administrative dead-letter list to be inspected/managed with Redis tooling, or is it obsolete duplicate storage that should be removed in favor of `IFailedJobStore`?
- A. Keep it as a native Redis DLQ, document the exact key format and administrative inspection/cleanup path, and define its retention.
- B. Remove it and rely on `IFailedJobStore` as the supported failed-job source of truth.
- C. Expose it through a supported Redis-specific administrative API and make its cleanup semantics explicit.

**Architect recommendation:** A only if the native DLQ is a deliberate user-facing capability and its lifecycle is documented; otherwise B avoids duplicate unbounded payload retention.

**Status:** DECIDED on 2026-11-22: the owner chose option B in the working session. The Redis driver no longer writes `netqueue:{queue}:failed`; `IFailedJobStore` is the one record of a failed job. Lists that earlier versions wrote are not touched. Implemented by PR #325 (merge `e7d8662`).

## AD-019 — Persistent queue batch history retention

**Observed source behavior:** `RedisBatchRepository` adds every batch ID to a persistent index set and has no Forget/Delete/Prune API. `ResumePendingCallbacksAsync` scans every ID on every worker startup, including historical batches whose callbacks are already complete. DatabaseBatchRepository likewise retains completed batch records with no repository-level removal method.

**Decision required:** choose whether batch history is retained indefinitely by design, or define an application-controlled retention/pruning contract and a minimum audit-history duration.

**Architect recommendation:** expose an explicit prune/delete operation with an age/terminal-state policy; never delete unfinished batches or batches with incomplete callback progress. Avoid a blind TTL on the hash while leaving the index entry behind.

**Status:** DECIDED on 2026-11-22: the owner chose an explicit prune operation that the application calls. Only batches that are finished and whose applicable callbacks were all delivered are deleted; running batches and batches with undelivered callbacks never are. The repositories store no finish time, so there is no minimum age; adding one needs a new column on `QueueBatches` and would be a separate decision. Implemented by PR #326 (merge `0cf0c26`).

## AD-020 — Cancellation semantics for in-flight Redis commands

**Observed source behavior:** Queue Redis, RedisBatchRepository and RedisFailedJobStore check the token before issuing commands, but StackExchange.Redis operations do not accept a CancellationToken. Once a mutating command is sent, the server may apply it even if the caller has cancelled.

**Decision required:**
- A. Define cancellation as a preflight check only after a command is issued; the command is awaited to learn its outcome and may complete despite a later cancellation request.
- B. Cancel only the caller's wait with `WaitAsync(token)`, explicitly documenting that the server-side operation can still complete and the outcome is indeterminate.
- C. Another explicit contract with equivalent handling of the uncertain outcome of an already-sent mutation.

**Architect recommendation:** A for queue mutations; a cancelled wait that hides whether Push/Ack/Release/Fail actually completed can trigger unsafe duplicate retries. Check cancellation before each separate command and use atomic scripts/transactions for multi-command state changes where possible. Document that an already-sent Redis command cannot be rolled back by a caller token.

**Status:** DECIDED on 2026-11-22: the owner chose option A in the working session. The token is a check before each Redis command is sent; a command that was sent is awaited so the caller learns its outcome, and a token cannot roll it back. The behaviour already existed; the contract is now written down and tested. Implemented by PR #327 (merge `9193594`).


## AD-021 — Atomic owner metadata for Queue unique-job leases

**Observed source behavior:** `CacheUniqueJobGate` acquires an `ICacheLock` and stores `messageId|token` in a separate `ICacheStore` key. Release reads the owner record, releases the token-owned lock, then unconditionally removes the owner key. A newer dispatch can acquire and write its owner record between the old lock release and the old unconditional remove. A delayed owner write can also arrive after the old lease expires and overwrite the newer owner's metadata.

**Decision required:**
- A. Add a backend-guaranteed atomic operation that acquires the lock and associates the message owner in one transaction/script, and releases/removes only when both owner and lock token still match.
- B. Extend the Cache lock abstraction to support a caller-supplied owner/fencing token so the lock's own token can identify the message owner without a separate mutable metadata key.
- C. Accept a weaker, bounded-liveness guarantee for unique jobs and document that stale metadata may block a key until TTL expiry. This option does not satisfy the current claim that stale owner cleanup is safely fenced and is not recommended.

**Architect recommendation:** A or B. Prefer B if it can be implemented consistently by Memory, Redis and Memcached without exposing backend-specific behavior. Otherwise use A and make unsupported providers fail explicitly rather than silently weakening uniqueness. Do not select C for production correctness.

**Status:** WAITING FOR OWNER. On 2026-11-22 the owner deferred it: the lock work of AD-002 (steps 2 to 5, which add the optional renewal and fencing capabilities) is being done first and in another session, and this decision depends on it.


## AD-022 — Lifetime of ambient transaction state captured by detached tasks

**Observed source behavior:** the ambient transaction uses `AsyncLocal`. A child task created in the callback captures its own ExecutionContext and can retain the ambient node after the parent callback restores its value and commits/disposes the transaction. The leased scope currently has no inactive-state guard.

**Decision required:**
- A. Enforce an internal active/completed state on the leased scope. Commit, rollback or disposal invalidates it; any later acquisition through a captured ExecutionContext fails with a clear Naravel lifecycle exception. Also document that all work intended to participate must be awaited before the callback returns.
- B. Keep the current behavior and document strictly that callbacks must not start detached work and that all child tasks must complete before return. This is weaker because the framework cannot detect stale use before provider access.

**Architect recommendation:** A. The internal guard prevents a stale captured ambient context from reaching a disposed provider connection and turns an opaque provider error into a deterministic framework error. It does not make concurrent use of one transaction safe; the existing one-flow-at-a-time restriction remains.

**Status:** DECIDED on 2026-11-22: the owner chose option A in the working session. Commit, rollback and disposal end the transaction's scope, and any later use of it through a captured context fails with a clear `InvalidOperationException`. Implemented by PR #328 (merge `010b0d7`).
