# Review Report — Naravel.Routing.Cache

## Review metadata

- Package ID: `Naravel.Routing.Cache`.
- Source project: `src/Naravel.Routing.Cache/Naravel.Routing.Cache.csproj`.
- Source-review baseline: `main` at `198b36f9e2753ccfb68354a02480cdc25e7eabc7`; this audit branch contains that commit.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source and contract review; independent second review NOT RUN.
- Status: REVIEWED WITH FINDINGS (1 P2; source inspection only).
- Files inspected: `CacheThrottleStore`, registration, `IThrottleStore`, `ThrottleGuard`, cache adapter tests, README and Routing guide..
- Dependencies: `Naravel.Routing` and `Naravel.Cache`; dependency direction is one-way from adapter to both, Routing does not reference Cache..
- Verification performed: source-to-contract tracing and inspection of the listed tests and documentation.
- Verification NOT RUN: .NET restore/build/test, live provider or service execution unless explicitly stated below, full CI and mutation testing.

## Executive assessment

The adapter uses a single atomic cache increment per request and a clock-aligned fixed window. It hashes partition keys and tests TTL, boundaries, skew, concurrent increments, first store creation and failed creation retry. After the store has once opened, a store rebuilt during cache configuration reload is opened synchronously before the throttle timeout starts.

## Package and dependency map

- Implementation: `CacheThrottleStore : IThrottleStore`.
- Registration: `AddNaravelSharedThrottle(storeName)`.
- Key: hash of partition plus fixed-window bucket.
- One `ICacheStore.IncrementAsync` per request; first store creation is shared through one task.

## Findings


### [P2] RTC-001 — Cache store recreation after reload can block before the throttle timeout

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; runtime reproduction NOT RUN.
- Exact source evidence: `CacheThrottleStore.OpenAsync` uses `Task.Run(OpenOnce)` while `_opened` is false. After `_opened` becomes true, it calls `caches.Store(storeName)` directly. The source comment notes that a store recreated after a cache configuration reload is created inline. `ThrottleGuard.CallStoreAsync` invokes `_store.AttemptAsync(...).AsTask()` before it calls `WaitAsync(_timeout)`.
- Affected API/configuration: `AddNaravelSharedThrottle`, Cache store configuration reload and `ThrottleOptions.StoreTimeout`.
- User impact: A slow synchronous driver factory during cache reload can hold an HTTP request longer than the configured throttle timeout. The breaker cannot act until the blocking call returns a task.
- Trigger/example: The store was opened once; configuration reload retires it; the first request after reload causes CacheManager to build a new Redis store synchronously.
- Why this proves the concern: The timeout starts only after `AttemptAsync` returns. The inline `caches.Store` call happens before that point.
- Existing test gap: Tests prove first creation is off the caller thread, but do not recreate a store after `_opened` is true or block that factory inside HTTP throttle.
- Required safe outcome: Use the same non-blocking single-creation path for cache store recreation as for initial creation, and make the timeout cover store acquisition as well as increment.
- Regression tests required: Open once, trigger configuration reload, block replacement factory, call through HTTP throttle and prove fallback response arrives within `StoreTimeout`; then release factory and prove recovery.
- Owner decision: Not required to record this finding; required before changing runtime behavior.

#### Plain-language explanation

**In everyday words:** First connection creation is moved off the request thread. After a configuration reload, the replacement connection is created directly on the request thread before the timeout starts.

**A realistic example:** The throttle timeout is 250 ms. Redis settings are reloaded and the new connection takes several seconds to open. The next request can wait those seconds before the timeout can act.

**What we checked:** `CacheThrottleStore.OpenAsync` uses `Task.Run(OpenOnce)` while `_opened` is false. After `_opened` becomes true, it calls `caches.Store(storeName)` directly. The source comment notes that a store recreated after a cache configuration reload is created inline. `ThrottleGuard.CallStoreAsync` invokes `_store.AttemptAsync(...).AsTask()` before it calls `WaitAsync(_timeout)`.

**Why that proves the concern:** The timeout starts only after `AttemptAsync` returns. The inline `caches.Store` call happens before that point.

**What is not yet proven:** the review inspected source and existing test code only. The behavior was not reproduced by running a test or a live service in this review.

**What should change:** Use the same non-blocking single-creation path for cache store recreation as for initial creation, and make the timeout cover store acquisition as well as increment.

**How we will prove the fix:** Open once, trigger configuration reload, block replacement factory, call through HTTP throttle and prove fallback response arrives within `StoreTimeout`; then release factory and prove recovery.


## Test assessment

- Existing tests inspected: `CacheThrottleStoreTests`, shared-throttle registration/host tests and Routing `ThrottleFailureTests`..
- Coverage seen in source: window boundaries, retry-after, TTL, hashing, clock skew, concurrent increments, initial async store creation and retry after initial creation failure..
- Gaps: successful initial open followed by settings reload, blocked replacement factory and proof that `StoreTimeout` applies to that reload path.
- Tests actually run for this review: NONE.
- Build/restore: NOT RUN.
- External service/provider integration: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Documentation and packaging assessment

README/guide correctly explain fixed-window behavior, clocks, process-local Memory store and backend failure modes. The timeout promise should also cover reload-time recreation.

## Risk summary and next action

- P2: RTC-001.
- P0 / P1 / P3 / OBS / DOC: none recorded.

Recommended next task: make store initialization non-blocking on both first creation and recreation after reload, and test HTTP timeout behavior. Do not change runtime code in this audit without owner approval.

## Plain-language explanation

This adapter lets multiple application instances share throttle counts using a Cache store. Source and tests were inspected; no build, tests or live Redis/Memcached test ran.
