# Architecture Vetoes — target production architecture

Date: 2026-11-08  
Current main baseline: c6a7d58ffb84d885741998bb32d2082c54c4ab61  
Scope: the nineteen reviewed packages Naravel.Foundation, Naravel.Cache, Naravel.Redis, Naravel.Cache.Redis, Naravel.Cache.Memcached, Naravel.Queue, Naravel.Queue.Memory, Naravel.Queue.File, Naravel.Queue.Database, Naravel.Queue.Redis, Naravel.Queue.Coordination, Naravel.Queue.Encryption, Naravel.Events, Naravel.Events.Queue, Naravel.Database, Naravel.Database.Sqlite, Naravel.Database.MySql, Naravel.Database.Postgres and Naravel.Database.SqlServer.

## Disposition

This is a principal-architect decision, not a suggestion list. Some of the designs below were explicitly approved in earlier PDRs and documented as intentional limitations. That historical approval explains the implementation; it does not make the trade-off safe or acceptable as the target architecture for a reliable, isolated and scalable platform.

**AV-001, AV-002 and AV-011 are P1 release blockers. AV-003 through AV-010 and AV-012 through AV-019 are mandatory P2 architecture work.** None is implemented or verified by this audit commit. The related findings remain OPEN until the design is corrected, the exact regression tests pass, and required CI evidence is recorded.

A veto does not authorize an uncontrolled runtime/API/dependency change. Remediation must be a separate, scoped task with an updated PDR where required. An old PDR approval alone is not a valid reason to move a veto to ACCEPTED RISK.

## Veto register

### AV-001 — P1: Store/scope flush must never escape its namespace

**Affected findings:** CCH-001, CRD-002, MCD-006.

**Why this is vetoed:** ScopedCacheStore.FlushAsync delegates to the backing store and therefore escapes a user/tenant scope. The Memcached adapter maps an ordinary store flush to Enyim FlushAllAsync, which clears every key on every working node in the shared client pool. Redis FlushAsync also must not report success after skipping required disconnected primaries. These behaviours either delete unrelated data or leave stale data while claiming invalidation succeeded. This violates the namespace semantics and isolation expected of a cache store.

**Required target design:** all ordinary store/scope flushes must affect exactly the requested namespace and must be complete-or-fail. A cluster-wide destructive flush may exist only as a separately named administrative operation with explicit cluster-level intent. If a provider cannot safely implement scoped invalidation, the operation must fail explicitly rather than widening its blast radius. Any Memcached epoch/generation mechanism must use control state that cannot be evicted/reset while old entries remain reachable; a plain evictable Memcached key is not sufficient.

**Required proof:** multi-store and multi-scope tests with unrelated base/store/tenant keys; Redis disconnected/partial-primary tests; Memcached real-server tests showing flush of one store/scope leaves all other keys intact; generation eviction/restart tests proving stale values never reappear.

### AV-002 — P1: Distributed lock and rate-limiter mutual exclusion must be real

**Affected findings:** CCH-004, MCD-007.

**Why this is vetoed:** Memcached release performs GET-token, local compare, then unconditional DELETE. A lease can expire after the read and a new owner can acquire before the old owner's DELETE, allowing the stale owner to delete the new owner's lock. Separately, CacheRateLimiter uses a fixed 30-second lock TTL without renewal/fencing or a proven upper bound for its multi-await critical section. Both undermine the mutual-exclusion guarantee callers reasonably infer from ICacheLock.

**Required target design:** atomic token-conditioned release is necessary but is not, by itself, proof that a stale holder cannot continue its critical section after lease expiry. Any correctness-grade distributed critical section must either use a fencing token that the protected resource validates on every relevant mutation, or another enforceable mechanism that rejects work from an expired owner. For `CacheRateLimiter`, prefer one atomic backend-side state transition for check/increment/window handling over a multi-await critical section protected only by a fixed lease. Where a backend cannot provide the required guarantee, do not expose it as a correctness-grade distributed lock and do not use it for cross-instance rate limiting/coordination. A lock timeout or lease expiry must never silently permit overlapping writes.

**Required proof:** a deterministic stale-owner interleaving test (A acquires; A's lease expires; B acquires; A resumes; the protected resource rejects A's stale mutation; B remains valid); two-limiter-instance test that stalls the first operation beyond its lease and proves the limit is never exceeded; expiry, cancellation and backend-timeout tests.

### AV-003 — P2: Tag-version metadata needs a safe bounded lifecycle

**Affected finding:** CCH-008.

**Why this is vetoed:** every distinct tag ever flushed leaves a no-TTL version key. High-cardinality tags make backing-store metadata grow monotonically in long-running installations. A blind TTL is unsafe: after a version key expires or is evicted, treating its version as zero can make an old unexpired tagged value reachable again.

**Required target design:** choose an enforceable bounded/reusable tag contract or a generation/epoch and reclamation design that cannot reset while old values remain reachable. If the design requires finite TTL for tagged entries, enforce and document it rather than assuming callers comply.

**Required proof:** high-cardinality stress across the full lifecycle; reclamation tests with tagged entries that have no TTL; verify that cleanup/eviction/restart can never resurrect an invalidated value.

### AV-004 — P2: Network I/O must be genuinely asynchronous

**Affected findings:** RED-002, MCD-009.

**Why this is vetoed:** first Redis use invokes synchronous ConnectionMultiplexer.Connect; Memcached counters wrap synchronous socket operations in Task.Run. The latter occupies shared ThreadPool workers while waiting on network I/O; moving blocking I/O to a worker is not an asynchronous implementation. Under cold-start concurrency or slow backend conditions this can increase worker pressure and tail latency.

**Required target design:** create/pre-warm Redis connections through an async initialization path before synchronous Cache/Queue consumers use them; synchronous access may return only an already initialized connection. Provide genuinely asynchronous Memcached counter operations through a supported client/API or maintained adapter. Do not hide synchronous I/O behind Task.Run.

**Required proof:** controlled asynchronous connection-open tests with cancellation and concurrent first use; slow-Memcached load tests measuring worker usage/queueing while unrelated application work remains schedulable; counter atomicity and TTL-contract tests.

### AV-005 — P2: Named Memcached stores need independently scalable server pools

**Affected finding:** MCD-008.

**Why this is vetoed:** current registration rejects any named Memcached stores whose normalized server lists differ and registers one global Enyim client. Named stores therefore cannot isolate workloads, capacity, regions or failure domains across clusters.

**Required target design:** create/reuse one client per canonical normalized server-pool configuration. Identical pools share a client; distinct pools are independent. Reload and disposal must retire each pool only when its dependants are safe and dispose the underlying client exactly once.

**Required proof:** two different clusters register and operate independently; same-topology stores share a client; changing one topology does not retire another; disposal exactly once; one cluster outage does not take down the other store.

### AV-006 — P2: Late resource cleanup failures must be observable

**Affected finding:** FND-004.

**Why this is vetoed:** Foundation silently catches every exception while disposing a driver that finishes creation after its manager has already been disposed. Avoiding an unobserved background exception is correct; making a resource-release failure invisible is not.

**Required target design:** route the late-disposal failure to a manager-owned diagnostic/logging mechanism while preserving the guarantee that other resources are cleaned up and no background exception is left unobserved.

**Required proof:** a gated driver factory completes after manager disposal and returns a driver whose disposal throws; assert exactly-once disposal and a structured, observable failure without an unobserved task exception.

### AV-007 — P2: Cache and Lock need provider-neutral operational telemetry

**Affected finding:** CCH-007.

**Why this is vetoed:** the Naravel.Cache meter currently instruments the rate limiter but not the store and lock contracts. Operators cannot compare Memory, Redis, Memcached and custom providers through one consistent surface or detect latency, miss-rate and failure changes at the abstraction boundary.

**Required target design:** bounded metrics and activities for cache hit/miss, write/remove, operation latency/failure, and lock acquisition/contended/failure. Never emit keys, values, subject identifiers, tokens, credentials or connection strings; avoid unbounded tag dimensions.

**Additional second-pass evidence:** `CacheRateLimiter.AttemptAsync` accepts any non-empty caller-supplied `limiter` string and records it as a metric tag (and activity tag). The API does not constrain this to a finite registered policy set. Applications that derive limiter names from request/user/tenant data can therefore create unbounded metric cardinality. This is part of AV-007, not a separate closure or a claim that every application currently does this.

**Required proof:** MeterListener and ActivityListener tests for every operation outcome, asserting stable low-cardinality tags and the absence of sensitive/high-cardinality values. Include repeated attempts with many distinct limiter strings and prove metric dimensions remain bounded.

### AV-008 — P2: Positive TTL must never become permanent storage

**Affected finding:** MCD-010.

**Why this is vetoed:** EnyimMemcachedCore 3.5.2 converts a positive TimeSpan to integer seconds by truncation for relative expirations up to 30 days. A positive duration below one second becomes zero; Memcached interprets zero as no expiry. A short-lived key or rate-limit bucket can therefore become permanent.

**Required target design:** round a positive TTL up to the provider's minimum representable expiration or reject unsupported durations explicitly. Never silently map a positive TTL to no-expiry.

**Required proof:** real Memcached expiry tests at 1 ms, 500 ms, 999 ms and 1 second for both Set and TTL counters, plus the 30-day relative/absolute boundary.

### AV-009 — P2: Batch dispatch must recover from partial publication

**Affected findings:** QUE-001, RQR-004.

**Why this is vetoed:** `IJobDispatcher.BatchAsync` registers the entire planned job count before publishing messages one by one. A failure after one or more successful publishes leaves a batch whose un-published members can never reach a terminal state; the API throws before returning the batch identity. Persistent batch state can remain pending indefinitely, without an observable recovery handle.

**Required target design:** a partial batch dispatch must be explicit, discoverable and recoverable. Every planned member must have a durable state (pending publication, published, completed, failed, or deliberately cancelled), and a failure must not strand an unreachable batch. Do not assume heterogeneous queue connections and brokers share one transaction. The public partial-failure/result contract is owner decision AD-009.

**Required proof:** inject failure on the Nth publish, during message construction and during cancellation; prove the batch identity and every member's final/recoverable state remain observable after repository recreation, without silently losing already-published jobs.

### AV-010 — P2: In-process queue state must have a bounded lifetime

**Affected findings:** MEM-001, FILE-002.

**Why this is vetoed:** `MemoryQueueDriver` keeps queue lists, per-queue lock objects and failed message envelopes in static dictionaries for the lifetime of the process. Empty/misspelled queue probes allocate permanent list/lock entries; every terminal failure retains its full message payload in a private dictionary that has no read or removal path. `FileQueueDriver` also keeps a static `QueueGates` dictionary of `SemaphoreSlim` instances keyed by full root path and queue name, with no reclamation/disposal path. These process-local coordination tables grow for every distinct queue/root pair and persist for the process lifetime.

**Required target design:** preserve the documented sharing of same-name Memory connections within one process and File-driver coordination for the same physical queue path, but ensure empty queue/lock/gate entries and failed-message state have a safe, bounded lifecycle. Do not remove/dispose a gate while a concurrent operation may still be using it. Remove the inaccessible duplicate Memory failed store or define an explicit, bounded inspection/removal contract.

**Required proof:** high-cardinality queue/root probe and terminal-failure tests demonstrate state reclamation; concurrent pop/ack/cleanup tests prove no duplicate reservation or lost message; two Memory driver instances with the same connection name and File driver instances sharing the same root/queue still coordinate correctly.

### AV-011 — P1: File queue names must not escape or alias the configured root

**Affected finding:** FILE-001.

**Why this is vetoed:** `FileQueueDriver.Sanitize` deletes invalid filename characters rather than validating or injectively encoding queue names. The dot-segment name `..` survives unchanged, so combining it with the configured base path and the `pending`, `reserved` or `failed` suffix can write outside that root. Different logical names can also collapse to the same sanitized directory (for example, a slash-containing name and the name without the slash). This breaks filesystem containment and queue namespace isolation.

**Required target design:** every accepted queue name must map injectively to one directory component under a canonicalized base root. Reject unsafe names or use an injective encoding; prove the final path remains below the configured root. Do not delete characters and assume that the result is safe. The compatibility/migration policy for existing on-disk queue directories is owner decision AD-014.

**Required proof:** path traversal, rooted-path, dot-segment and colliding-name tests on Windows and Linux; prove all accepted directories remain inside the base root and distinct accepted queue names cannot read or settle each other's messages.

### AV-012 — P2: Reclaiming drivers must reject non-positive visibility timeouts

**Affected findings:** DBQ-001, FILE-004, RQR-001 (and any other reclaim-capable driver that accepts a non-positive timeout).

**Why this is vetoed:** Database, File and Redis drivers expose a visibility timeout that controls when an in-flight reservation can be reclaimed, but their public constructors currently accept zero/negative values without rejecting them. In Database, `staleBefore = now - visibilityTimeout`; in File, elapsed time is compared directly with the timeout; Redis similarly computes the reservation deadline from the configured value. A zero/negative setting therefore makes a live reservation immediately eligible for reclaim, allowing two workers to execute the same message concurrently.

**Required target design:** every driver that reclaims reservations must require a strictly positive visibility timeout at constructor and configuration boundaries. Invalid values must fail fast with a message naming the store and option. Drivers that do not use visibility reclaim should expose `null`, not a non-positive duration.

**Required proof:** constructor and DI tests reject zero/negative values for every reclaim-capable driver; two-worker tests prove that a live reservation cannot be reclaimed before a positive timeout; preserve the existing positive default and stale-reservation behavior.

## Historical PDR decisions explicitly vetoed

- PDR-007: cluster-wide Memcached flush (AV-001), non-atomic Memcached lock release and fixed rate-limit lock lease (AV-002), single shared Memcached pool (AV-005), blocking counter workaround (AV-004), unbounded tag version keys (AV-003), and limiter-only Cache telemetry/high-cardinality limiter tags (AV-007).
- PDR-012: partial batch publication can leave permanently-pending batches (AV-009); unbounded process-local Memory queue state and File gate table (AV-010); unsafe File queue path mapping (AV-011); non-positive reclaim visibility timeouts (AV-012).
- PDR-010 / D-66: synchronous first Redis connection (AV-004).

The PDRs retain their original approval text as historical context and append the veto disposition. They must not be read as approval to ship these constraints as the final production architecture.

## Verification status

- Runtime code changes: none.
- Test changes or tests run: none.
- Build/CI: NOT RUN.
- Vetoes cleared: none.
- Next gate: obtain owner decisions AD-001 through AD-022, then create a prioritized remediation stage for AV-001, AV-002 and AV-011 first and schedule AV-003 through AV-010 and AV-012 through AV-019. Do not mark the architecture accepted for production until the required verification evidence is present.


## Owner decisions pending — not architecture vetoes

The following items are source-confirmed observations or contract ambiguities. They are deliberately **not** represented as vetoes until the owner chooses the intended public contract. See [OWNER-DECISIONS.md](OWNER-DECISIONS.md).

- **AD-001 / MCD-011 — explicit zero TTL:** Memcached currently maps an explicit `TimeSpan.Zero` to the same Enyim expiration value as `null` (no expiry), while `CacheFake` rejects explicit zero. The `ICacheStore.SetAsync` contract does not currently define zero-TTL semantics clearly enough to declare one provider behavior incorrect. Owner decision required.
- Other API and lifecycle decisions that affect remediation sequencing are listed in the owner decision register. No answer is inferred from historical PDR wording.


### AV-013 — P2: Unique-job owner metadata must be fenced with the lock lease

**Affected finding:** QCO-001.

**Why this is vetoed:** `CacheUniqueJobGate` stores message ownership separately from the Cache lock. The release path validates an owner record, releases the token-owned lock, then unconditionally removes the owner key. A newer dispatch can acquire the released lock and write its owner record before that remove runs; the stale releaser then deletes the newer record. A delayed owner write can also overwrite a newer lease's metadata after the earlier lock expires. Token-fencing the lock does not fence the separate metadata mutation.

**Required target design:** atomically bind the message owner to the lock lease, or use a backend-guaranteed compare-and-set/delete operation that checks both the current owner and token. A stale owner must never overwrite or remove metadata belonging to a newer lease. Preserve the optional Queue/Cache package boundary and make unsupported backends fail explicitly rather than silently weakening the guarantee. The exact abstraction is owner decision AD-021.



### AV-014 — P2: JSON-path updates must represent JSON null correctly

**Affected finding:** DBQ-001.

**Why this is vetoed:** SQLite, MySQL and PostgreSQL JSON-path update compilers call `JsonText(value!)`; the JSON serializer helper has no null case and dereferences the null value. The documented JSON-path null update therefore fails before SQL is generated on three supported dialects, while SQL Server explicitly rejects this operation because its `json_modify` semantics delete the key.

**Required target design:** encode JSON null as the JSON literal `null` for SQLite/MySQL/PostgreSQL, retain SQL Server's explicit unsupported behavior unless a separate delete-key API is approved, and test the actual database semantics.

### AV-015 — P2: Every insert compiler path must enforce the final expanded parameter count

**Affected finding:** DBQ-002.

**Why this is vetoed:** `Raw` values can expand to multiple bound parameters. Batch insert checks the final binding count after expansion, but `CompileInsertGetId` checks only the number of columns and returns without checking the expanded count.

**Required target design:** enforce the dialect limit after all raw expressions have expanded, for every statement-producing compiler path. A statement over the limit must fail before provider execution.

### AV-016 — P2: Direct raw SQL must reserve the generated `@pN` marker namespace

**Affected finding:** DBQ-003.

**Why this is vetoed:** Query raw methods and `Raw` reject `@pN` markers, but direct `IDatabase` raw SQL passes through `DatabaseExecutor.Prepare` without the same check. A caller's existing marker can alias a generated marker and silently change the SQL's meaning.

**Required target design:** apply one consistent reserved-marker rule at the lowest shared preparation boundary, without silently rewriting user SQL.

### AV-017 — P2: Ambient transaction scopes must be invalidated at owner completion

**Affected finding:** DBQ-004.

**Why this is vetoed:** `AsyncLocal` values are captured by child ExecutionContexts. Restoring the parent value after a callback does not clear a detached task's captured ambient node. The current leased scope continues returning borrowed leases after its owning transaction has committed/disposed.

**Required target design:** mark the leased scope inactive at commit/rollback/disposal and reject later acquisition through stale ambient contexts before touching provider objects. Document that work intended to join the transaction must be awaited before the callback returns. The owner must decide whether to enforce this internal guard in addition to the documentation (AD-022).

### AV-018 — P2: Transaction cleanup must always release the connection lease

**Affected finding:** DBQ-005.

**Why this is vetoed:** `DatabaseTransaction.DisposeAsync` disposes the provider transaction and then the owned lease sequentially. If provider transaction disposal throws, lease disposal is skipped and the connection pool resource can remain held.

**Required target design:** guarantee lease disposal in a `finally` path, preserve the primary exception, and retain secondary cleanup failures for diagnosis. Fault-injection tests must prove the lease is released exactly once even when provider disposal fails.


### AV-019 — P2: Every SQLite in-memory connection must be uniquely named and shared with its keeper

**Affected finding:** SQL-001.

**Why this is vetoed:** the driver documents support for both `:memory:` and `Mode=Memory`, but only the literal `:memory:` form is rewritten to a unique shared-cache data source. A named `Mode=Memory` source with the default private cache gives each operation a separate database; a shared-cache name reused by two Naravel connection definitions can also violate the documented isolation boundary.

**Required target design:** normalize every memory-mode definition to a unique internal named data source and shared cache for the keeper and operation connections, or explicitly reject unsupported configurations. Tests must prove persistence across leases and isolation across connection names.
