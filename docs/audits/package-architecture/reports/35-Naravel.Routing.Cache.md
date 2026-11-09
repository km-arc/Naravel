# Review Report — Naravel.Routing.Cache

## Review metadata

- Package ID: Naravel.Routing.Cache.
- Source project: src/Naravel.Routing.Cache/Naravel.Routing.Cache.csproj.
- Baseline: main at d3f4342db8aa7d0f8085d1205f76f6ae5bfa8ba3.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source, distributed-contract and test review; independent fresh-session review NOT RUN.
- Status: REVIEWED — NO SOURCE-LEVEL FINDINGS (runtime provider verification pending).
- Files inspected: CacheThrottleStore, service registration, Cache and Routing interfaces, CacheThrottleStoreTests, SharedThrottleHostTests, RedisSharedThrottleTests, support/fakes, README and Routing guide.
- Dependencies: Naravel.Routing, Naravel.Cache, Naravel.Foundation and the selected cache provider through ICacheStore.
- Verification performed: fixed-window key construction, counter atomicity, expiry, clock alignment, cache-store opening, cancellation while opening, registration order and multi-host behavior inspected.
- Verification NOT RUN: restore, build, adapter tests, live Redis provider tests, multi-process clock-skew run, full CI, mutation testing and independent review.

## Executive assessment

The adapter implements the shared throttle using one atomic cache increment per request. The key includes a SHA-256 digest of the partition and a fixed-window bucket number. It does not read the current counter before incrementing and does not add a second lock around the attempt.

The fixed-window boundary can allow up to twice the configured limit in a short interval across adjacent windows. That is explicitly documented as the selected fixed-window behavior. The adapter also documents that hosts must keep their clocks within normal NTP skew. No source-level correctness finding was established in this pass.

## Package and dependency map

- Implementation: CacheThrottleStore implements IThrottleStore.
- Registration: AddNaravelSharedThrottle selects a named Naravel.Cache store and registers the adapter as the Routing store.
- Key: a fixed-format SHA-256 hash of the policy/endpoint/subject partition plus the clock-aligned window number.
- Store operation: one IncrementAsync call with TTL; no read-modify-write lock is used.
- Dependencies: Routing, Cache and Foundation abstractions; the concrete cache provider remains configurable.

## Findings

No source-level finding was established in the inspected adapter.

## Existing guarantees and coverage

- The window is aligned to UTC time boundaries.
- The cache key uses a fixed-length digest rather than raw endpoint/user data.
- The attempt is one atomic increment, so concurrent requests use the same backend counter.
- The counter expiry is at least as long as the bucket remains reachable.
- First store creation is shared by concurrent callers; an individual cancelled caller stops waiting without cancelling the shared creation for other callers.
- Tests cover aligned boundaries, RetryAfter, fixed-window double-burst behavior, clock skew, concurrent attempts, failed creation and multi-host quota sharing.
- Redis-specific tests cover the shared store path; those tests require an external provider.

## Test assessment

- Tests inspected: CacheThrottleStoreTests, SharedThrottleHostTests and RedisSharedThrottleTests.
- Tests actually run in this review: NONE.
- Live Redis, build/CI, mutation testing and independent review: NOT RUN.

## Documentation and packaging assessment

- The package README describes the required shared Cache store and registration.
- The Routing guide explains fixed-window behavior, host clock skew and failure modes.
- The adapter does not claim sliding-window semantics or that a process-local Memory store shares quotas across hosts.

## Risk summary and next action

- P0/P1/P2/P3/OBS/DOC: none established by source inspection.
- Recommended next task: keep the fixed-window and clock-skew limitations explicit, and run the Redis multi-host tests in CI before release.

## Plain-language explanation

This adapter lets several application servers use the same cache counter for a route's request limit. Each request increments one shared counter; the adapter does not read a value and then write it back under a separate lock. The counter is tied to a fixed UTC minute/window. Near a boundary, a caller can use the limit just before and just after the boundary; that is the documented trade-off of a fixed window. Tests were inspected but not run, and live Redis behavior was not verified here.
