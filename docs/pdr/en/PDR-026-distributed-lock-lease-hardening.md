# PDR-026 — Distributed lock and lease hardening

**Status: APPROVED by the owner on 2026-11-26.** This record supersedes the Memcached-lock portion of AD-002 and authorizes the breaking removal described below. It does not authorize an implicit weaker lock implementation or a new dependency.

## Native-first verdict

.NET provides `SemaphoreSlim` for process-local coordination, but no built-in cross-process distributed lock with lease renewal, ownership-checked release, or fencing. A provider-specific lock adds value only when its guarantees are explicit and callers can observe lease loss. A cache provider is not automatically a safe lock provider.

## Laravel surface

Laravel exposes cache-backed atomic locks, including `lock`, `block`, `refresh`, and `forceRelease`, and uses locks for scheduler overlap prevention. The requirement is coordination across processes, not API-shape parity.

## Current Naravel behavior and evidence

- `ICacheLock` is the shared cache-lock abstraction. Redis and Memory have optional refresh and force-release capabilities; Memcached's implementation cannot perform owner-checked refresh.
- `CacheLockLease` renews a supported lease, enforces mandatory `MaxHold`, and signals loss through `LostToken`. This is cooperative cancellation, not fencing: a handler that ignores cancellation can continue after expiry.
- AD-002's planned scheduler integration (step 4b) and optional Redis fencing work (step 5) remain incomplete. Queue `WithoutOverlapping` integration does not prove scheduler correctness.
- Memcached's lock implementation has a GET/compare/DELETE release race and cannot safely implement refresh/fencing. Its cache-store functionality is independent and remains supported.
- A monotonic fencing token is useful only when the protected resource atomically rejects stale tokens. A token property on a lease, without enforcement at that resource, would not prevent stale writes.

## Decision

1. **Memcached is a cache provider only.** Remove the `MemcachedLock` public type, its lock-driver registration and its API-baseline entries. Remove public claims that Memcached provides locks. Keep Memcached cache-store support intact. This is an intentional pre-1.0 breaking change under OD-09 and must be listed in `CHANGELOG.md`.
2. **No silent fallback.** Do not substitute a process-local lock, fixed lease, or another provider when a Memcached lock is requested. Memcached lock resolution must fail explicitly because no Memcached lock provider is registered.
3. **Keep the existing lease contract honest.** Review and test renewal deadlines, hung/late provider calls, lease-loss notification, cancellation, maximum hold, release/disposal races, and scheduler overlap integration. A lease is not a fencing mechanism.
4. **Do not claim fencing.** Fencing-token implementation is deferred because the current lock contract alone cannot guarantee that an arbitrary protected resource validates tokens atomically. Document this limitation and stale-holder risk at the public API and relevant guides. Any future fencing feature requires a separate PDR proving end-to-end resource-side enforcement; a token field alone is insufficient.
5. **Preserve optional capabilities.** Do not add members to shipped `ICacheLock` or require custom lock drivers to implement refresh/fencing. Redis and Memory capabilities remain opt-in and must be documented according to tested behavior.

## Rejected alternatives

- Keeping Memcached as a best-effort lock: rejected because its release race can delete a successor's lock and it cannot safely renew ownership.
- Falling back to Memory or a fixed MaxHold lease: rejected because it silently changes cross-process coordination into weaker semantics.
- Advertising a fencing token without resource-side validation: rejected because it gives a false correctness guarantee.
- Adding a member to shipped `ICacheLock`: rejected because it breaks custom implementations unnecessarily.

## Compatibility and release

The removal of `MemcachedLock` and its registration is a public breaking change. Naravel is pre-1.0; OD-09 permits it when recorded in `CHANGELOG.md`. Remove the member from the shipped API baseline only under this explicit owner decision and record the change in `roadmap/AUDIT.md`. The Memcached cache store and its package remain.

## Verification required

- Source/API search proves there is no remaining `MemcachedLock` type, registration, API-baseline entry, sample, or documentation claim.
- Tests prove Memcached registration configures the cache store without registering any lock provider, and requesting a lock does not silently resolve to another provider.
- Lease and scheduler tests cover the failure and race cases named in decision 3, using deterministic time where appropriate.
- Independent review by a fresh session and mutation testing are required for concurrency-critical paths when available; neither may be claimed unless actually performed.
- `/usr/bin/python3 roadmap/check.py`, full solution restore/build/test, and relevant provider CI jobs are recorded accurately. If local .NET/NuGet verification is unavailable, state that and rely on a green CI run before merge.
- All changed documentation is source- and test-verified, all inbound links are updated, and the changelog and Audit record the breaking change and residual fencing limitation.
