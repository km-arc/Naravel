# PDR-024 — Shared store for the route `throttle` middleware

**Native-first verdict:** `System.Threading.RateLimiting` is the right limiter for one process and stays the default. It cannot share a quota between processes, and no native .NET component does. Naravel already has the shared pieces in Cache (an atomic counter with a first-write expiry, and the Cache-backed `IRateLimiter`, PDR-007a); what is missing is the connection between them and the route middleware.

Status: **Proposed (draft, 2026-11-01). Not approved, not scheduled.** `NEXT` is R17; if approved this becomes a stage after R17 and the owner assigns its id.

## Context

`throttle` (PDR-009) is built on `PartitionedRateLimiter` with one fixed-window limiter per partition, held in `ThrottleLimiterRegistry` in the memory of the process. The partition is `policy|endpoint|subject`, where the subject is the authenticated name identifier, or the remote IP when anonymous. PDR-009 left a shared store to "a separate PDR (after `Naravel.Cache`)"; this is that PDR.

| Situation | What happens today |
|---|---|
| N instances behind a load balancer | each counts alone: a caller can send up to N times the limit per window and sees 429 on one server and 200 on another |
| An instance restarts, crashes, scales in or out | its counters start empty; callers get a fresh window |
| Anonymous callers behind a proxy without forwarded headers | all share the proxy's IP, one bucket (the opposite error; a deployment setting, native ASP.NET Core, not part of this decision) |

This is documented in `docs/en|fa/routing.md` ("Throttle behind more than one server") and its current behaviour is pinned by a test (`Throttle_counts_per_application_instance_and_starts_empty_after_a_restart`). The in-process behaviour is not a defect for a single instance or for damping abuse; it is a limit of the design.

What exists to build on: `IRateLimiter.AttemptAsync(limiter, subjectKey, maxAttempts, window, ct)` returns `Allowed`, `Remaining` and `RetryAfter`, which is everything the middleware needs for `X-RateLimit-*` and `Retry-After`. On a shared Redis store it is one quota for all instances. The login example in `cache.md` already uses it in a handler. What it costs: each attempt takes a lock, reads two keys, increments, writes the window start on the first attempt of a window and releases the lock, so five to six store calls per request. Worse for a throttle, the lock serialises every attempt of one subject and waits up to 5 seconds for it: the caller the throttle exists to stop (one client sending a burst) queues its own requests on the lock, so the throttle adds load exactly when it is needed. That design suits an explicit "attempts" counter such as a login handler; it is the wrong shape for a middleware on the request path.

A better shape already exists on the Cache contract. `ICacheStore.IncrementAsync(key, by, ttl)` is atomic and sets the expiry only when the key is created, on every provider (Redis: one Lua call; Memcached: `add` then `increment`; Memory). With the window index in the key (`bucket = floor(now / window)`), a fixed window needs **one** such call, no lock and no stored window start: `Allowed = count <= limit`, `Remaining = limit - count`, `RetryAfter = window - (now mod window)`. It is not Redis-specific, so it does not reopen the PDR-007a deferral of a Redis-only fast path.

## Options

| | Option | Routing depends on | User code |
|---|---|---|---|
| **A (recommended)** | Routing gets a small `IThrottleStore` seam. The default implementation is today's in-process limiter, unchanged. A small adapter over the Cache counter (`ICacheStore.IncrementAsync`) ships in a new package (working name `Naravel.Routing.Cache`) that depends on Routing and Cache | nothing new | `services.AddNaravelSharedThrottle("redis");` (one line next to `AddNaravelRouting`); policies and `throttle:login` stay as they are |
| B | Routing references `Naravel.Cache` and a `ThrottleStore` option selects a named store | `Naravel.Cache` and, through it, Foundation and the memory cache package | `o.ConfigureThrottleStore("redis")` |
| C | Build nothing; keep documenting the limits and the recipes (own `IRouteMiddleware` over `IRateLimiter`, or limit at the proxy or gateway) | nothing | as today |

Why A: Routing stays a package that needs only the ASP.NET Core framework reference (PDR-009), nobody who only wants the in-process throttle pulls Cache, and the direction of dependency (Routing.Cache → Routing, Cache) follows hard rule 3. B is less code but adds Cache to every Routing user. C costs nothing and is already the situation described in the docs; it leaves every team to write the same 15-line middleware, including the 429 and header handling.

## Decision (if approved: option A)

- `IThrottleStore` (public, in Routing): `ValueTask<ThrottleDecision> AttemptAsync(string partitionKey, int permitLimit, TimeSpan window, CancellationToken ct)`. `ThrottleDecision` carries `Allowed`, `Remaining`, `RetryAfter`. The partition key is the same `policy|endpoint|subject` string used today.
- `ThrottleMiddleware` calls the store; the headers and the 429 response are unchanged for both stores. The in-process store keeps `PartitionedRateLimiter` and its behaviour bit for bit; the existing tests must pass without edits.
- `Naravel.Routing.Cache` adds `AddNaravelSharedThrottle(storeName)`, an `IThrottleStore` over `ICacheStore.IncrementAsync(key, 1, window)` as described above: key `throttle:{SHA-256 of the partition key}:{bucket}`, one store call per request, no lock. The key is hashed because Memcached limits key length and characters. It adds no third-party dependency and does not use `IRateLimiter`.
- Semantics: fixed window aligned to the clock (`bucket = floor(UtcNow / window)`), not "the first request starts the window" as in `IRateLimiter`. Instances must keep their clocks within the usual NTP skew; a skew of a few milliseconds moves the boundary by that much and nothing else. The documented up to 2x burst across a boundary stays. No sliding window or token bucket here.
- **Bounded latency.** A throttle must not make a slow backend slow for everyone. Each store call gets its own short timeout (`ThrottleOptions.StoreTimeout`, proposed default 250 ms) linked to the request's cancellation token; a timeout counts as a store failure.
- **Failure policy (owner decision).** When the store throws or times out, the choices are *fail open* (admit without any limit), *fail closed* (503 with `Retry-After`) and *degrade to local* (apply today's in-process limiter for that request). Recommendation: **degrade to local** as the default, with `FailClosed` as an explicit per-policy opt-in for credential endpoints. It keeps a Redis outage from taking the application down and still bounds abuse (at the per-instance level the product has today, never unlimited). A small circuit breaker (after a failure, skip the store for a short period, proposed 5 s, then probe again) stops every request paying the timeout while the backend is down. Each failure is logged once per breaker period and counted in `naravel.routing.throttle.store_failures`. Laravel throws, which surfaces as a 500; that is not proposed.
- Telemetry: Routing adds a `naravel.routing.throttle` counter with the dimensions policy and outcome (`allowed`, `rejected`, `degraded`) and the failure counter above. Bounded dimensions only, never the subject.
- Fake: `CacheFake` (Naravel.Cache.Testing) already stands in for the shared counter, with an injectable clock; the in-process store needs none.

## Open questions for the owner

1. Option A, B or C.
2. Failure policy: degrade to local (recommended), fail open, fail closed, or no default (the caller must choose per policy). And the breaker and timeout defaults.
3. Whether the shared throttle may use clock-aligned windows over `IncrementAsync` (recommended: one call, no lock) instead of `IRateLimiter` (several calls and a lock). The cost of the recommendation is only that window boundaries follow the instances' clocks.
4. Maintenance mode state is process-local for the same reason (`docs/en/routing.md`, limitations). It is not covered here; say if it should get its own PDR.

## Not decided here

Sliding-window and token-bucket algorithms, per-user dynamic limits, a cross-instance maintenance flag, and any change to how the client address is determined (forwarded headers stay native ASP.NET Core).

## Verification required after approval

Tests: the in-process store passes the existing throttle tests unchanged; a store contract run on the in-process store, on the Cache adapter over the memory store and, in the env-gated provider job, over Redis with two application hosts that must share one quota (the existing two-host test pattern, with the expectation inverted); restart keeps the quota on Redis; fail open and fail closed on a store that throws; headers and 429 identical for both stores; cancellation. A benchmark of the in-process path (must not regress) and of the shared path on Redis (the claim is one store call per request), reviewed before merge. A burst test: many concurrent requests of one subject against Redis must not queue on a lock. Breaker and timeout behaviour on a store that hangs. Docs EN/FA, `CHANGELOG.md`, public API files. Build, tests and benchmarks are verified by CI only.

## Alternatives considered

1. **Replace the in-process limiter with the shared one.** Rejected: makes every Routing user pay Redis round trips and a hard dependency for something a single instance does better in memory.
2. **Make `ThrottleLimiterRegistry` itself pluggable through `Manager<TDriver,TOptions>`.** Rejected: Routing is not driver-based (PDR-009); one small interface is enough.
3. **Distributed partitions through a custom `PartitionedRateLimiter` over Redis.** Rejected: the native limiter's leases and replenishment timers assume local state; the Cache counter contract already gives one atomic call with the right expiry semantics and has provider tests.
4. **Wrap `IRateLimiter` as it is.** Rejected for the request path: five to six store calls, and a per-subject lock with a 5 second wait that queues the very burst the throttle must stop. It stays the right tool for an explicit attempts counter in a handler (`cache.md`).
