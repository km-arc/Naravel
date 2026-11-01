# PDR-024 — Shared store for the route `throttle` middleware

**Native-first verdict:** `System.Threading.RateLimiting` is the right limiter for one process and stays the default. It cannot share a quota between processes, and no native .NET component does. Naravel already has the shared piece (the Cache-backed `IRateLimiter`, PDR-007a); what is missing is the connection between it and the route middleware.

Status: **Proposed (draft, 2026-11-01). Not approved, not scheduled.** `NEXT` is R17; if approved this becomes a stage after R17 and the owner assigns its id.

## Context

`throttle` (PDR-009) is built on `PartitionedRateLimiter` with one fixed-window limiter per partition, held in `ThrottleLimiterRegistry` in the memory of the process. The partition is `policy|endpoint|subject`, where the subject is the authenticated name identifier, or the remote IP when anonymous. PDR-009 left a shared store to "a separate PDR (after `Naravel.Cache`)"; this is that PDR.

| Situation | What happens today |
|---|---|
| N instances behind a load balancer | each counts alone: a caller can send up to N times the limit per window and sees 429 on one server and 200 on another |
| An instance restarts, crashes, scales in or out | its counters start empty; callers get a fresh window |
| Anonymous callers behind a proxy without forwarded headers | all share the proxy's IP, one bucket (the opposite error; a deployment setting, native ASP.NET Core, not part of this decision) |

This is documented in `docs/en|fa/routing.md` ("Throttle behind more than one server") and its current behaviour is pinned by a test (`Throttle_counts_per_application_instance_and_starts_empty_after_a_restart`). The in-process behaviour is not a defect for a single instance or for damping abuse; it is a limit of the design.

What exists to build on: `IRateLimiter.AttemptAsync(limiter, subjectKey, maxAttempts, window, ct)` returns `Allowed`, `Remaining` and `RetryAfter`, which is everything the middleware needs for `X-RateLimit-*` and `Retry-After`. On a shared Redis store it is one quota for all instances. The login example in `cache.md` already uses it in a handler. What it costs: each attempt takes a lock, reads two keys, increments, writes the window start on the first attempt of a window and releases the lock, so five to six store calls per request. PDR-007a deferred a Redis-only fast path on purpose; for a middleware that runs on every request of a route this must be measured again.

## Options

| | Option | Routing depends on | User code |
|---|---|---|---|
| **A (recommended)** | Routing gets a small `IThrottleStore` seam. The default implementation is today's in-process limiter, unchanged. A small adapter over `IRateLimiter` ships in a new package (working name `Naravel.Routing.Cache`) that depends on Routing and Cache | nothing new | `services.AddNaravelSharedThrottle("redis");` (one line next to `AddNaravelRouting`); policies and `throttle:login` stay as they are |
| B | Routing references `Naravel.Cache` and a `ThrottleStore` option selects a named store | `Naravel.Cache` and, through it, Foundation and the memory cache package | `o.ConfigureThrottleStore("redis")` |
| C | Build nothing; keep documenting the limits and the recipes (own `IRouteMiddleware` over `IRateLimiter`, or limit at the proxy or gateway) | nothing | as today |

Why A: Routing stays a package that needs only the ASP.NET Core framework reference (PDR-009), nobody who only wants the in-process throttle pulls Cache, and the direction of dependency (Routing.Cache → Routing, Cache) follows hard rule 3. B is less code but adds Cache to every Routing user. C costs nothing and is already the situation described in the docs; it leaves every team to write the same 15-line middleware, including the 429 and header handling.

## Decision (if approved: option A)

- `IThrottleStore` (public, in Routing): `ValueTask<ThrottleDecision> AttemptAsync(string partitionKey, int permitLimit, TimeSpan window, CancellationToken ct)`. `ThrottleDecision` carries `Allowed`, `Remaining`, `RetryAfter`. The partition key is the same `policy|endpoint|subject` string used today.
- `ThrottleMiddleware` calls the store; the headers and the 429 response are unchanged for both stores. The in-process store keeps `PartitionedRateLimiter` and its behaviour bit for bit; the existing tests must pass without edits.
- `Naravel.Routing.Cache` adds `AddNaravelSharedThrottle(storeName)`, an `IThrottleStore` over `IRateLimiter`. It adds no third-party dependency.
- Semantics: fixed window, first admitted request starts the window, the documented up to 2x burst at a boundary stays. No sliding window or token bucket here.
- **Failure policy (owner decision).** When the store throws (backend down, the 5 second lock wait ends in `TimeoutException`): *fail open* (admit, log a warning, count `naravel.routing.throttle.store_failures`) or *fail closed* (respond 503 with `Retry-After`). Recommendation: a per-policy setting, default **fail open**, because the throttle protects the application and a Redis outage should not take every throttled route down; credential endpoints choose fail closed explicitly. Laravel throws, which surfaces as a 500; that is not proposed.
- Telemetry: the shared path reuses the Cache limiter's allowed/rejected meters; Routing adds the failure counter above. Bounded dimensions only (policy and outcome), never the subject.
- Fake: `RateLimiterFake` (Naravel.Cache.Testing) already stands in for the shared limiter; the in-process store needs none.

## Open questions for the owner

1. Option A, B or C.
2. Failure policy: default fail open, fail closed, or no default (the caller must choose per policy).
3. The cost. If the benchmark shows five to six Redis calls per request is too slow for a per-request middleware, a single-call Redis implementation of `IThrottleStore` (one Lua script) would be a separate, Redis-only decision that reopens the PDR-007a deferral. Approve option A with or without that fast path.
4. Maintenance mode state is process-local for the same reason (`docs/en/routing.md`, limitations). It is not covered here; say if it should get its own PDR.

## Not decided here

Sliding-window and token-bucket algorithms, per-user dynamic limits, a cross-instance maintenance flag, and any change to how the client address is determined (forwarded headers stay native ASP.NET Core).

## Verification required after approval

Tests: the in-process store passes the existing throttle tests unchanged; a store contract run on the in-process store, on the Cache adapter over the memory store and, in the env-gated provider job, over Redis with two application hosts that must share one quota (the existing two-host test pattern, with the expectation inverted); restart keeps the quota on Redis; fail open and fail closed on a store that throws; headers and 429 identical for both stores; cancellation. A benchmark of the in-process path (must not regress) and of the shared path on Redis, reviewed before merge. Docs EN/FA, `CHANGELOG.md`, public API files. Build, tests and benchmarks are verified by CI only.

## Alternatives considered

1. **Replace the in-process limiter with the shared one.** Rejected: makes every Routing user pay Redis round trips and a hard dependency for something a single instance does better in memory.
2. **Make `ThrottleLimiterRegistry` itself pluggable through `Manager<TDriver,TOptions>`.** Rejected: Routing is not driver-based (PDR-009); one small interface is enough.
3. **Distributed partitions through a custom `PartitionedRateLimiter` over Redis.** Rejected: the native limiter's leases and replenishment timers assume local state; the Cache limiter already has the right shape and tests.
