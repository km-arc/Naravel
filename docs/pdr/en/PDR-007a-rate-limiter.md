# PDR-007a — Cache-backed rate limiting

**Native-first verdict:** `System.Threading.RateLimiting` is the right choice for in-process limits. It does not provide shared quotas across application instances; Naravel should add only that Cache-backed behavior, not wrap the native API wholesale.

Status: **Approved 2026-10-06.**

## Context

R04 completes the existing Cache work (PDR-007) with a RateLimiter. ASP.NET Core and .NET already provide useful local limiters, but their in-memory state is process-local. Applications that need one quota shared by several instances otherwise have to build provider-specific coordination and expiration logic themselves.

Laravel surface: `Illuminate\Cache\RateLimiter` (`attempt`, `tooManyAttempts`, `remaining`, `availableIn`, `clear`). Native baseline: `System.Threading.RateLimiting`, ASP.NET Core rate-limiting middleware, and Cache locks/stores. The useful gap is a small, async facade for a shared fixed-window counter.

## Proposed decision

Add a Cache-backed `IRateLimiter` to the existing Cache package; do not create another manager, provider package, or local in-process limiter. Leave local policies to `System.Threading.RateLimiting` and ASP.NET Core middleware. The Cache-backed limiter uses an explicitly selected Cache store and its lock capability to serialize updates to a bucket. If the selected store cannot provide the required lock semantics, registration or use must fail clearly rather than silently claim cross-process correctness.

The first version supports named fixed-window policies. One atomic `AttemptAsync` operation checks the limit and records a permitted attempt under the same lock, returning a typed decision with `Allowed`, `Remaining`, and `RetryAfter`. It accepts a caller-supplied subject key; it does not infer identity from HTTP context. `ClearAsync` removes the subject's current bucket. Bucket keys are namespaced under the configured Cache prefix and use a stable, non-reversible representation of the subject key so raw identifiers are not exposed in backend keys. As with fixed-window algorithms generally, a subject may consume up to twice the nominal limit across two adjacent windows in a short boundary interval; document this behavior rather than promising a rolling-window bound.

Add `ICacheStore.IncrementAsync(key, by, ttl)`. The TTL is applied only when the counter is first created; later increments must not move the window boundary. Memory protects the create/increment/expiry update with its per-key lock. Redis uses one Lua operation with `INCRBY`, `PTTL` and `PEXPIRE` only when the key has no expiry. Memcached first uses atomic `AddAsync(key, initialValue, ttl)` and, when the key already exists, its atomic increment operation. The limiter additionally uses the selected `ICacheLock` around checking the limit, updating the counter and maintaining window-start metadata.

Defer a limiter-specific Redis-atomic implementation. The first limiter implementation must use the existing cross-provider Cache/lock contracts; do not add a Redis-only fast path. Benchmark the selected hot path before merge. This does not change the provider-level atomicity contract of the existing `ICacheStore.IncrementAsync` operation.

Use Cache expiration for the window lifetime and define a single boundary rule: the first accepted request starts the window; the bucket expires at the end of that window. Rejected attempts do not extend it. Distributed correctness is conditional on the selected provider's lock guarantees and shared backend; Memory is explicitly process-local. No sliding-window, token-bucket, dynamic policy discovery, or Laravel-compatible global facade is proposed in this stage.

Proposed common case (two lines):

```csharp
services.AddNaravelRateLimiter("redis");
var decision = await limiter.AttemptAsync("login", subjectKey, 5, TimeSpan.FromMinutes(1), cancellationToken);
```

The exact registration and method names are proposals; keep the common path at no more than three user-code lines. Configuration-driven named policies may be added only if they remain compatible with Foundation conventions and do not make the simple path harder.

## Contracts and quality

- All backend operations are async and accept `CancellationToken`; no sync-over-async or reflection on hot paths.
- Existing `ICacheStore` and `ICacheLock` are reused. Do not add a new dependency or change Cache provider ownership without a separate PDR.
- A `Naravel.Cache.Testing` fake provides deterministic attempts, state inspection, and reset for application tests.
- Emit `Meter` measurements for allowed and rejected attempts and operation duration, plus an `ActivitySource` span. Metric dimensions are bounded (policy and outcome only); never tag with subject keys.
- Document that Cache-backed limits are distributed only when the configured Cache and lock drivers are shared and honor their advertised atomicity. Users who only need process-local protection should use native .NET rate limiting instead.

## Verification required after approval

Tests must cover fixed-window boundaries (including the documented up-to-2x adjacent-window burst), concurrent attempts at the limit, expiration, clear, cancellation, Memory's process-local scope, and rejection of a store without suitable lock support. Add contract coverage for every supported Cache driver and a benchmark for the new hot path; the benchmark must be reviewed before merge. Update English and Persian Cache docs and both parity tables. Keep the existing Cache dependency set unchanged.

## Alternatives considered

1. **Use only `System.Threading.RateLimiting`.** Recommended for local limits, but insufficient for a shared quota across instances.
2. **Build a second local limiter API around the .NET limiter.** Rejected; it duplicates a complete native solution and adds little value.
3. **Implement Redis-specific counters in the core package.** Rejected; it couples Cache to one provider and bypasses the existing Cache/lock contracts.
4. **Expose separate check and increment calls as the primary operation.** Rejected; concurrent callers could pass the check together. The common operation must evaluate and record under one lock.

## Approval

Owner approved this proposal on 2026-10-06. The Cache-lock-based fixed-window scope, provider guarantees required for distributed use, and proposed quickstart are approved for implementation. The owner-approved notes are: the fixed-window boundary can permit a burst up to 2x the nominal limit across adjacent windows; benchmark the selected path before merge; defer a limiter-specific Redis-atomic fast path and use the existing Cache/lock contracts.
