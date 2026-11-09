# Review Report — Naravel.Cache.Redis

## Review metadata

- Package ID: `Naravel.Cache.Redis` (explicitly declared in the project file).
- Source project: `src/Naravel.Cache.Redis/Naravel.Cache.Redis.csproj`.
- Baseline commit: `99ea264e11bf4bcb9e4ae87a0d1519452f8570ed` (`main`).
- Review date: 2026-11-07.
- Reviewer mode: principal-architect source review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; runtime verification pending).
- Files inspected: all source files under `src/Naravel.Cache.Redis/`; Redis provider tests and Redis JSON tests; shared Cache store contract; Cache lock/store tests; Cache and Redis documentation; PDR-007 and PDR-010; Cache.Redis project metadata and public API baselines.
- Verification performed: project/dependency metadata review; PublicAPI shipped/unshipped inspection; key-prefix/flush implementation and tests inspected; JSON/AOT contract inspected; lock acquisition/release script inspected; config parsing and cancellation behavior traced.
- Verification NOT RUN: `dotnet restore`, `dotnet build`, `dotnet test`, live Redis tests, disconnected-primary flush reproduction, in-flight cancellation reproduction, invalid Database config test, mutation testing, and independent review.

## Executive assessment

- Overall risk: **Medium (P2)** pending the three provider correctness/contract findings.
- Architectural fit: The adapter is thin and follows PDR-007/PDR-010. It keeps Redis-specific cache and lock implementations in an opt-in package, borrows the shared multiplexer from Naravel.Redis, and keeps provider logic separate from Cache core.
- Strengths: prefix escaping is explicit; Redis flush is scoped to the configured store prefix; TTL counter increment and expiry are one Lua operation; lock acquire is SET NX and release is atomic compare-and-delete; JSON source-generation options are supported for trimming/AOT; the adapter uses one shared connection for cache and lock.
- Summary: The main correctness gaps are silent fallback on malformed Redis database selection, flush reporting success when it has skipped every disconnected endpoint, and cancellation tokens that only prevent starting an operation but do not cancel the caller's wait after Redis I/O has begun. Existing tests verify the happy-path prefix/script/AOT contracts and pre-cancelled operations, but do not cover these failure paths.

## Package and dependency map

- Public package contract: `Naravel.Cache.Redis.RedisCacheStore`, `Naravel.Cache.Redis.RedisLock`, `RedisCacheServiceCollectionExtensions.AddNaravelRedisCache`, and `SerializerOptionsKey`.
- Package metadata: explicit `PackageId=Naravel.Cache.Redis`; target `net10.0` inherited from shared build metadata; `PackageReadmeFile=README.md`; package README is `docs/nuget/Naravel.Cache.Redis.md`; description/tags are present.
- Project references: `Naravel.Cache` and `Naravel.Redis`.
- Direct package references: `Microsoft.Extensions.Configuration.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions`. StackExchange.Redis is supplied transitively through Naravel.Redis rather than referenced directly here.
- Registration: `AddNaravelRedisCache` calls `AddNaravelRedis`, then uses `AddCacheDriver` to register one RedisCacheStore and one RedisLock factory per configured Cache store with Driver=redis.
- Connection ownership: the factory resolves a named shared connection through `RedisManager.Connection` or an implicit shared connection through `FromConnectionString`; both adapters are constructed with `ownsConnection:false`. Naravel.Redis is the owner.
- Database selection: both cache and lock factories parse the optional `Cache:Stores:<name>:Database` value and pass `-1` when parsing fails or the setting is omitted.
- Serialization: JSON values use `System.Text.Json`; a keyed `JsonSerializerOptions` registration named by `SerializerOptionsKey` can supply a source-generated `JsonSerializerContext`. Without custom options, reflection-based default metadata is used; unsupported types fail loudly when reflection metadata is unavailable.
- Public API baseline: shipped API entries exist; `PublicAPI.Unshipped.txt` is empty in the reviewed snapshot.

## Second-pass architecture revalidation — 2026-11-08

- Revalidation baseline: `main` at `44e957ca3db6d9f15665fc8fa070f70522c163c9`, after PR #243 reverted PR #241.
- Revalidation method: targeted source-level reinspection of the previously cited implementation paths against the current `main`; the earlier package-specific source baselines remain recorded above.
- Outcome: CRD-001 through CRD-003 remain supported by the cited source paths and remain OPEN; no runtime reproduction is claimed.
- Verification NOT RUN in this pass: .NET restore/build/test, live provider integration, concurrency stress reproduction, mutation testing, and independent reviewer assessment. Source-level confirmation is not presented as runtime reproduction.
- No runtime code, tests, dependencies, public API, or roadmap stage was changed by this revalidation.

## Findings

### [P2] CRD-001 — Invalid Database configuration silently falls back to the connection default

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; config regression test NOT RUN.
- Evidence: `src/Naravel.Cache.Redis/RedisCacheServiceCollectionExtensions.cs:33,39` uses `int.TryParse(store["Database"], out var database) ? database : -1` for both RedisCacheStore and RedisLock. In StackExchange.Redis, database `-1` means use the connection's configured default database.
- Affected contract: `Cache:Stores:<name>:Database` configuration for both the cache store and its lock.
- User impact: a typo or malformed non-empty value (for example, `"Database": "two"`) is treated exactly like the option being omitted. The provider can silently use database `-1` / the connection-string default instead of failing configuration. This can place cache and lock keys in a different logical Redis database than the operator intended and can create unexpected key overlap with another application or module.
- Trigger / failure scenario: the Redis connection string sets `defaultDatabase=4`, while the Cache store intends `Database=0` but is configured as `Database="zero"`. The current factory passes `-1`, so StackExchange.Redis selects the connection's default database rather than surfacing the malformed setting.
- Why existing tests do not catch it: `RedisProviderTests` tests key prefix escaping, flush pattern, lock script, and TTL increment; it does not test the Cache.Redis registration's Database parsing. The Cache documentation shows `Database: "0"` but does not define malformed-input behavior.
- Recommended correction: distinguish missing/empty from invalid. Missing should preserve the documented `-1` connection-default behavior; a non-empty non-integer value should fail fast with a message naming the Cache store and the `Database` option. Validate once and use the same parsed value for both cache and lock factories.
- Regression tests required: missing Database uses `-1`; valid numeric setting is passed to both GetDatabase calls; malformed and overflow values fail clearly and never silently fall back.
- Public API / PDR / release impact: implementation/config validation only; no public API change is needed.

### [P2] CRD-002 — Redis FlushAsync skips disconnected endpoints and may report success without flushing

- Remediation state: OPEN.
- Architecture disposition: AV-001 requires flush to be namespace-safe and complete-or-fail; never report success after silently skipping required primaries.
- Evidence status: CONFIRMED BY CONTROL-FLOW ANALYSIS; disconnected-endpoint reproduction NOT RUN.
- Evidence: `src/Naravel.Cache.Redis/RedisCacheStore.cs:152-165`, especially line 158: `if (!server.IsConnected || server.IsReplica) continue;`. After skipping all endpoints, the method returns normally. The flush test proxy in `tests/Naravel.Cache.Tests/RedisProviderTests.cs:21-38` only models a connected primary.
- Affected contract: `ICacheStore.FlushAsync` for Redis.
- User impact: if the multiplexer has configured endpoints but none is currently connected (or all primary endpoints are disconnected), the method can skip every endpoint and complete successfully even though no cache key was removed. A caller may assume stale cache entries are gone and proceed with invalidation-dependent logic while the old values remain. In a multi-primary topology, skipping a disconnected primary while flushing others can also leave keys behind on that shard.
- Trigger / failure scenario: resolve a RedisCacheStore while Redis is unavailable, or lose all primary connections before calling `FlushAsync`. `GetEndPoints` returns endpoints; each server reports `IsConnected=false`; the loop continues for every endpoint and the method returns a completed task instead of surfacing that the flush was not performed.
- Why existing tests do not catch it: the proxy test explicitly sets `IsConnected=true` and asserts that the configured prefix is scanned/deleted. There is no test for zero connected primaries, a disconnected shard, or a partially completed flush.
- Recommended correction: define flush success semantics for partial availability. At minimum, do not return success if no primary endpoint was actually scanned. For multi-primary/cluster use, detect and surface any primary that could not be scanned rather than silently claiming a complete flush. Preserve the existing skip-replica behavior.
- Regression tests required: all endpoints disconnected must produce a visible failure; replica-only connectivity must not count as a successful flush; a cluster with one disconnected primary must not silently report full success; a connected primary still scans only the escaped configured prefix.
- Public API / PDR / release impact: behavioral reliability correction; no public API change expected. Confirm the expected partial-flush error policy with the owner if changing return/exception behavior.

### [P2] CRD-003 — Cancellation tokens are checked only before Redis I/O, not while awaiting it

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE AND TEST COVERAGE REVIEW.
- Evidence: `src/Naravel.Cache.Redis/RedisCacheStore.cs:94-148` checks the token before issuing each StackExchange.Redis operation, then awaits the returned task without `WaitAsync(cancellationToken)`; `FlushAsync` uses `WithCancellation` on the key iterator but awaits `KeyDeleteAsync` without a cancellable wait. `src/Naravel.Cache.Redis/RedisLock.cs:35-53` has the same early-check-only pattern for SET NX and Lua release. `tests/Naravel.Cache.Tests/CacheStoreTests.cs:80-89` covers a token cancelled before the operation starts; it does not cancel while the Redis task is pending. `docs/en/cache.md:346` documents `OperationCanceledException` for a cancelled token, and PDR-007 line 62 requires async I/O APIs to accept CancellationToken.
- Affected contract: all `ICacheStore` Redis operations and `ICacheLock.AcquireAsync/ReleaseAsync` when cancellation occurs after a call has started.
- User impact: when a request aborts while a Redis call is waiting on network I/O, the provider's returned task continues waiting until StackExchange.Redis completes or times out. Request cancellation therefore does not promptly release the caller's wait, and lock/rate-limit flows may remain pending after the request is no longer interested in the result.
- Important semantic distinction: `WaitAsync(cancellationToken)` cancels the caller's wait; it does not guarantee that an already-issued Redis command is cancelled or rolled back. For Set/Increment/Release, the remote side effect may still complete after the caller receives OperationCanceledException. The API/documentation must preserve that distinction.
- Why existing tests do not catch it: the Redis provider proxies return immediately completed tasks; pre-cancelled tests only test the early guard. No test gives the proxy a pending TaskCompletionSource, cancels after the operation is issued, and verifies the returned provider task stops waiting.
- Recommended correction: wrap the awaitable StackExchange.Redis tasks with `WaitAsync(cancellationToken)` (including flush deletes) where this is the intended Cache contract, while documenting that remote commands may still complete after caller cancellation. Do not claim that cancellation retracts a Redis command. Ensure cancellation does not cause a partially flushed store to be reported as a successful full flush.
- Regression tests required: a pending Redis database proxy operation should remain pending; after the caller token is cancelled, the provider task should promptly throw OperationCanceledException while the underlying simulated command may still complete. Cover TryGet, Set, Increment/TTL increment, lock acquire/release and flush deletion.
- Public API / PDR / release impact: implementation-only, although documentation should clarify best-effort cancellation semantics.

## Test assessment

- Existing tests inspected: `RedisProviderTests` (prefix glob escaping, prefix-only flush, atomic token release script, atomic TTL increment script); `RedisJsonOptionsTests` (source-generated JSON resolver, unknown type fails, default JSON format); `CacheStoreContractTests` (Redis live contract for typed values, basic operations, atomic counters, first-write-only TTL); `CacheStoreTests` (pre-cancelled behavior); `CacheLockTests` (Memory lock token semantics); provider registration/disposal tests in `CacheManagerIntegrationTests`.
- Real Redis coverage: `RedisCacheStoreContractTests` and `RedisJsonOptionsTests` are gated by `NARAVEL_TEST_REDIS`; they were inspected but not executed.
- Additional real-server coverage: `PresenceRedisTests` uses RedisCacheStore and RedisLock with two real Redis connections for presence storage, including a multi-client/parallel join test and expiry check. It does not currently establish the full RedisLock wrong-token/acquire/release contract on the Cache provider itself.
- Missing regression coverage: malformed Database configuration; disconnected/all-disconnected Redis flush; cancellation after a Redis operation has begun.
- Tests actually run: none in this remote source-inspection pass.
- Tests NOT RUN: Cache test project, real Redis provider tests, full solution build/test, roadmap check, package consumer smoke, mutation testing, and independent fresh-session review.

## Documentation and packaging assessment

- Project metadata: explicit `PackageId=Naravel.Cache.Redis`; depends on Cache and Naravel.Redis; `docs/nuget/Naravel.Cache.Redis.md` is declared and packed as root `README.md`.
- Package README accurately describes installation and a basic Redis-backed Cache registration; the full Cache guide contains the more detailed `Database`, prefix, lock, and provider limitations.
- PDR-007 explicitly requires prefix-bounded Redis Flush; the source and current tests implement the safe prefix pattern. This area passed source review.
- PDR-007 requires async I/O methods to accept CancellationToken; signatures do accept it, but the in-flight cancellation behavior is only partial as described in CRD-003.
- JSON source-generation/AOT guidance matches the constructor contract and Redis JSON tests.
- The cache guide states that Redis lock token release is atomic; source uses a compare-and-delete Lua script. The Redis provider unit test checks the generated script text; a dedicated live Redis lock contract for wrong-token release, expiry, and reacquisition was not found in the inspected Cache tests.

## Risk summary and next action

- P0: none established.
- P1: none established from this pass.
- P2: CRD-001, CRD-002, CRD-003.
- P3: none established.
- OBS: none added.
- DOC: cancellation documentation should be precise about the difference between cancelling the caller's wait and cancelling an already-issued Redis command.
- Recommended next task: owner review of CRD-001 through CRD-003. Prioritize fail-fast Database validation and flush completeness; agree cancellation semantics before changing all provider awaits. No code has been changed by this audit.

## Plain-language explanation

### CRD-001 — A typo in the Redis database number can silently choose the connection's default database

- **In everyday words:** If the `Database` setting contains text that is not a number, the adapter treats it like the setting was left out. It does not tell the operator that the value is invalid.
- **A realistic example:** The connection string defaults to database 4. The Cache setting says `"zero"` by mistake. The adapter passes `-1`, so Redis uses database 4 instead of reporting the typo.
- **What we checked:** `src/Naravel.Cache.Redis/RedisCacheServiceCollectionExtensions.cs`, around lines 33 and 39, where `int.TryParse` falls back to `-1` for both cache and lock registration.
- **Why that proves the risk:** Invalid non-empty input and an omitted setting take the same path, even though they should have different meanings.
- **What is not yet proven here:** The parsing behavior is confirmed by source inspection; no registration test for malformed values was run.
- **Safe outcome and proof:** Missing/empty should keep the documented connection-default behavior, but malformed or overflowing text must fail with an error naming the store and setting. Test missing, valid, malformed and overflow values for both cache and lock.

### CRD-002 — Redis flush can report success after skipping every disconnected server

- **In everyday words:** The flush method skips servers that are disconnected. If every server is skipped, the method still returns normally even though it did not remove the cache entries.
- **A realistic example:** Redis is offline when an operator calls `FlushAsync`. The method sees disconnected servers, skips them and completes. The application believes the cache was cleared, but old values remain.
- **What we checked:** `src/Naravel.Cache.Redis/RedisCacheStore.cs`, around lines 152–165, especially the `IsConnected` check. The existing test proxy always reports a connected primary.
- **Why that proves the risk:** The method has a successful return path even when it scans no connected primary.
- **What is not yet proven here:** The audit traced the control flow; it did not disconnect a live Redis endpoint during a flush.
- **Safe outcome and proof:** A flush must either confirm that every required primary was handled or return a visible failure. Test all-disconnected, replica-only and partially disconnected cases, and keep the existing prefix-safety test.

### CRD-003 — Cancelling a request does not stop waiting for an already-started Redis operation

- **In everyday words:** The adapter checks cancellation before sending a Redis command, but once the command starts, it waits for Redis even if the caller has cancelled.
- **A realistic example:** A web request is cancelled while a Redis write is waiting on the network. The request code still waits until Redis responds or times out.
- **What we checked:** `src/Naravel.Cache.Redis/RedisCacheStore.cs` around lines 94–148 and `src/Naravel.Cache.Redis/RedisLock.cs` around lines 35–53. The current tests cover cancellation before an operation starts, not while its task is pending.
- **Why that proves the gap:** The code awaits the provider task without making the caller's wait respond to the cancellation token.
- **What is not yet proven here:** No pending-command cancellation test was run. Cancelling the caller's wait would not undo a command already sent to Redis.
- **Safe outcome and proof:** Define whether cancellation should stop the caller's wait, and document that the remote change may still finish. Test with a pending fake Redis task: cancel after dispatch, prove the caller receives `OperationCanceledException`, then allow the simulated Redis command to finish and verify the documentation does not claim it was rolled back.
