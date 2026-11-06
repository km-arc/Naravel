# PDR-024 — Shared store for the route `throttle` middleware

**Native-first verdict:** `System.Threading.RateLimiting` is the right limiter for one process and stays the default. It cannot share a quota between processes, and no native .NET component does. Naravel already has the shared pieces in Cache (an atomic counter with a first-write expiry, and the Cache-backed `IRateLimiter`, PDR-007a); what is missing is the connection between them and the route middleware.

Status: **Approved 2026-11-03** (see *Approval*): option A, the clock-aligned counter, the failure policy *degrade to local* and the breaker contract as written below. The implementation is stage R31 (gate: this PDR). Prerequisite satisfied: PR #192 merged; its full CI run #497 passed (AUDIT D-74). <!-- check:allow-count -->

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

## Decision (option A)

- `IThrottleStore` (public, in Routing): `ValueTask<ThrottleDecision> AttemptAsync(string partitionKey, int permitLimit, TimeSpan window, CancellationToken ct)`. `ThrottleDecision` carries `Allowed`, `Remaining`, `RetryAfter`. The partition key is the same `policy|endpoint|subject` string used today.
- `ThrottleMiddleware` calls the store; the headers and the 429 response are unchanged for both stores. The in-process store keeps `PartitionedRateLimiter` and its behaviour bit for bit; the existing tests must pass without edits.
- `Naravel.Routing.Cache` adds `AddNaravelSharedThrottle(storeName)`, an `IThrottleStore` over `ICacheStore.IncrementAsync(key, 1, window)` as described above: key `throttle:{SHA-256 of the partition key}:{bucket}`, one store call per request, no lock. The key is hashed because Memcached limits key length and characters. It adds no third-party dependency and does not use `IRateLimiter`.
- Semantics: fixed window aligned to the clock (`bucket = floor(UtcNow / window)`), not "the first request starts the window" as in `IRateLimiter`. Instances must keep their clocks within the usual NTP skew; a skew of a few milliseconds moves the boundary by that much and nothing else. The documented up to 2x burst across a boundary stays. No sliding window or token bucket here.
- **Bounded latency.** A throttle must not make a slow backend slow for everyone. Each store call gets its own short timeout (`ThrottleOptions.StoreTimeout`, default 250 ms) linked to the request's cancellation token; a timeout counts as a store failure.
- **Failure policy (approved 2026-11-03).** When the store throws or times out, the choices are *fail open* (admit without any limit), *fail closed* (503 with `Retry-After`) and *degrade to local* (apply today's in-process limiter for that request). Decision: **degrade to local** as the default, with `FailClosed` as an explicit per-policy opt-in for credential endpoints. It keeps a Redis outage from taking the application down and still bounds abuse (at the per-instance level the product has today, never unlimited). Laravel throws, which surfaces as a 500; that is not proposed. The contract:
  - `ThrottleFailureMode { DegradeToLocal (default), FailClosed, FailOpen }`, set per policy (`o.ConfigureThrottlePolicy("login", 5, TimeSpan.FromMinutes(1), ThrottleFailureMode.FailClosed)`); `FailOpen` exists as an explicit opt-in only.
  - **What is a failure:** the store throws, or the call exceeds `StoreTimeout`. An `OperationCanceledException` while the request's own token is cancelled (the client went away) is **not** a failure: it propagates and does not touch the breaker.
  - **One breaker per store** (every policy shares the same backend), time taken from `TimeProvider`. *Closed*: calls go to the store. After a failure it is *Open* for `BreakerPeriod` (default 5 s): the store is not called and each request is handled by its policy's failure mode. When the period has passed it is *Half-open*: exactly one request (the probe) calls the store while the others are still handled as in Open. A successful probe closes it; a failed probe opens it for another period.
  - **Degraded requests** use a per-instance in-process limiter with the same policy numbers; its counters are separate, and requests it admitted are not replayed into the store when the backend returns. Responses carry the headers the local limiter computes. `FailClosed` answers 503 with `Retry-After` set to the remaining open time (at least 1 s) and no `X-RateLimit-*` headers.
  - Each breaker opening is logged once (a warning with the exception), and every failure is counted in `naravel.routing.throttle.store_failures`.
- Telemetry: Routing adds a `naravel.routing.throttle` counter with the dimensions policy and outcome (`allowed`, `rejected`, `degraded`) and the failure counter above. Bounded dimensions only, never the subject.
- Fake: `CacheFake` (Naravel.Cache.Testing) already stands in for the shared counter, with an injectable clock; the in-process store needs none.

## Memory provider and lock lifetime (fixed in PR #192; CI run #497 passed) <!-- check:allow-count -->

- **The shared throttle takes no lock.** The adapter makes one `IncrementAsync` call per request. `MemoryLock` and `IRateLimiter` are not on its path, so nothing here depends on how Memory locks behave.
- **The Memory lock-lifetime defect is fixed.** The Memory provider had retained lock state for every key or lock name it had seen: `MemoryCacheStore` held a lock object per key passed to `IncrementAsync`, and the static `MemoryLock` table retained a state per lock name after release. A key per client address and window (this design) or a lock per subject (`IRateLimiter` today) could therefore grow memory without bound, driven by the client. The finding was recorded as AUDIT D-74 and closed after PR #192 merged; the 256 fixed gates, release-time state retirement, expiry sweep and regression tests are in place. Full CI run #497 passed. This also fixes the Cache-backed rate limiter on the Memory store, independent of this PDR. <!-- check:allow-count -->
- **What stays in Memory for this design.** One counter entry per subject and window, with the window as its time-to-live. Expired entries are removed by `IMemoryCache` and by the store's eviction callback, so memory follows the number of distinct subjects seen within about one window (plus the cache's scan interval), not the history of the process.
- **Memory is process-local.** `AddNaravelSharedThrottle("memory")` counts per process and shares nothing, like `MemoryLock` (`IsShared` is false). It is allowed for development and tests and the docs say so. There is no runtime check: `ICacheStore` does not say whether a store is shared, and adding such a flag to the public Cache contract is a separate decision that is not made here.

## Approval

The owner confirmed option A and the clock-aligned counter on 2026-11-01. On 2026-11-03 the owner answered the two questions that were still open: (1) the failure policy is **degrade to local** by default, with `FailClosed` as an explicit per-policy opt-in and `FailOpen` as an explicit opt-in only; (2) the breaker contract and its defaults are approved exactly as written above (one breaker per store, `StoreTimeout` 250 ms, `BreakerPeriod` 5 s, a single probe when half-open, a client abort is not a failure). Both defaults are options on `ThrottleOptions`, so tuning them later is not an API change. Registering the implementation as stage R31 was left to the assistant's choice ("select best"). The maintenance-mode state (question 4 of the draft) was not answered, so it stays outside this PDR (see *Not decided here*).

## Not decided here

Sliding-window and token-bucket algorithms, per-user dynamic limits, a cross-instance maintenance flag, and any change to how the client address is determined (forwarded headers stay native ASP.NET Core).

## Verification required

Routing tests use a test double of `IThrottleStore` (counts calls, can throw, hang until cancelled or answer) and a manual `TimeProvider`, so no test waits for a real breaker period.

**Failure handling (the contract above):**

| Test | Proves |
|---|---|
| `Degrade_to_local_limits_with_the_in_process_limiter_when_the_store_throws` | policy 2 per window, store throws: requests 1-2 get 200, request 3 gets 429 from the local limiter; never unlimited |
| `Degrade_to_local_when_the_store_exceeds_the_timeout` | a store that hangs until cancelled: the request completes after about `StoreTimeout`, not after the store; then the local limit applies |
| `Degraded_responses_keep_the_rate_limit_headers_and_count_the_degraded_outcome` | `X-RateLimit-*` and `Retry-After` come from the local limiter; the counter records `degraded` and the failure counter 1 |
| `Breaker_opens_after_a_failure_and_skips_the_store_for_the_breaker_period` | after one failure, N further requests inside the period make zero store calls |
| `Breaker_probes_once_after_the_period_and_closes_when_the_probe_succeeds` | clock advanced past the period: one store call; then every request uses the store again |
| `Breaker_reopens_for_another_period_when_the_probe_fails` | a failed probe: no store calls for another full period |
| `Only_one_request_probes_a_half_open_breaker` | many concurrent requests at the end of the period: exactly one store call |
| `Fail_closed_policy_answers_503_with_retry_after_and_never_uses_the_local_limiter` | 503, `Retry-After` of at least 1 s, no `X-RateLimit-*`, local limiter untouched |
| `Fail_open_policy_admits_without_a_limit_when_the_store_fails` | only for a policy that opted in |
| `Failure_mode_is_per_policy_on_one_store` | `login` fail closed and `api` degrade to local behave differently on the same failing store |
| `A_client_abort_is_not_a_store_failure` | the request token cancelled: no breaker change, no `degraded` outcome, no log |
| `The_breaker_opening_is_logged_once_per_period` | many failing requests in one period: one warning |
| `Recovery_does_not_replay_locally_admitted_requests_into_the_store` | after the probe closes the breaker the store counts from its own state |

**Store behaviour:** the in-process store passes the existing throttle tests unchanged; one store contract runs on the in-process store, on the Cache adapter over the Memory store and, in the env-gated provider job, over Redis. Two application hosts on Redis must share one quota (the two-host test of this repository with the expectation inverted), and a restarted host keeps the quota. Headers and 429 are identical for both stores. A burst test: many concurrent requests of one subject against Redis must not queue on a lock. A key test: the key is hashed and stays within the Memcached key rules for long and unusual partition keys.

**Memory provider:** the adapter over the Memory store takes no `MemoryLock`; many distinct subjects over several short windows leave the entry count falling after the windows expire (the test uses a real window of a few hundred milliseconds because `MemoryCacheStore` reads the system clock); the tests that PR #192 adds for AUDIT D-74 passed in CI run #497. <!-- check:allow-count -->

A benchmark of the in-process path (must not regress) and of the shared path on Redis (the claim is one store call per request), reviewed before merge. English docs, `CHANGELOG.md`, and public API files. Build, tests and benchmarks are verified by CI only.

## Alternatives considered

1. **Replace the in-process limiter with the shared one.** Rejected: makes every Routing user pay Redis round trips and a hard dependency for something a single instance does better in memory.
2. **Make `ThrottleLimiterRegistry` itself pluggable through `Manager<TDriver,TOptions>`.** Rejected: Routing is not driver-based (PDR-009); one small interface is enough.
3. **Distributed partitions through a custom `PartitionedRateLimiter` over Redis.** Rejected: the native limiter's leases and replenishment timers assume local state; the Cache counter contract already gives one atomic call with the right expiry semantics and has provider tests.
4. **Wrap `IRateLimiter` as it is.** Rejected for the request path: five to six store calls, and a per-subject lock with a 5 second wait that queues the very burst the throttle must stop. It stays the right tool for an explicit attempts counter in a handler (`cache.md`).
