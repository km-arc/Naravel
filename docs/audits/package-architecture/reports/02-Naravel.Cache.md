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

### [P2] CCH-007 — Cache store and lock operations have no package-level telemetry

- Remediation state: OPEN.
- Architecture disposition: VETO AV-007 — a production cache/lock abstraction must expose bounded operational telemetry.
- Evidence status: CONFIRMED BY SOURCE SEARCH.
- Evidence: src/Naravel.Cache/Diagnostics/CacheTelemetry.cs:8-12 defines only rate-limiter allowed/rejected counters and duration; current call sites are in CacheRateLimiter. No ICacheStore or ICacheLock operation emits a Cache activity/measurement. ROADMAP.md:116-117 says I/O modules emit a package Meter and ActivitySource spans.
- User impact: operators cannot observe cache hit/miss rates, write/delete failures, provider latency, lock contention/acquisition failures, or backend degradation through a consistent Naravel abstraction. Provider-native metrics do not cover Memory, all Memcached usage, and custom stores uniformly.
- Why the previous PDR scope is not sufficient: PDR-007a limited instrumentation to the rate limiter, but this leaves the Cache/Lock abstractions operationally opaque. The architectural requirement is observability of the module contract, not merely one feature inside it.
- Required correction: add low-cardinality measurements and activities at the Cache/Lock abstraction or a provider-neutral instrumentation seam. At minimum cover operation, outcome, and duration; include hit/miss and lock acquired/contended/failure distinctions where meaningful. Never emit cache keys, values, subjects, lock tokens, connection strings, or other secrets.
- Regression tests required: MeterListener and ActivityListener tests for hit/miss, write, remove, backend failure, lock outcome and duration; assert bounded tags and absence of raw keys/values/tokens.
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
