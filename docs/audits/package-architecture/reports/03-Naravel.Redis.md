# Review Report — Naravel.Redis

## Review metadata

- Package ID: `Naravel.Redis` (explicitly declared in the project file).
- Source project: `src/Naravel.Redis/Naravel.Redis.csproj`.
- Baseline commit: `99ea264e11bf4bcb9e4ae87a0d1519452f8570ed` (`main`).
- Review date: 2026-11-07.
- Reviewer mode: principal-architect source review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; runtime verification pending).
- Files inspected: all files under `src/Naravel.Redis/`; Redis test project and its unit/migration tests; Redis PDR and English module/NuGet docs; Cache.Redis and Queue.Redis registration/borrow paths; Foundation registry behavior.
- Verification performed: PackageId/metadata/dependency graph review; PublicAPI shipped/unshipped review; connection creation/ownership/reload path review; cross-module Cache/Queue connection reuse and disposal tests inspected; PDR/documentation consistency review.
- Verification NOT RUN: `dotnet restore`, `dotnet build`, `dotnet test`, real Redis integration tests, concurrency stress tests, injected connection failure/retry tests, and mutation testing.

## Executive assessment

- Overall risk: **Medium (P2)** pending the connection-name collision and blocking connection-open design questions.
- Architectural fit: Strong alignment with the accepted PDR-010 goal: one named multiplexer owned by one Foundation manager, borrowed by Cache and Queue, with compatibility for the previous per-store ConnectionString configuration.
- Strengths: uses the real StackExchange.Redis multiplexer rather than hiding client capabilities; provider adapters borrow rather than own the shared connection; Foundation provides caching/reload/disposal; integration tests verify that cache/lock/queue consumers reuse and do not prematurely dispose the shared multiplexer.
- Summary: The shared-connection architecture is sound at its core. A configured store can occupy the same `implicit-...` namespace used by `FromConnectionString`, causing that method to silently return a connection for a different configured string. Also, the first connection attempt uses synchronous `ConnectionMultiplexer.Connect` even though PDR-010's final quality contract requires asynchronous connection work where available. Finally, the implicit-connection factory closes over the raw string and remains stored in Foundation's long-lived registry; this contradicts the PDR's “never storing” wording and deserves an explicit secret-lifetime decision.

## Package and dependency map

- Public package contract: `IRedisConnection`, `RedisManager.Connection`, `RedisManager.FromConnectionString`, `RedisOptions`, and `RedisServiceCollectionExtensions.AddNaravelRedis`.
- Package metadata: explicit `PackageId=Naravel.Redis`; target framework `net10.0` inherited from shared build metadata; `PackageReadmeFile=README.md`; packed README is `docs/nuget/Naravel.Redis.md`; description and tags are present.
- Project references: `Naravel.Foundation` only.
- Direct package references: `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options.ConfigurationExtensions`, and `StackExchange.Redis`.
- Consumers: `Naravel.Cache.Redis` and `Naravel.Queue.Redis` reference this package; neither directly references StackExchange.Redis in the reviewed project files.
- Registration: `AddNaravelRedis` binds `RedisOptions`, registers Foundation's `RedisManager`, installs a replaceable `Func<string,IConnectionMultiplexer>` opener, and registers configured `Redis:Stores:<name>` connections lazily.
- Ownership: `RedisConnection` owns and disposes the multiplexer. Cache/lock adapters pass `ownsConnection:false`; Queue's Redis driver is created with `ownsConnection:false`; Manager owns the shared `IRedisConnection` and disposes it.
- Public API baseline: shipped API entries exist for the package; `PublicAPI.Unshipped.txt` is empty.

## Findings

### [P2] RED-001 — FromConnectionString can collide with a configured implicit-prefixed name

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE-LEVEL CONTROL-FLOW ANALYSIS; deterministic regression test NOT RUN.
- Evidence: `src/Naravel.Redis/RedisManager.cs:46-58` calculates a name, checks only `_registry.IsRegistered(name)`, and if the name already exists it skips registration and calls `Driver(name)`. `src/Naravel.Redis/RedisServiceCollectionExtensions.cs:39-45` registers every configured store name without reserving/rejecting the `implicit-` namespace.
- Affected contract: `RedisManager.FromConnectionString(string)`; also the compatibility path used by `Naravel.Cache.Redis` and `Naravel.Queue.Redis` when a store supplies `ConnectionString` rather than `Connection`.
- User impact: a call intended to connect to the supplied connection string can silently resolve an unrelated explicitly configured store whose name equals the generated implicit name. The caller can then send cache/queue operations to the wrong Redis endpoint/database.
- Trigger / failure scenario:
  1. A connection string is passed to `FromConnectionString`.
  2. The method deterministically computes `implicit-` + the first 16 hex characters of SHA-256.
  3. A configured Redis store already uses that exact name (the configuration name is unrestricted and can be computed from the string).
  4. `IsRegistered` returns true, so the factory capturing the caller-supplied connection string is not registered.
  5. `Driver(name)` returns the configured store's multiplexer instead of the requested connection.
- Additional risk: the hash is truncated to 64 bits. Distinct strings with a SHA-256 prefix collision also map to one registry key; the current code does not compare the original string after a name match.
- Why existing tests do not catch it: tests prove that equal strings share one connection and that different normal strings open separate connections, but do not register a configured store whose name occupies the generated implicit namespace or force a hash collision.
- Recommended correction: make the name namespace collision-safe. Options include reserving/rejecting configured names beginning with `implicit-`, or keeping an exact identity map for implicit connections and rejecting a name collision when the original string differs. Consider using the full SHA-256 digest rather than 64 bits; a full digest alone does not protect against collision with an explicitly configured name unless the namespace is also reserved/validated.
- Regression tests required: compute the generated implicit name for a test connection string, register a different configured connection under that name, then verify that `FromConnectionString` fails clearly or resolves the requested string without silently returning the configured connection. Add coverage for two distinct strings with a simulated name collision through an injectable name-hasher seam only if such a seam is approved; do not add a public API merely for testing.
- Public API / PDR / release impact: preferably implementation/config validation only; owner approval is required if the accepted configuration namespace is tightened.

### [P2] RED-002 — Initial multiplexer connection is synchronous despite PDR-010's async requirement

- Remediation state: OPEN — OWNER DECISION.
- Evidence status: CONFIRMED BY SOURCE AND PDR COMPARISON; latency reproduction NOT RUN.
- Evidence: `src/Naravel.Redis/RedisServiceCollectionExtensions.cs:37` registers an opener using `ConnectionMultiplexer.Connect(connectionString)`; `src/Naravel.Redis/RedisManager.cs:46-58,61-64` exposes only synchronous `FromConnectionString` / `Connection` flow; `docs/en/redis.md:86-88` explicitly describes the connection as synchronous. PDR-010's final “Contracts and quality” paragraph at line 61 states that connection work is async where the client offers it and accepts a `CancellationToken`. The upstream StackExchange.Redis [Basic Usage documentation](https://github.com/StackExchange/StackExchange.Redis/blob/main/docs/Basics.md) explicitly documents both `ConnectionMultiplexer.Connect` and `ConnectionMultiplexer.ConnectAsync`; whether the pinned 3.4.31 overload provides native cancellation was not established in this source pass.
- Affected contract: first resolution of a configured or implicit Redis connection, including first cache/queue use with the Redis provider.
- User impact: the first caller can block synchronously while DNS, TCP/TLS negotiation, authentication, and Redis connection setup complete or time out. In a request path this ties up a request thread. There is no cancellation path for the connection attempt, and a slow/unavailable Redis endpoint can delay first use.
- Why existing tests do not catch it: all Redis manager tests replace the opener with a synchronous counting delegate that returns immediately; provider integration tests exercise Redis operations after construction, but the source does not test a slow asynchronous connection-establishment path.
- Recommended correction: reconcile the approved public contract with the PDR. If async connection setup is required, investigate the pinned package's `ConnectAsync` overload and cancellation semantics, then use the asynchronous connection API behind Foundation's async factory and expose/establish a predictable async initialization path for consumers. Cache and Queue currently resolve stores synchronously, so this requires an explicit startup/pre-warm or API design rather than a blind delegate substitution. If synchronous first use is an intentional, owner-accepted trade-off, amend the PDR/DoD and docs to record the bounded blocking behavior, connection timeout guidance, and the lack of cancellation.
- Regression tests required: with the existing opener seam, use an asynchronous opener that waits on a controlled gate; verify async resolution awaits it without blocking worker threads and propagates cancellation. If sync-first behavior is retained, add bounded timeout/failed-connect tests and an explicit test/documentation for the blocking contract.
- Public API / PDR / release impact: may require additive public API and a change to how Cache/Queue pre-resolve connections. Owner decision/PDR amendment required before implementation.

### [OBS] RED-003 — The raw connection string is retained by a long-lived factory closure

- Remediation state: OPEN — OWNER DECISION.
- Evidence status: CONFIRMED BY SOURCE AND FOUNDATION REGISTRY LIFETIME.
- Evidence: `src/Naravel.Redis/RedisManager.cs:50-55` registers `provider => Open(provider, name, connectionString)`, capturing the original string. Foundation's `DriverRegistry<TDriver>` retains the factory in its registry entry for the lifetime of that registration; it is not removed after a successful connection is created. The PDR-010 decision at line 39 says the implicit connection is de-duplicated by a hash and the string is “never storing or logging”; the Redis guide at line 87 says it is kept “only inside the factory”.
- Affected contract: `FromConnectionString` secret-lifetime guarantee and the PDR's statement that the raw string is not stored.
- User impact: for a connection created through `FromConnectionString`, the raw connection string—including any password—remains reachable through the long-lived registry delegate closure after the multiplexer has been created. This is not a log/name leak, but it extends the in-memory lifetime of the secret and means “never storing” is not literally true. Named configured stores also remain in configuration/options, so this observation specifically concerns the extra closure retention path.
- Why existing tests do not catch it: tests assert only that the generated connection name does not contain the hostname/password; they do not inspect factory retention or define a secret lifetime contract.
- Recommended correction: first decide whether memory retention after successful connection is an acceptable consequence of supporting retries after failed connection creation. If not, consider replacing the retained factory after successful creation with an identity-preserving factory that no longer captures the raw string, or store only a protected/ephemeral connection descriptor. Do not claim that the string is not stored until the implementation actually guarantees it.
- Regression / verification required: document the chosen retention contract; if the implementation is changed, add a focused test for retry after an initial failed open and prove a successful open no longer needs the raw string in the retained factory. A source-level review of captured delegates may be required; avoid brittle reflection-based tests over private closure internals.
- Public API / PDR / release impact: owner decision about the secret retention guarantee; no public API change is necessarily required.

## Test assessment

- Existing tests inspected: `RedisManagerTests` (named connection caching, default connection, implicit string de-duplication, secret-free generated name, disposal exactly once, unknown name, missing connection string, Extend, reload, unrelated reload, blank input); `SharedConnectionMigrationTests` (Cache store+lock reuse, two store reuse, Cache+Queue sharing named connection, borrowed disposal, queue own/borrowed disposal, constructor compatibility).
- Integration evidence inspected: Cache.Redis registration resolves either `Connection` or `FromConnectionString` and always passes `ownsConnection:false`; Queue.Redis registration does the same. The Queue batch/failed-job repositories borrow the multiplexer from the Queue driver.
- Missing regression coverage: configured-name/implicit-name collision; sync connect latency/cancellation contract; concurrent first-use calls to `FromConnectionString` specifically (Foundation manager's generic once-only behavior is tested elsewhere); secret retention policy.
- Real Redis: the Redis manager unit tests intentionally do not fake the Redis protocol; they use a connection factory stand-in to count creation/disposal. Real Redis integration is covered by env-gated Cache/Queue provider suites, which were inspected but NOT RUN in this audit.
- Tests actually run: none in this remote source-inspection pass.
- Tests NOT RUN: `tests/Naravel.Redis.Tests`, Cache Redis provider tests, Queue Redis provider tests, full solution build/test, real Redis service integration, latency/cancellation reproduction, mutation testing, and independent review.

## Documentation and packaging assessment

- Project metadata and package README mapping match: `PackageId=Naravel.Redis`, `PackageReadmeFile=README.md`, and `docs/nuget/Naravel.Redis.md` is packed to the root README.
- Dependencies are aligned with PDR-010: Foundation plus Microsoft.Extensions abstractions/options and StackExchange.Redis. Cache.Redis and Queue.Redis project-reference Naravel.Redis and do not take a separate StackExchange.Redis package reference in their project files.
- The guide correctly documents the real multiplexer, ownership, reload, Connection/ConnectionString compatibility, factory test seam, and the synchronous `Connect` implementation.
- Documentation mismatch: PDR-010 says connection work is async where the client offers it, while the implementation/guide explicitly use synchronous `ConnectionMultiplexer.Connect`. The PDR or implementation needs a deliberate resolution.
- PDR's phrase “never storing” the implicit string is stronger than the actual factory-closure behavior; qualify or implement a shorter retention lifetime.

## Risk summary and next action

- P0: none established.
- P1: none established from this pass.
- P2: RED-001 and RED-002.
- P3: none established.
- OBS: RED-003.
- DOC: the PDR/source mismatch is described under RED-002; no additional standalone documentation error was classified.
- Recommended next task: owner review of RED-001 and RED-002; decide whether connection strings may be retained in the registry after successful open (RED-003). Prioritize namespace collision validation and PDR/async contract reconciliation. No code has been changed by this audit.
