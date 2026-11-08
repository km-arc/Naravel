# Review Report — Naravel.Cache.Memcached

## Review metadata

- Package ID: `Naravel.Cache.Memcached` (explicitly declared).
- Source project: `src/Naravel.Cache.Memcached/Naravel.Cache.Memcached.csproj`.
- Source-review baseline commit: `a69e0becca197ee8dc14d2a576b45dd763263833` (`main`); architecture veto revalidation: `97764a4535bf6ef371bd4d1a764a055a19da511f`.
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; runtime verification pending).
- Files inspected: all files under `src/Naravel.Cache.Memcached/`; package project file and shipped/unshipped API baselines; central package versions; `MemcachedProviderTests`, shared Cache store contract, Cache store/lock tests, Cache registration tests and real-server serializer tests; `ICacheStore`, Memory and Redis store implementations; Cache guide, NuGet README and PDR-007.
- Dependency verification: the exact pinned `EnyimMemcachedCore 3.5.2` tag was inspected, including its public client API, options defaults, generic get result, store result path and binary mutation result path.
- Verification performed: DI/server-pool configuration flow; typed serialization and counter conversion; read/write result handling; cancellation flow; lock acquire/release; cross-provider counter contract; test-double and service-gated coverage.
- Verification NOT RUN: `dotnet restore`, `dotnet build`, `dotnet test`, live Memcached integration, CI, mutation testing, overflow/null/failure/cancellation reproductions, and independent review.

## Executive assessment

- Overall risk: **High (P1)** until AV-001 and AV-002 are remediated; AV-003 through AV-008 are mandatory P2 architecture work.
- Architectural fit: The adapter is thin, but four previously accepted PDR-007 constraints are vetoed as final architecture: cluster-wide flush, non-atomic lock release, a single global server pool, and blocking counter I/O scheduled on the ThreadPool. They describe current implementation behaviour; they are not accepted target-state guarantees.
- Strengths: per-store key prefixes are applied consistently; configured Memcached stores share one normalized server pool and mismatched pools are rejected; TTL counter creation uses atomic add followed by atomic increment with the same TTL in the expiry race; the provider keeps numeric values as invariant decimal text for server-side arithmetic; the documented flush and lock limitations match the code.
- Summary: Ten issues are now tracked. In addition to the five provider correctness gaps, the package can flush the entire shared cluster through a store-level method, release a new owner's lock in a read/delete race, forces all named stores onto one server pool, schedules blocking counter network calls on ThreadPool workers, and converts positive sub-second TTLs to Memcached's no-expiry value. The first two are P1 isolation/concurrency vetoes; the remaining architecture vetoes are mandatory before final production acceptance.

## Package and dependency map

- Public package contract: `MemcachedCacheStore`, `MemcachedLock` and `MemcachedCacheServiceCollectionExtensions.AddNaravelMemcachedCache`.
- Package metadata: explicit `PackageId=Naravel.Cache.Memcached`; `PackageReadmeFile=README.md`; the packed README is `docs/nuget/Naravel.Cache.Memcached.md`; description and package tags are present. The target framework is inherited from shared build properties.
- Project references: `Naravel.Cache`.
- Direct package references: `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `EnyimMemcachedCore`, `MessagePack` and `Newtonsoft.Json`.
- Central versions: `EnyimMemcachedCore 3.5.2`, `MessagePack 3.1.12`, `Newtonsoft.Json 13.0.4`, and Microsoft.Extensions abstractions `10.0.14`. The repository pins MessagePack directly for the Enyim serialization dependency; the package comment requires the real-server serializer round-trip tests when changing that pin.
- Registration: `AddNaravelMemcachedCache` identifies configured stores using the Memcached driver, normalizes and compares server lists, rejects an empty or different server pool, registers one shared Enyim client and registers named cache-store/lock adapters.
- Resource/configuration model: all configured stores share one Enyim client and its server pool. A different normalized server list is rejected; topology changes require a host restart. Store-level values can continue to reload through Foundation.
- Public API baseline: shipped entries declare the store, lock and registration extension; `PublicAPI.Unshipped.txt` contains only the nullable header.

## Findings

### [P2] MCD-001 — Generic value-type cache misses can be reported as hits

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; live-provider reproduction NOT RUN.
- Evidence: `src/Naravel.Cache.Memcached/MemcachedCacheStore.cs:62-63` returns based on `result.HasValue` without checking `result.Success`. In the pinned [Enyim v3.5.2 generic get result](https://github.com/cnblogs/EnyimMemcachedCore/blob/v3.5.2/src/Enyim.Caching/Memcached/Results/GetOperationResult%601.cs), `HasValue` is implemented as `Value != null`, while `Value` is the generic `T`. For a non-nullable value type, a failed/missing result leaves `Value` at `default(T)`, which is not null. Enyim's operation result separately exposes `Success`.
- Affected contract: `ICacheStore.TryGetAsync<T>` for types that take the generic Enyim read path rather than the provider's integral/text conversion paths.
- User impact: a genuinely absent key can be returned as `Found=true` with `false`, `0`, `DateTime.MinValue`, or another default value. Callers such as cache-aside/remember flows may treat the default as a cached value and skip recomputation.
- Trigger / failure scenario: call `TryGetAsync<bool>("missing")`, `TryGetAsync<double>("missing")`, `TryGetAsync<DateTime>("missing")`, or use an enum/custom struct key that has never been stored. These types fall through to `_client.GetAsync<T>`; the Enyim generic result's `HasValue` check is true for its default value.
- Scope note: the provider avoids this exact generic path for the integral types listed by `TryGetIntegralType` and for Guid/DateTimeOffset/decimal handled by `TryGetTextType`. The finding applies to other non-nullable value types and any additional value type that uses the generic path.
- Why existing tests do not catch it: the real-server serializer tests read each value only after writing it. Their missing-key assertion is for `string`, a reference type. The `DispatchProxy` test result explicitly sets `HasValue` and therefore does not reproduce Enyim's `Value != null` implementation for value types.
- Recommended correction: do not infer presence from `HasValue` alone. Inspect Enyim's success/status/exception metadata and distinguish a true not-found response from an operation failure; map only a genuine miss to `Found=false`, and surface backend failures instead of returning a default value.
- Regression tests required: missing-key tests for bool, float/double, DateTime, enum and a custom struct; existing-key tests for the same set; a failed-read test that proves backend failure is not silently converted into a miss.
- Public API / PDR / release impact: implementation correction; no public API change expected.

### [P2] MCD-002 — Failed writes and lock acquisition can be reported as success or contention

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; failure-result integration test NOT RUN.
- Evidence: `src/Naravel.Cache.Memcached/MemcachedCacheStore.cs:67-75` awaits `_client.SetAsync(...)` but discards its returned `bool`. The pinned [Enyim v3.5.2 client](https://github.com/cnblogs/EnyimMemcachedCore/blob/v3.5.2/src/Enyim.Caching/MemcachedClient.cs) implements `StoreAsync` by returning the operation result's `Success`; its `PerformStoreAsync` returns a failed result for a null value and for serialization failure rather than turning those result states into a successful write. `src/Naravel.Cache.Memcached/MemcachedLock.cs:23-30` similarly interprets every `AddAsync=false` as ordinary lock contention and returns `null`.
- Affected contract: `ICacheStore.SetAsync` and `ICacheLock.AcquireAsync`.
- User impact: the Cache abstraction's `SetAsync` returns a completed `Task` even when Enyim reports that the value was not stored. The caller cannot distinguish a successful write from an unsuccessful operation. For locks, a failed `AddAsync` operation is exposed as “lock not acquired”, which is indistinguishable from another owner holding the lock. This can turn backend/operation errors into a false lock-contention path.
- Trigger / failure scenario: pass a null reference value to `SetAsync`. Enyim's `PerformStoreAsync` explicitly returns a failed result for null; `StoreAsync` therefore returns `false`; the adapter discards it and completes normally. A failed Add operation on the lock path is similarly returned as `null`, just like a genuinely occupied lock.
- Why existing tests do not catch it: `MemcachedProviderTests.MemcachedClientProxy` hardcodes `SetAsync` to `Task.FromResult(true)`; its `AddAsync` fake has one boolean used only to model the normal add/duplicate race. There is no test for a failed Enyim store result or the distinction between lock contention and backend failure.
- Recommended correction: never discard the Enyim store result. Convert unsuccessful writes into a visible failure consistent with the Cache abstraction and its documented backend-error behavior. For lock acquisition, preserve a distinction between “key already exists” and an actual failed operation; if Enyim's boolean API cannot express that distinction, use a failure-aware operation path or revise the adapter/dependency boundary rather than treating every `false` as contention.
- Regression tests required: a Set operation returning `false` must not complete as a successful write; a serializer failure must be visible; an actual existing lock must still return `null`; an operation-level/backend failure must not be reported as normal contention.
- Public API / PDR / release impact: no public API change is necessarily required, but the failure behavior should be checked against `docs/en/cache.md:347`, which says backend errors are surfaced unchanged.

### [P2] MCD-003 — Unsigned Memcached counter semantics corrupt the signed `long` contract

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE AND PROTOCOL; boundary reproduction NOT RUN.
- Evidence: `src/Naravel.Cache/Abstractions/ICacheStore.cs:22-29` defines counter amounts and results as signed `long`. `src/Naravel.Cache.Memcached/MemcachedCacheStore.cs:99,112,122` casts Enyim's `ulong` mutation results to `long` without a range check. The pinned [Enyim v3.5.2 interface](https://github.com/cnblogs/EnyimMemcachedCore/blob/v3.5.2/src/Enyim.Caching/IMemcachedClient.cs) exposes increment/decrement values and results as `ulong`; its [mutation implementation](https://github.com/cnblogs/EnyimMemcachedCore/blob/v3.5.2/src/Enyim.Caching/MemcachedClient.cs) returns the mutation result's `Value` even when the operation result is failed. The [Memcached protocol](https://github.com/memcached/memcached/blob/master/doc/protocol.txt) defines arithmetic values as unsigned 64-bit integers, with increment overflow wrapping and decrement underflow clamped to zero.
- Affected contract: `ICacheStore.IncrementAsync` and `DecrementAsync`, including reading values written through `SetAsync<long>`.
- User impact: if a stored counter is `long.MaxValue`, incrementing by one produces the unsigned value `9223372036854775808`; the unchecked cast returns `long.MinValue` while the server retains the larger positive number. A later `TryGetAsync<long>` attempts to convert that decimal text to `Int64` and overflows. Negative signed values can also be stored as decimal text by `SetAsync<long>`, but Memcached arithmetic expects an unsigned decimal value; Enyim's failed mutation result leaves its numeric result at the default zero, which the adapter returns as if it were the new count. Decrementing below zero is clamped by Memcached, unlike the Memory provider's signed subtraction.
- Trigger / failure scenario: set a counter to `long.MaxValue` and increment by one; separately, set a counter to `-1` and increment by one. The first returns a wrapped negative `long` while leaving an out-of-range server value; the second can return zero even though the stored negative text was not successfully mutated.
- Why existing tests do not catch it: the shared contract tests only increment a positive `long` value from 41 to 42 and perform positive first-create increments. They do not test negative values, `long.MaxValue`, overflow, decrement underflow, or failed mutation status. The provider proxy returns the default value passed to `Increment`, so it does not model unsigned server arithmetic or Enyim's failed-result behavior.
- Recommended correction: define and enforce an explicit signed-counter contract for Memcached. Do not cast arbitrary `ulong` results to `long`; prevent or surface values outside the signed range, propagate mutation failures rather than returning the default zero, and obtain an owner decision on whether Memcached's zero-floor decrement behavior is an accepted provider-specific limitation or must be normalized. Do not silently change the cross-provider contract.
- Regression tests required: `long.MaxValue + 1` must fail explicitly without returning a wrapped value; negative stored counters must produce an explicit, documented outcome; failed server mutation must not return zero as success; decrement below zero must have a tested and owner-approved contract.
- Public API / PDR / release impact: likely implementation and documentation/PDR clarification; no public API change is assumed. The signed-range and underflow behavior needs an owner decision before remediation.

### [P2] MCD-004 — Cancellation after asynchronous I/O starts does not cancel the caller's wait

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE AND TEST COVERAGE REVIEW.
- Evidence: `src/Naravel.Cache.Memcached/MemcachedCacheStore.cs:35-90,126-130` checks the token before issuing the operation, then awaits Enyim tasks without a cancellable wait. `src/Naravel.Cache.Memcached/MemcachedLock.cs:23-43` has the same pattern. The pinned [Enyim v3.5.2 client interface](https://github.com/cnblogs/EnyimMemcachedCore/blob/v3.5.2/src/Enyim.Caching/IMemcachedClient.cs) exposes the relevant asynchronous methods without a `CancellationToken` parameter.
- Affected contract: asynchronous cache reads/writes/removes/exists/flush and Memcached lock acquire/release when cancellation occurs after the Enyim operation has started.
- User impact: after request cancellation, the provider's returned task can remain pending until Enyim completes or its network timeout expires. This retains the caller's wait and resources beyond the point at which the request has abandoned the operation.
- Scope distinction: the synchronous counter commands scheduled with `Task.Run` are excluded from this finding. PDR-007 line 61 and the Cache guide explicitly document that cancellation can prevent queued counter work but cannot interrupt a counter command once it has started. This finding concerns Enyim's asynchronous I/O calls.
- Why existing tests do not catch it: provider proxy tasks complete immediately. The shared cancellation test inspected in `CacheStoreTests` covers pre-cancelled behavior, not cancellation after a Memcached I/O task has been issued. No pending-`TaskCompletionSource` test was found for the provider or its lock.
- Recommended correction: where consistent with the approved Cache contract, make the caller's wait cancellable (for example, by applying `WaitAsync(cancellationToken)` to the Enyim task). Document that this does not cancel or roll back a command already sent to Memcached; its remote side effect may still complete after the caller receives `OperationCanceledException`.
- Regression tests required: hold Enyim Get/Set/Remove/Add/Flush tasks pending in a proxy, cancel after dispatch, and assert that the provider task promptly throws `OperationCanceledException`; separately complete the underlying task afterwards to verify no false claim that the remote command was rolled back.
- Public API / PDR / release impact: no public API change required; clarify caller-wait versus remote-command cancellation semantics in the provider documentation.

### [P3] MCD-005 — Null nullable-integral values are stored as empty text and cannot be read

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; reproduction NOT RUN.
- Evidence: `src/Naravel.Cache.Memcached/MemcachedCacheStore.cs:70-74` selects the integral-text branch based on `typeof(T)` even when `value` is null; `Convert.ToString(null, CultureInfo.InvariantCulture)` yields an empty string. The read path at lines 39-45 then passes that empty text to `Convert.ChangeType` for the underlying integral type.
- Affected contract: `SetAsync<int?>`, `SetAsync<long?>` and the other nullable integral types recognized by `TryGetIntegralType`, when the supplied value is null.
- User impact: the null is persisted as an empty string rather than represented as null. Reading the key as the same nullable integral type throws `FormatException` instead of returning `Found=true, Value=null` or an explicit unsupported-value error.
- Trigger / failure scenario: call `SetAsync<int?>("optional", null, ttl)`, then `TryGetAsync<int?>("optional")`. The read sees a hit containing empty text and attempts to parse it as `Int32`.
- Why existing tests do not catch it: the real-server serializer round-trip suite covers non-null integral values and does not test nullable integral nulls. The shared contract also does not include null values.
- Recommended correction: only encode integral values as decimal text when the value is non-null. Define a representation for null that the Memcached transcoder can round-trip, or reject it explicitly and consistently rather than storing a value that the read path cannot parse.
- Regression tests required: nullable int/long null round-trip; non-null nullable integral round-trip; verify the empty string remains a distinct valid string value and is not confused with a null numeric value.
- Public API / PDR / release impact: implementation correction and clarification of null-value support; no public API change assumed.

### [P1] MCD-006 — Store-level FlushAsync clears the entire shared Memcached cluster

- Remediation state: OPEN.
- Architecture disposition: VETO AV-001 — ordinary store/scope invalidation must never become a cluster-wide destructive operation.
- Evidence status: CONFIRMED BY SOURCE AND APPROVED-PDR TRACE.
- Evidence: src/Naravel.Cache.Memcached/MemcachedCacheStore.cs:126-130 directly calls _client.FlushAllAsync(). The pinned Enyim v3.5.2 implementation loops over every working node in its client pool when it executes FlushAllAsync (MemcachedClient.cs, FlushAllAsync); this is a pool-wide flush, not a prefix delete. All configured Naravel Memcached stores currently receive the same IMemcachedClient from one registration.
- Affected contract: ICacheStore.FlushAsync, including named stores and any ScopedCacheStore view that delegates to it.
- User impact: flushing one named cache store deletes entries belonging to every other store sharing the pool, including entries whose configured prefixes differ. Through ScopedCacheStore.FlushAsync, a tenant/user view can trigger the same cluster-wide deletion. This is a data-isolation violation, not merely a performance limitation.
- Trigger / failure scenario: configure two Memcached stores with different prefixes on the same server pool, write unrelated values through both, and call FlushAsync on one. The Enyim pool flush removes both. Calling it from one tenant scope can erase the other tenant's cache too.
- Why existing tests do not catch it: current provider tests only verify that the fake receives FlushAllAsync; there is no multi-store or multi-scope real-server assertion. The PDR and docs explicitly describe the destructive behaviour, which is why it was previously treated as an accepted limitation.
- Required correction: remove FlushAllAsync from the ordinary ICacheStore.FlushAsync path. Implement a namespace/scope-specific invalidation mechanism whose control state cannot be silently evicted/reset while old values remain reachable. If that guarantee cannot be provided for Memcached, fail explicitly with a capability error rather than deleting unrelated data. Cluster-wide flush, if retained at all, must be a separately named administrative operation with explicit cluster-level intent.
- Regression tests required: two named stores sharing one client; two scopes in one store; base-store values; flush one target and prove every out-of-scope value survives. Add an eviction/restart test for any generation/epoch mechanism to prove old values cannot reappear.
- Public API / PDR / release impact: PDR-007's accepted cluster-wide flush decision is vetoed. A public capability/exception or administrative API may be required; no silent semantic widening is acceptable.

### [P1] MCD-007 — Memcached lock release can delete a different owner's lock

- Remediation state: OPEN.
- Architecture disposition: VETO AV-002 — a distributed lock must not release a lock that belongs to another owner.
- Evidence status: CONFIRMED BY SOURCE-LEVEL INTERLEAVING.
- Evidence: src/Naravel.Cache.Memcached/MemcachedLock.cs:39-43 performs GetValueAsync<string>, compares the token locally, then calls unconditional RemoveAsync. The token comparison and deletion are not one atomic server operation. PDR-007 line 54 and the Cache guide document this release race.
- Affected contract: ICacheLock.ReleaseAsync, and every critical section or cache-backed limiter that trusts this provider for mutual exclusion.
- User impact: an owner whose lease has expired can delete a later owner's lock. A third caller can then acquire a new lock while the later owner is still in its critical section, so two callers can execute concurrently despite using the same lock name.
- Trigger / failure scenario: owner A calls Release and reads token A; A's TTL expires; owner B acquires the same key with token B; A resumes and executes unconditional Remove; owner C can now acquire while B is still running.
- Why existing tests do not catch it: Memcached_lock_checks_token_before_removing_key tests only a mismatched token observed at the read; it does not interleave expiry/reacquisition between Get and Remove.
- Required correction: use an atomic token-conditioned release/fencing mechanism supported by the actual Memcached protocol/client. If the pinned client/protocol cannot provide that guarantee, do not register/expose MemcachedLock as a correctness-grade distributed ICacheLock; route coordination-critical work to a provider with atomic ownership semantics.
- Regression tests required: deterministic barrier-based interleaving of A-read, A-expiry, B-acquire, A-remove; prove B remains held and C cannot acquire. Also test stale owner release after expiry and reacquisition against a real Memcached service.
- Public API / PDR / release impact: PDR-007's explicit acceptance of non-atomic release is vetoed. The package must not claim reliable distributed locking while this race remains.

### [P2] MCD-008 — All named Memcached stores are forced onto one server pool

- Remediation state: OPEN.
- Architecture disposition: VETO AV-005 — named stores must be independently placeable/scalable when their isolation or load profile requires it.
- Evidence status: CONFIRMED BY SOURCE.
- Evidence: src/Naravel.Cache.Memcached/MemcachedCacheServiceCollectionExtensions.cs:26-50 compares every configured Memcached store's normalized server list and throws if any differs; lines 52-59 register one Enyim client and inject it into every store/lock factory.
- Affected contract: Cache:Stores:<name>:Servers for multiple named Memcached stores.
- User impact: independent workloads cannot be placed on separate Memcached clusters for capacity, noisy-neighbour isolation, failure-domain separation, regional placement, or staged migration. One pool outage or topology change affects every configured Memcached store.
- Trigger / failure scenario: configure a sessions store on cluster A and a catalog store on cluster B. Registration fails, even though CacheManager exposes named stores.
- Why existing tests do not catch it: the integration tests intentionally assert that different server pools are rejected, matching the earlier PDR decision.
- Required correction: create/reuse one Enyim client per canonical normalized server-pool configuration. Stores with identical pool configurations should share a client; stores with different configurations must be allowed to use independent clients. Define disposal/reload ownership so each client is disposed exactly once after all dependent stores are retired.
- Regression tests required: two different pools register and resolve independently; two stores with the same normalized pool share one client; reload of one pool retires only that pool; disposal occurs exactly once; one pool outage does not prevent the other store from operating.
- Public API / PDR / release impact: PDR-007's single-global-pool constraint is vetoed. Configuration shape can remain the same; implementation and lifetime ownership need redesign.

### [P2] MCD-009 — Synchronous Memcached counter I/O is queued onto the shared ThreadPool

- Remediation state: OPEN.
- Architecture disposition: VETO AV-004 — asynchronous cache APIs must not consume a blocked worker thread for network I/O.
- Evidence status: CONFIRMED BY SOURCE; saturation impact NOT LOAD-TESTED.
- Evidence: src/Naravel.Cache.Memcached/MemcachedCacheStore.cs:93-123 wraps synchronous _client.Increment and _client.Decrement calls in Task.Run. The pinned Enyim v3.5.2 IMemcachedClient exposes these arithmetic methods synchronously; its public interface has no asynchronous increment/decrement overload.
- Affected contract: both ICacheStore.IncrementAsync methods and DecrementAsync, particularly hot counters such as shared rate-limit buckets.
- User impact: each in-flight counter operation occupies a ThreadPool worker while the socket operation waits. Under concurrent high-volume rate limiting or slow Memcached nodes, blocked workers can accumulate and delay unrelated continuations, timers, request work and other async operations. Task.Run moves the blocking call; it does not make the network operation asynchronous or provide backpressure.
- Why existing tests do not catch it: tests prove positive counter values and concurrency correctness but do not measure blocked worker count, queue growth, or ThreadPool starvation under slow-server conditions.
- Required correction: expose/use a genuinely asynchronous Memcached arithmetic operation (through an upstream/client upgrade, a maintained adapter/fork, or a protocol-level async implementation). A bounded queue can protect resource usage during migration, but an unbounded per-call Task.Run is not an acceptable final design.
- Regression tests required: slow-server concurrency test with bounded worker usage and bounded queueing; cancellation while queued; prove normal application tasks remain schedulable during counter load; verify counter atomicity and first-write TTL after the async implementation.
- Public API / PDR / release impact: PDR-007's ThreadPool workaround is vetoed as a final architecture. The provider's async public surface should remain truly non-blocking.

### [P2] MCD-010 — Positive sub-second TTLs become no-expiry in Enyim

- Remediation state: OPEN.
- Architecture disposition: VETO AV-008 — a positive TTL must never be translated into permanent storage.
- Evidence status: CONFIRMED BY PINNED DEPENDENCY SOURCE; live-server reproduction NOT RUN.
- Evidence: MemcachedCacheStore.SetAsync passes the caller's TimeSpan directly to Enyim (src/Naravel.Cache.Memcached/MemcachedCacheStore.cs:67-75); the TTL counter path passes it to AddAsync and the synchronous increment overload (lines 103-112). In pinned EnyimMemcachedCore 3.5.2, MemcachedClient.GetExpiration casts validFor.TotalSeconds to uint for values at or below 30 days (MemcachedClient.cs:1474-1484); any positive duration below one second truncates to 0. Memcached interprets expiration 0 as no expiry.
- Affected contract: SetAsync(key, value, ttl) and IncrementAsync(key, by, ttl) for 0 < ttl < 1s.
- User impact: a caller that asks for a 100 ms or 900 ms cache lifetime can get a permanent entry instead. For TTL counters this can leave a rate-limit bucket indefinitely, violating the fixed-window contract and causing persistent false rejections.
- Trigger / failure scenario: call SetAsync("short", value, TimeSpan.FromMilliseconds(500)); Enyim converts the duration to zero expiry. The same conversion applies to TTL-based counter creation.
- Why existing tests do not catch it: provider TTL tests use whole-second durations (30 seconds); the shared contract uses a 3-second TTL and sleeps. No positive fractional-second boundary is tested.
- Required correction: normalize every positive TTL to a Memcached-representable expiry that is not zero (for example, round up to at least one second), or reject unsupported sub-second TTL values explicitly. Never silently map a positive TTL to no-expiry.
- Regression tests required: 1 ms, 500 ms, 999 ms, exactly 1 s, and values across the 30-day relative/absolute boundary for both Set and TTL increment. Use a real Memcached server to prove the item expires rather than remaining indefinitely.
- Public API / PDR / release impact: implementation correction; amend docs to state the provider's representable precision. No public API change assumed.

## Test assessment

- Existing tests inspected: `MemcachedProviderTests`; shared `CacheStoreContractTests`; `MemcachedSerializerRoundTripTests`; `CacheStoreTests`; `CacheLockTests`; `CacheManagerIntegrationTests`.
- Positive-path provider tests: key prefix and typed value handling; null TTL maps to Memcached no-expiry; TTL counter first creation; TTL retained if the key expires between add and increment; wrong lock token is not removed.
- Shared live-provider contract: checks a string round trip, existence/removal, positive `long` increment, concurrent positive increments and first-write-only TTL. It does not exercise decrement or signed/overflow boundaries.
- Real-server serialization tests: cover int, long, double, bool, Guid, DateTime, DateTimeOffset, decimal, byte arrays, string arrays and a POCO with collections. These cover successful retrieval after Set, not value-type misses or nullable-integral nulls.
- Service gating: real Memcached tests require `NARAVEL_TEST_MEMCACHED`; the Cache guide states that CI runs the provider tests against a Memcached service container. The CI/test run was not independently queried or executed as part of this source review.
- Missing regression coverage: the gaps in MCD-001 through MCD-005 plus namespace-safe store/scope flush, the stale-owner lock interleaving, multiple independent server pools, bounded ThreadPool use under slow counter I/O, and sub-second TTL expiry.
- Tests actually run: none.
- Tests NOT RUN: Memcached provider tests, live `NARAVEL_TEST_MEMCACHED` suite, Cache test project, full solution build/test, roadmap checks, package consumer smoke, mutation testing, and independent review.

## Documentation and packaging assessment

- Package metadata and packed README path match the project file. The package is an opt-in Cache provider and depends on `Naravel.Cache`.
- The package documentation and Cache guide accurately describe current behaviour: ttl null means no expiry; all stores share one server pool; FlushAsync flushes the entire cluster; lock release is not atomic; and counter calls are synchronous.
- These constraints were historically approved in PDR-007 but are now explicitly vetoed as target architecture: AV-001 (cluster-wide flush), AV-002 (lock release), AV-004 (blocking counter I/O), and AV-005 (single server pool). Their documentation does not make them safe or acceptable for the final system.
- Enyim's sub-second TTL conversion is not documented; MCD-010 requires provider documentation and tests to state the actual supported precision.
- The guide's generic cancellation/error statements do not explain the post-dispatch cancellation behavior or the result-loss paths in MCD-001/MCD-002.
- No PDR statement defines how the signed `long` Cache counter contract maps to Memcached's unsigned 64-bit arithmetic, especially overflow, negative stored values and decrement underflow.
- No documentation statement defines whether nullable integral null values are supported for this provider.

## Risk summary and next action

- P0: none established.
- P1: MCD-006 and MCD-007 (Architecture Vetoes AV-001 and AV-002).
- P2: MCD-001, MCD-002, MCD-003, MCD-004, MCD-008, MCD-009 and MCD-010 (AV-004, AV-005 and AV-008).
- P3: MCD-005.
- OBS: no separate observation added; the Memcached decrement-underflow difference is recorded as an owner decision within MCD-003.
- Recommended next task: treat MCD-006 and MCD-007 as release-blocking architecture remediation; then resolve MCD-008 through MCD-010 alongside MCD-001 through MCD-005. No runtime code, tests or dependencies were changed by this audit.
