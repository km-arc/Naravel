# Architecture Vetoes — target production architecture

Date: 2026-11-08  
Revalidation baseline: main at 44e957ca3db6d9f15665fc8fa070f70522c163c9  
Scope: the five reviewed packages Naravel.Foundation, Naravel.Cache, Naravel.Redis, Naravel.Cache.Redis and Naravel.Cache.Memcached.

## Disposition

This is a principal-architect decision, not a suggestion list. Some of the designs below were explicitly approved in earlier PDRs and documented as intentional limitations. That historical approval explains the implementation; it does not make the trade-off safe or acceptable as the target architecture for a reliable, isolated and scalable platform.

**AV-001 and AV-002 are P1 release blockers. AV-003 through AV-008 are mandatory P2 architecture work.** None is implemented or verified by this audit commit. The related findings remain OPEN until the design is corrected, the exact regression tests pass, and required CI evidence is recorded.

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

## Historical PDR decisions explicitly vetoed

- PDR-007: cluster-wide Memcached flush (AV-001), non-atomic Memcached lock release and fixed rate-limit lock lease (AV-002), single shared Memcached pool (AV-005), blocking counter workaround (AV-004), unbounded tag version keys (AV-003), limiter-only Cache telemetry and high-cardinality limiter tags (AV-007).
- PDR-010 / D-66: synchronous first Redis connection (AV-004).

The PDRs retain their original approval text as historical context and append the veto disposition. They must not be read as approval to ship these constraints as the final production architecture.

## Verification status

- Runtime code changes: none.
- Test changes or tests run: none.
- Build/CI: NOT RUN.
- Vetoes cleared: none.
- Next gate: obtain owner decisions AD-001 through AD-008, then create a prioritized remediation stage for AV-001 and AV-002 first and schedule AV-003 through AV-008. Do not mark the architecture accepted for production until the required verification evidence is present.


## Owner decisions pending — not architecture vetoes

The following items are source-confirmed observations or contract ambiguities. They are deliberately **not** represented as vetoes until the owner chooses the intended public contract. See [OWNER-DECISIONS.md](OWNER-DECISIONS.md).

- **AD-001 / MCD-011 — explicit zero TTL:** Memcached currently maps an explicit `TimeSpan.Zero` to the same Enyim expiration value as `null` (no expiry), while `CacheFake` rejects explicit zero. The `ICacheStore.SetAsync` contract does not currently define zero-TTL semantics clearly enough to declare one provider behavior incorrect. Owner decision required.
- Other API and lifecycle decisions that affect remediation sequencing are listed in the owner decision register. No answer is inferred from historical PDR wording.
