# Owner Architecture Decisions — Package Revalidation

Date: 2026-11-08  
Branch: `audit/package-architecture-revalidation`  
Baseline: `main` at `44e957ca3db6d9f15665fc8fa070f70522c163c9`

This register separates **architectural recommendations** from **owner-approved decisions**. Nothing in this file is approved by implication. The principal-architect vetoes AV-001 through AV-008 remain active; the questions below are not silently resolved by this review.

## AD-001 — Explicit zero TTL for `ICacheStore.SetAsync`

**Observed source behavior:** Memcached maps an explicit `TimeSpan.Zero` to protocol expiration zero (no expiry); `CacheFake` rejects explicit zero; the public interface documentation does not define the zero case. The shared provider contract does not test it.

**Decision required:**
- A. Explicit zero means immediate expiry, and every provider/fake must implement that behavior.
- B. Explicit zero is invalid and every provider/fake must reject it; `null` alone means no expiry.

**Architect recommendation:** B. It aligns with the existing fake and the TTL-counter overload's non-positive validation, and prevents an accidental zero from becoming a permanent Memcached entry.

**Status:** WAITING FOR OWNER.

## AD-002 — The guarantee promised by `ICacheLock`

**Observed source behavior:** `ICacheLock.AcquireAsync(name, ttl)` returns a token for a finite lease. The interface has no renewal or fencing token. Redis release is atomic token-checked, but a holder that continues executing after its lease expires is not prevented by Redis from mutating an unrelated protected resource. Memcached has the additional GET/compare/DELETE release race. `CacheRateLimiter` protects a multi-await critical section with a fixed 30-second lease.

**Decision required:**
- A. Define `ICacheLock` explicitly as a best-effort lease valid only while its TTL remains live; callers must not assume stale work is fenced. Remove or redesign consumers that need stronger correctness.
- B. Require correctness-grade distributed critical sections and add an enforceable fencing/lease capability; a protected resource must reject stale fencing tokens. Backends without that capability cannot provide that guarantee.

**Architect recommendation:** B for any cross-process critical section that protects durable/shared state. In either case, redesign `CacheRateLimiter` around one atomic backend-side state transition rather than a multi-call operation whose safety depends on a fixed lock TTL. Do not describe token-checked release alone as fencing.

**Status:** WAITING FOR OWNER.

## AD-003 — Scope/store flush semantics and Memcached administration

**Observed source behavior:** `ICacheStore.FlushAsync` is documented as flushing the store namespace. `ScopedCacheStore.FlushAsync` delegates to the backing store and escapes the scope. Memcached's implementation invokes cluster-wide `FlushAllAsync`.

**Decision required:**
- A. Preserve namespace semantics. Scope flush invalidates only that scope; provider implementations unable to do so fail explicitly. A separate, explicitly named administrative cluster-flush API may be introduced only if the owner wants it.
- B. Narrow the public contract so unsupported providers expose no flush on scoped views, while retaining an explicit store-level operation with its limitations.

**Architect recommendation:** A, with no implicit cluster-wide behavior. If Memcached cannot safely invalidate one namespace, ordinary flush should fail clearly. A destructive admin operation must be separate and explicitly named, permissioned/documented, and tested.

**Status:** WAITING FOR OWNER.

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
