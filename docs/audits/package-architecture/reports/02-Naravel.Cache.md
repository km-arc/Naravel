# Review Report — Naravel.Cache

## Review metadata

- Package ID: `Naravel.Cache` (explicitly declared in the project file).
- Source project: `src/Naravel.Cache/Naravel.Cache.csproj`.
- Baseline commit: `847d605adb21f1ab4b6a60b13fd1685bb5893179` (`main`).
- Review date: 2026-11-07.
- Reviewer mode: principal-architect source review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; runtime verification pending).
- Files inspected: all source files under `src/Naravel.Cache/`; Cache test project and its test files; `docs/en/cache.md`; `docs/nuget/Naravel.Cache.md`; PDR-007 and PDR-007a; relevant ROADMAP quality requirements; shared package metadata.
- Verification performed: package metadata and project-reference inspection; public API shipped/unshipped review; source-to-doc comparison; tests and provider-gate review; static reproduction of tag-key collision.
- Verification NOT RUN: `dotnet restore`, `dotnet build`, `dotnet test`, live Redis/Memcached provider tests, race reproduction, mutation testing, and independent review.

## Executive assessment

- Overall risk: **Medium to High (P2)** until the two scope/tag correctness findings and the lock lease risk are resolved or explicitly accepted.
- Architectural fit: The package follows PDR-007's separation: core contracts and memory implementations in Cache, Foundation for managers, optional Redis/Memcached providers, and a distinct LockManager.
- Strengths: explicit store/lock contracts, optional provider dependencies, prefix-aware provider documentation, tag-version invalidation, explicit user/tenant scope wrappers, in-memory fake, driver/config reload tests, provider contract tests, and a separate Cache-backed fixed-window limiter.
- Summary: The highest-impact risks are that flushing a scoped view actually flushes its entire backing store, and that distinct tag sets can serialize to the same physical cache key. Time-dependent production paths also bypass the repository's TimeProvider rule. The Cache-backed limiter relies on a fixed 30-second lock lease without renewal or an operation deadline, so its atomicity is conditional on every critical section finishing before lease expiry.

## Package and dependency map

- Public package contract: `ICacheStore`, `ICacheLock`, `CacheManager`, `LockManager`, `CacheOptions`, `MemoryCacheStore`, `MemoryLock`, `ScopedCacheStore`, `TaggedCacheStore`, `CacheStoreExtensions`, `CacheLockExtensions`, `IRateLimiter`, `CacheRateLimiter`, and testing fakes.
- Package metadata: explicit `PackageId=Naravel.Cache`; target `net10.0` inherited from shared build metadata; `PackageReadmeFile=README.md`; package README packed from `docs/nuget/Naravel.Cache.md`; description and tags are present.
- Project references: `Naravel.Foundation` only.
- Direct package references: `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`, and `Microsoft.Extensions.Options.ConfigurationExtensions`.
- Optional providers: Redis and Memcached are separate packages; Cache core does not directly reference their client libraries.
- Registration: `AddNaravelCache` registers the Memory cache and lock, discovers configured named stores using `AddCacheDriver`, adds the default `ICacheStore` scoped wrapper, configures `CacheOptions`, and registers `CacheManager` and `LockManager`.
- Ownership: Foundation owns manager-created stores/locks; the direct `ICacheStore` default service is a scoped wrapper over the manager-owned driver. DI ownership is covered by integration tests.
- Public API baseline: `PublicAPI.Shipped.txt` contains the released API; `PublicAPI.Unshipped.txt` is empty in the inspected snapshot.

## Second-pass architecture revalidation — 2026-11-08

- Revalidation baseline: `main` at `44e957ca3db6d9f15665fc8fa070f70522c163c9`, after PR #243 reverted PR #241.
- Revalidation method: targeted source-level reinspection of the previously cited implementation paths against the current `main`; the earlier package-specific source baselines remain recorded above.
- Outcome: CCH-001 through CCH-008 remain OPEN. CCH-007 is expanded below to cover the caller-controlled limiter metric tag and its unbounded-cardinality risk.
- Verification NOT RUN in this pass: .NET restore/build/test, live provider integration, concurrency stress reproduction, mutation testing, and independent reviewer assessment. Source-level confirmation is not presented as runtime reproduction.
- No runtime code, tests, dependencies, public API, or roadmap stage was changed by this revalidation.

## Findings

### [P1] CCH-001 — ScopedCacheStore.FlushAsync escapes the scope

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; runtime regression test NOT RUN.
- Evidence: `src/Naravel.Cache/Scoping/ScopedCacheStore.cs:24-27` prefixes individual keys, but `:59` delegates `FlushAsync` directly to `_inner.FlushAsync`. Scopes are created by `CacheManager.ForUser` (`CacheManager.cs:23-27`) and `CacheManager.Scope` (`:30-33`).
- Affected contract: `ICacheStore.FlushAsync` when called on a scoped view.
- User impact: code that calls `FlushAsync` on one user/tenant scope can erase cache entries for every other scope and unscoped key in the same backing store. The blast radius is provider-dependent: Memory removes keys tracked by that store instance; Redis flushes the configured Redis prefix, not the extra scope prefix; Memcached clears the whole shared cluster, as the current guide and PDR document.
- Trigger / failure scenario: create `tenant-a = manager.Scope("tenant:a")` and `tenant-b = manager.Scope("tenant:b")`; write a key in each; call `tenant-a.FlushAsync()`. The current wrapper calls the underlying store's full `FlushAsync`, so tenant-b data is removed too.
- Why existing tests do not catch it: `ScopedCacheStoreTests` covers key isolation and tag composition, but not `FlushAsync` on a scope. `CacheStoreTests` tests backing-store flush, not scope-specific flush.
- Recommended correction: define the contract explicitly. Prefer a scope-aware flush where a provider can safely delete only the wrapper prefix; where a provider cannot do that (notably Memcached), either fail explicitly or document a clearly named whole-store operation rather than silently widening a scoped operation. Do not silently change public semantics without owner/PDR review.
- Regression tests required: with Memory, write through two scoped views plus the base store, flush one scope, and assert the other scope and base key survive. Add provider-specific contract tests for Redis and explicitly assert/document the Memcached limitation.
- Public API / PDR / release impact: potentially public behavioral change; review PDR-007 and obtain owner approval before changing the contract.

### [P2] CCH-002 — Tagged cache key encoding collides for distinct tag sets

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY STATIC REPRODUCTION; runtime test NOT RUN.
- Evidence: `src/Naravel.Cache/Tagging/TaggedCacheStore.cs:27-39`, especially `:36` (tag/version interpolation) and `:39` (colon-joined serialization). The constructor trims tags but does not escape or reject `:` / `@` (`:17-20`).
- Affected contract: `ICacheStore` views created through `Tags(...)`.
- User impact: two different tag sets can address the same underlying cache key. One tagged view can therefore read a value written through another view, and flushing one tag group will not have the invalidation behavior callers expect for the colliding view.
- Concrete collision at initial version zero, with the same data key `key`: tag set `["x@0:y"]` serializes to `x@0:y@0:key`; tag set `["x", "y"]` serializes to exactly `x@0:y@0:key`. Both tag sets are accepted by the current constructor.
- Why existing tests do not catch it: `TaggedCacheStoreTests` covers single-tag invalidation, stable initial versions, and concurrent writes, but no adversarial tag characters or distinct tag-set collision cases.
- Recommended correction: encode the tag tuple unambiguously (length-prefix or escaped components, or hash a canonical structured representation); keep the serialization stable across providers and release versions. Avoid simply banning characters unless the public tag contract is explicitly tightened.
- Regression tests required: write under `["x@0:y"]`, prove `["x", "y"]` cannot read it, flush one set, and prove only the intended set is invalidated. Add property/fuzz cases for delimiters, empty/whitespace tags, order normalization, and duplicate tags.
- Public API / PDR / release impact: implementation-only if the accepted tag input contract remains unchanged; any restriction on accepted tag characters requires owner/PDR review.

### [P2] CCH-003 — Production time paths bypass injected TimeProvider

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE.
- Evidence: `src/Naravel.Cache/Locks/MemoryLock.cs:22,66,69,95` uses `DateTime.UtcNow`; `src/Naravel.Cache/CacheLockExtensions.cs:48,55` uses `DateTime.UtcNow` and `:56` uses an unqualified `Task.Delay`; `src/Naravel.Cache/Stores/MemoryCacheStore.cs:114` uses `DateTimeOffset.UtcNow`; `src/Naravel.Cache/RateLimiting/CacheRateLimiter.cs:49,73` uses `DateTimeOffset.UtcNow`. The repository rule in `AGENTS.md` → “Time-dependent code” requires injected `TimeProvider` and deterministic test clocks.
- Affected behavior: Memory lock leases and expiry sweeps, lock wait deadlines, Memory counter-expiry bookkeeping, fixed-window RetryAfter calculations.
- User impact: wall-clock adjustments can cause a Memory lock to expire too early or remain held too long; a clock jump can make `BlockAsync` return before or after the requested wait; Memory counter-expiry bookkeeping can drift from cache entry expiry; and a shared rate limiter computes RetryAfter using a wall clock from whichever instance handles the request, so host clock skew can make RetryAfter inaccurate.
- Test gap: the production-path tests use real `Task.Delay` windows (for example `CacheLockTests`, `CacheStoreTests`, and `CacheRateLimiterTests`). `CacheFake` already accepts a `TimeProvider` and its tests use a manual clock, but that clock does not control `MemoryLock`, `MemoryCacheStore`, or `CacheRateLimiter`.
- Recommended correction: inventory each use and separate wall-clock timestamps from elapsed-time/lease calculations. Inject a `TimeProvider` where wall time is required; use monotonic timestamp measurement for elapsed lock waits/leases where appropriate; route delays through the provider. Check how the `IMemoryCache` expiry clock is coordinated before altering the increment-expiry map. Do not mechanically replace every occurrence.
- Regression tests required: manually advance time for MemoryLock acquire/expire/sweep, `BlockAsync` wait timeout and cancellation, Memory TTL increments, and RateLimiter fixed-window boundaries/RetryAfter. Remove real sleeps from tests where the fake can express the boundary.
- Public API / PDR / release impact: some current constructors and extension methods do not accept `TimeProvider`; introducing injection may add public overloads or change DI wiring. Obtain owner approval for any public API surface before implementation.

### [P1] CCH-004 — Cache-backed limiter lock lease can expire during its critical section

- Remediation state: OPEN.
- Evidence status: CONFIRMED DESIGN RISK; deterministic reproduction NOT RUN.
- Evidence: `src/Naravel.Cache/RateLimiting/CacheRateLimiter.cs:13` fixes `LockTtl` at 30 seconds; `:47-75` performs multiple awaited store reads/writes inside the callback; `:100-103` runs that callback under `BlockAsync`. `ICacheLock.AcquireAsync` treats TTL as a lease and there is no renewal contract on `ICacheLock`.
- Affected guarantee: PDR-007a says the limiter checks and records a permitted attempt under one lock so concurrent callers cannot pass the check together.
- User impact: if a backing store or custom driver call stalls for longer than 30 seconds, the lock lease can expire while the first callback is still running. A second application instance can acquire the same lock and enter the critical section before the first one completes, potentially admitting more than the configured limit.
- Why existing tests do not catch it: limiter concurrency tests exercise fast in-memory operations; server tests exercise ordinary Redis/Memcached latency. No test pauses an operation beyond the lock lease while a second replica attempts the same bucket.
- Recommended correction: establish a verifiable upper bound for the complete critical section below the lease, or use a renewable/fenced lease design. If relying on provider command timeouts, configure and test them so the total multi-call operation cannot outlive the lease. Do not assume caller cancellation always interrupts an in-flight provider command.
- Regression tests required: use two limiter instances and a controllable store/lock; pause the first operation after the count read until the lease expires, run a second attempt, then resume the first and assert the configured limit is never exceeded. Test cancellation and provider timeout behavior too.
- Public API / PDR / release impact: potentially changes distributed correctness guarantees and lock semantics; owner approval/PDR review required before implementation.

### [P3] CCH-005 — RememberAsync non-cancellation overload does not validate a null factory

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE.
- Evidence: `src/Naravel.Cache/CacheStoreExtensions.cs:9-15` wraps `factory` in `_ => factory()` without validating the original delegate; validation exists only in the token-aware overload at `:18-28`. `RememberForeverAsync` forwards through the same overload at `:38-43`.
- User impact: a null factory can appear to succeed when the key is already cached, but throws `NullReferenceException` on a cache miss instead of failing immediately with `ArgumentNullException`. The same inconsistency affects `RememberForeverAsync`.
- Why existing tests do not catch it: current tests cover valid delegates and cache hits/misses, not a null delegate in both states.
- Recommended correction: validate the original delegate before wrapping/forwarding, preserving one consistent argument contract.
- Regression tests required: for both `RememberAsync` and `RememberForeverAsync`, assert null factory throws `ArgumentNullException` regardless of cache hit/miss.
- Public API / PDR / release impact: implementation-only.

### [DOC] CCH-006 — Cache quickstart still teaches direct DateTime.UtcNow

- Remediation state: OPEN.
- Evidence status: CONFIRMED.
- Evidence: `docs/en/cache.md:24` uses `DateTime.UtcNow` in the quickstart; the current package README `docs/nuget/Naravel.Cache.md` uses `TimeProvider.System` registration and `clock.GetUtcNow()` in its corresponding example. The agent's Time-dependent code rule also prohibits introducing direct system-clock calls in application/runtime guidance.
- User impact: the authoritative module guide teaches a pattern that conflicts with current repository rules and disagrees with the updated NuGet package page.
- Why existing tests do not catch it: `DocsExampleTests.cs` covers operations, `RememberAsync`, scopes/tags, locks, and rate limiting, but does not compile or run this exact `/time` quickstart snippet.
- Recommended correction: update the module quickstart to use an injected `TimeProvider`, and make the executable documentation test compile the exact registration and endpoint pattern. Keep the NuGet page and guide synchronized.
- Regression tests required: compile/run the quickstart snippet in `DocsExampleTests.cs`; confirm no direct `DateTime.UtcNow` remains in maintained Cache examples.
- Public API / PDR / release impact: documentation/test-only.

### [P2] CCH-007 — Cache store and lock operations lack safe provider-neutral telemetry

- Remediation state: OPEN.
- Architecture disposition: VETO AV-007 — production Cache/Lock telemetry must be useful and bounded.
- Evidence status: CONFIRMED BY SOURCE SEARCH AND TARGETED SECOND-PASS SOURCE REVIEW.
- Evidence: `src/Naravel.Cache/Diagnostics/CacheTelemetry.cs` defines rate-limiter allowed/rejected counters and duration, but no `ICacheStore` or `ICacheLock` operation emits a Cache activity/measurement. In addition, `src/Naravel.Cache/RateLimiting/CacheRateLimiter.cs` accepts a caller-controlled `limiter` string after only non-empty validation and adds it to metric tags and the activity as `naravel.cache.rate_limiter`. The API does not constrain policy names to a finite registered set.
- Affected contract: Cache package telemetry, including `IRateLimiter.AttemptAsync`.
- User impact: operators cannot observe cache hit/miss rates, write/remove failures, provider latency, lock contention/acquisition failures, or backend degradation through a consistent Naravel abstraction. Separately, applications that derive the limiter name from request/user/tenant data can cause unbounded metric cardinality and increased telemetry storage/query cost.
- Why the previous PDR scope is not sufficient: PDR-007a limited instrumentation to the rate limiter, but that leaves the Cache/Lock abstractions opaque. The current rate-limiter tag also assumes, without enforcing, that limiter names are low-cardinality.
- Required correction: add low-cardinality measurements and activities at the Cache/Lock abstraction or a provider-neutral instrumentation seam. At minimum cover operation, outcome, and duration; include hit/miss and lock acquired/contended/failure distinctions where meaningful. For limiter telemetry, emit only a finite, registered or otherwise demonstrably bounded policy identifier; never use request-, user-, tenant-, subject-, key-, value-, token-, credential-, or connection-derived dimensions.
- Regression tests required: MeterListener and ActivityListener tests for hit/miss, write, remove, backend failure, lock outcome and duration; assert bounded tags and absence of raw keys/values/tokens. Add a test supplying many distinct caller-controlled limiter strings and prove that metric dimensions remain bounded.
- Public API / PDR / release impact: observability contract and PDR amendment; no high-cardinality dimensions or new provider dependency is acceptable.

### [P2] CCH-008 — Per-tag version metadata grows without a safe reclamation lifecycle

- Remediation state: OPEN.
- Architecture disposition: VETO AV-003 — unbounded metadata growth is not an acceptable final-state trade-off.
- Evidence status: CONFIRMED BY SOURCE.
- Evidence: src/Naravel.Cache/Tagging/TaggedCacheStore.cs:9, 33, 76; FlushAsync increments a per-tag version through the no-TTL ICacheStore.IncrementAsync overload.
- User impact: every distinct tag ever flushed leaves a version counter in the backing store indefinitely. A long-running application that creates tags from order IDs, user IDs, or other high-cardinality values grows metadata monotonically even after tagged values expire or are evicted.
- Why a simple TTL is not a safe correction: if a tag-version key expires or is evicted and reads default it to version zero, an old value written under version zero can become visible again. The system cannot reclaim a tag generation while any old unexpired value may still exist.
- Required correction: define a safe bounded lifecycle. Options include a bounded/reusable tag model with enforced cardinality, a generation/epoch design whose control state cannot be evicted or reset while old entries remain reachable, or an explicit finite-lifetime contract for tagged values plus verified reclamation. A blind TTL on version keys is rejected.
- Regression tests required: high-cardinality tag stress over a representative lifecycle; reclamation tests proving old values never reappear after metadata cleanup; include tagged values with no TTL or reject them under an explicit documented contract.
- Public API / PDR / release impact: PDR/storage algorithm and possibly API contract change; this is a mandatory scalability correction, not an accepted risk.

## Test assessment

- Existing tests inspected: `CacheStoreContractTests`, `CacheStoreTests`, `CacheLockTests`, `CacheManagerIntegrationTests`, `CacheRateLimiterTests`, `RateLimiterServerTests`, `ScopedCacheStoreTests`, `TaggedCacheStoreTests`, `CacheFakeTests`, `DocsExampleTests`, Redis/Memcached provider test projects and `Naravel.Cache.Tests.csproj`.
- Missing regression coverage: scoped flush isolation; delimiter-collision tag sets; injected-clock production-path tests; lock lease expiry during a limiter callback; null factories on cache hit/miss; exact Cache quickstart compilation; cache-operation telemetry if the S3 requirement is retained; tag metadata retention/reclamation decision.
- Provider integration tests: Redis and Memcached cases are environment-gated and run in CI; they were inspected but not executed in this audit.
- Tests actually run: none in this source-inspection pass.
- Tests NOT RUN: Cache test project, full solution build/test, Redis/Memcached services, benchmark, roadmap check, and package-consumer smoke.
- Mutation testing: NOT RUN.
- Independent review: fresh-session review NOT RUN; the tag collision, scoped flush, lock lease, and time-dependent paths should receive an independent review before remediation is accepted.

## Documentation and packaging assessment

- Project metadata: `PackageId=Naravel.Cache`; `PackageReadmeFile=README.md`; `docs/nuget/Naravel.Cache.md` is packed as root README.
- Package page now has a `Use` section with `TimeProvider` and a clock-injected endpoint; the full guide's quickstart still uses `DateTime.UtcNow`, so the two maintained docs are inconsistent.
- The Cache guide accurately warns that `RememberAsync` is not atomic and documents the fixed-window up-to-2x boundary, Redis prefix-scoped flush, Memcached cluster-wide flush, and provider-specific lock caveats.
- Scope-specific flush behavior is not called out: a `ScopedCacheStore` delegates flush to the underlying store.
- Tag-version metadata persistence is not called out.
- Public API baseline inspected; no pending Unshipped entries.
- No package or dependency addition is recommended by this report.

## Risk summary and next action

- P0: none established.
- P1: CCH-001 and CCH-004 (Architecture Vetoes AV-001 and AV-002).
- P2: CCH-002, CCH-003, CCH-007 and CCH-008 (AV-003 and AV-007).
- P3: CCH-005.
- DOC: CCH-006.
- OBS: none.
- Recommended next task: treat scope-safe flush and lease/fencing correctness as release blockers. Bound tag metadata growth and add provider-neutral Cache/Lock telemetry before declaring the module production-ready. No runtime remediation has been made in this audit branch.

## Plain-language explanation

The findings below describe risks in cache scope handling, key naming, time, locks, input validation and operational visibility. Unless stated otherwise, they were identified by source inspection; the live provider and runtime tests listed in the report were not run in this audit pass.

### CCH-001 — Flushing one user or tenant scope can clear the whole cache store

- **In everyday words:** A scoped cache view limits normal key access, but its flush operation calls the full backing store's flush. A caller can therefore erase data outside its own scope.
- **A realistic example:** Tenant A and tenant B each store `profile`. Tenant A calls `FlushAsync` on its scoped cache. The current wrapper asks the underlying store to flush, so tenant B's data may be removed too.
- **What we checked:** `src/Naravel.Cache/Scoping/ScopedCacheStore.cs`, especially `FlushAsync` around line 59, and scope creation in `CacheManager.ForUser` / `CacheManager.Scope`.
- **Why that proves the risk:** Individual keys receive a scope prefix, but flush does not use that prefix; it delegates directly to the whole store.
- **What is not yet proven here:** The code path is confirmed by inspection. A two-tenant runtime regression was not run. The exact blast radius depends on the backing provider.
- **Safe outcome and proof:** Flushing a scoped view must never remove another scope's or the base store's entries. Test two scopes plus the base store; flush one scope and prove the other values remain. For providers that cannot safely flush one scope, the operation must fail clearly or be separately named as a whole-store action. Owner/PDR approval is required before changing this public behavior.

### CCH-002 — Different tag groups can produce the same cache key

- **In everyday words:** The code joins tag names into one text key without escaping separators. Some different tag groups can therefore become indistinguishable.
- **A realistic example:** The tag list `["x@0:y"]` and the list `["x", "y"]` can both produce `x@0:y@0:key` for the same data key.
- **What we checked:** `src/Naravel.Cache/Tagging/TaggedCacheStore.cs`, around lines 17–20 and 27–39, where tags are accepted and joined.
- **Why that proves the risk:** Both accepted inputs are converted to the same physical key, so one tagged view can read or invalidate data written through the other.
- **What is not yet proven here:** The report records a static collision example, not a test run against a live provider.
- **Safe outcome and proof:** Different tag lists must always produce different identities. Add the exact collision regression and cases with separators, empty tags, duplicate tags and tag ordering. Preserve the accepted tag contract unless the owner approves a restriction.

### CCH-003 — Some cache time calculations ignore the application's testable clock

- **In everyday words:** Parts of Cache read the computer's clock directly. Tests and applications cannot control those time decisions through the repository's `TimeProvider` rule.
- **A realistic example:** A Memory lock is expected to last ten seconds. The system clock moves unexpectedly, so the lock may expire too early or stay held too long.
- **What we checked:** Direct clock reads appear in `MemoryLock.cs`, `CacheLockExtensions.cs`, `MemoryCacheStore.cs` and `CacheRateLimiter.cs`; the report identifies the relevant lines.
- **Why that proves the risk:** These production paths use system time rather than an injected clock, while `AGENTS.md` requires injected `TimeProvider` for time-dependent code.
- **What is not yet proven here:** The audit did not simulate clock changes or run deterministic timing tests for these production classes.
- **Safe outcome and proof:** Each time-based behavior must use the approved clock source and distinguish real timestamps from elapsed-time measurement. Use a manually advanced clock to test lock expiry, waiting, cache counter expiry and rate-limit boundaries. Do not replace every clock call mechanically; check how cache expiry is coordinated first.

### CCH-004 — A rate-limit lock can expire while its protected work is still running

- **In everyday words:** The limiter uses a lock that lasts 30 seconds, but the work inside the lock has no guaranteed 30-second deadline. If it takes longer, another process may enter while the first one is still updating the count.
- **A realistic example:** Application A reads the counter and pauses while Redis is slow. The 30-second lock expires. Application B acquires the lock and also reads and updates the same counter before A has finished.
- **What we checked:** `src/Naravel.Cache/RateLimiting/CacheRateLimiter.cs`, where `LockTtl` is fixed at 30 seconds and several awaited reads/writes happen inside `BlockAsync`. The `ICacheLock` contract has no renewal operation.
- **Why that supports the risk:** The protected sequence can outlive the lock, and the contract provides no way to extend the lock while that sequence is running.
- **What is not yet proven here:** This is a design risk from source inspection. The report does not claim a reproduced double-admission or load-test result.
- **Safe outcome and proof:** The whole critical section must finish within a proven limit, or the lock must be safely renewable/fenced. Test two limiter instances with a controllable store: pause the first beyond the lease, run the second, then prove the configured limit is never exceeded. Owner/PDR approval is required before changing lock semantics.

### CCH-005 — A missing cache factory can fail only when the cache is empty

- **In everyday words:** One overload accepts a factory function but does not check that it exists before wrapping it. A null factory can appear harmless on a cache hit and fail later on a cache miss.
- **A realistic example:** A caller accidentally passes `null`. The key is already cached, so the call succeeds. Later the key expires, and the same call throws `NullReferenceException` instead of rejecting the bad argument immediately.
- **What we checked:** `src/Naravel.Cache/CacheStoreExtensions.cs`, lines 9–15 and 38–43; the token-aware overload validates the delegate but the non-token overload does not.
- **Why that proves the issue:** The non-token overload captures the original value in a lambda without checking it, so the error is postponed until the lambda runs.
- **What is not yet proven here:** The source path is clear, but no null-factory regression test was run in this audit.
- **Safe outcome and proof:** Both `RememberAsync` and `RememberForeverAsync` must throw `ArgumentNullException` immediately for a null factory, regardless of cache hit or miss. Add tests for both states.

### CCH-006 — The Cache guide teaches a direct system-clock call

- **In everyday words:** The maintained quickstart shows application code reading the system clock directly, even though the repository requires time to be supplied through `TimeProvider`.
- **A realistic example:** A developer copies the quickstart's `DateTime.UtcNow` call into an endpoint and later cannot control that time in a deterministic test.
- **What we checked:** `docs/en/cache.md` around line 24 uses `DateTime.UtcNow`, while `docs/nuget/Naravel.Cache.md` already shows `TimeProvider.System` and `clock.GetUtcNow()`.
- **Why that proves the mismatch:** The two maintained examples teach different time patterns, and the module guide conflicts with the repository rule.
- **What is not yet proven here:** The exact quickstart snippet was not compiled as part of this review.
- **Safe outcome and proof:** Update the guide to show an injected clock and compile/run that exact example in `DocsExampleTests`. Check that maintained Cache examples do not introduce direct system-clock reads.

### CCH-007 — Operators cannot observe most cache/lock activity, and limiter names can create excessive metric labels

- **In everyday words:** The package reports some rate-limit numbers, but not the basic cache and lock operations. Also, a caller can put an unlimited number of different names into a metric label, making monitoring data grow out of control.
- **A realistic example:** An application uses each tenant ID as the limiter name. Every tenant creates a new metric dimension. At the same time, operators cannot use Naravel's Cache telemetry to see cache misses, slow writes or lock contention.
- **What we checked:** `src/Naravel.Cache/Diagnostics/CacheTelemetry.cs` defines rate-limiter metrics but no Cache-store or Cache-lock operation measurements. `src/Naravel.Cache/RateLimiting/CacheRateLimiter.cs` accepts the caller's `limiter` string and adds it to metric/activity tags without limiting the possible values.
- **Why that proves the risk:** There is no common telemetry for the basic operations, and the caller-controlled label is not limited to a finite set.
- **What is not yet proven here:** No production telemetry load or cardinality stress test was run.
- **Safe outcome and proof:** Add provider-neutral measurements for operation, result and duration; use only bounded policy names in metric labels. Test with `MeterListener` and `ActivityListener`, including many distinct caller-supplied names, and prove raw keys, values and user data never become tags.

### CCH-008 — Tag-version records can grow forever

- **In everyday words:** Every time a tag group is flushed, the cache stores another version record. There is no safe cleanup rule, so a system that creates many one-off tags can keep growing this metadata forever.
- **A realistic example:** An application uses order IDs as tags. Each order is flushed once. Even after the order's cached data has expired, its tag-version record remains.
- **What we checked:** `src/Naravel.Cache/Tagging/TaggedCacheStore.cs`, especially the version increment during `FlushAsync`, which uses the no-expiry increment overload.
- **Why that proves the risk:** The code creates persistent per-tag records and the package has no matching reclamation lifecycle.
- **What is not yet proven here:** The report is based on source inspection; a long-running heap/store growth test was not run.
- **Safe outcome and proof:** Define a bounded lifecycle without allowing an old cached value to become visible again after a version record is removed. Test high-cardinality tags and safe cleanup, including values without TTL. A blind TTL on version records is unsafe because resetting a version can make an old value appear current again.

### Overall plain-language status

The Cache report records source-level findings and a static example for the tag-key collision. It does not claim that the live Redis or Memcached tests, timing reproductions, concurrency stress or mutation tests were run. Owner decisions remain required where the public behavior or distributed-lock guarantee would change.
