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

## Second-pass architecture revalidation — 2026-11-08

- Revalidation baseline: `main` at `44e957ca3db6d9f15665fc8fa070f70522c163c9`, after PR #243 reverted PR #241.
- Revalidation method: targeted source-level reinspection of the previously cited implementation paths against the current `main`; the earlier package-specific source baselines remain recorded above.
- Outcome: RED-001 through RED-003 remain supported by the cited source paths and remain OPEN / OWNER DECISION as previously classified.
- Verification NOT RUN in this pass: .NET restore/build/test, live provider integration, concurrency stress reproduction, mutation testing, and independent reviewer assessment. Source-level confirmation is not presented as runtime reproduction.
- No runtime code, tests, dependencies, public API, or roadmap stage was changed by this revalidation.

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

### [P2] RED-002 — Synchronous first Redis connection blocks the first caller

- Remediation state: OPEN.
- Architecture disposition: VETO AV-004 — connection establishment must be asynchronous and pre-initialized before synchronous consumer paths.
- Evidence status: CONFIRMED BY SOURCE; latency/cancellation reproduction NOT RUN.
- Evidence: src/Naravel.Redis/RedisServiceCollectionExtensions.cs:36-37 registers ConnectionMultiplexer.Connect(connectionString); src/Naravel.Redis/RedisManager.cs:46-58, 61-64 exposes only synchronous FromConnectionString / Connection flow. PDR-010's Contracts and quality section requires async connection work where supported; roadmap D-66 records the synchronous deviation because Cache and Queue resolve stores synchronously.
- Affected contract: first resolution of a configured or implicit Redis connection, including first cache/queue use.
- User impact: the first caller blocks while DNS, TCP/TLS, authentication and connection setup complete or time out. In request-driven paths this consumes a request thread; under concurrent cold starts or an unavailable endpoint it can cause ThreadPool pressure and delayed startup/requests. The connection attempt has no caller cancellation path.
- Why existing tests do not catch it: manager tests use an immediate synchronous factory delegate; provider tests begin after construction and do not pause connection establishment.
- Required correction: add deterministic async connection creation/pre-warm and make Cache/Queue startup resolve Redis through that async path. Synchronous access may return only an already-created connection; it must not secretly initiate network I/O. Do not simply put ConnectAsync inside the existing synchronous factory or wrap Connect in Task.Run.
- Regression tests required: a gated async opener must remain non-blocking to the caller until released; cancellation of the pre-warm wait must be observed; concurrent first resolution must create one multiplexer; sync access before pre-warm must fail clearly rather than block.
- Public API / PDR / release impact: may require additive async manager/initialization API and startup wiring in Cache/Queue. D-66's historical rationale does not waive this target-architecture requirement.

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
- P2: RED-001 and RED-002 (RED-002 is Architecture Veto AV-004).
- P3: none established.
- OBS: RED-003.
- DOC: PDR-010/source mismatch is described under RED-002 and is now an explicit veto rather than an accepted deviation.
- Recommended next task: preserve the shared-multiplexer ownership design, but move first-use creation to an async pre-warm lifecycle. Keep the connection-string retention question as a separate security/secret-lifetime decision. No runtime code has changed in this audit.

## Plain-language explanation

### RED-001 — An automatically named Redis connection can accidentally select a different configured connection

- **In everyday words:** The helper creates a short name from a connection string. If a configured store already has that name, the helper can return that other store instead of opening the connection the caller asked for.
- **A realistic example:** A caller passes connection string A. Its generated name is already used by a configured store that points to Redis B. The helper sees the name is taken and returns Redis B.
- **What we checked:** `src/Naravel.Redis/RedisManager.cs`, around lines 46–58, and `RedisServiceCollectionExtensions.cs`, around lines 39–45. The manager checks only whether the generated name exists; registration does not reserve the `implicit-` name range.
- **Why that proves the risk:** The code checks the name but does not compare the existing connection's original value with the caller's requested connection string.
- **What is not yet proven here:** The collision path was traced in source. A deterministic runtime test with a deliberately colliding configured name was not run.
- **Safe outcome and proof:** A name collision must never silently send work to a different Redis server. Reserve and validate the generated-name range or keep an exact identity check. Test a configured store whose name matches the generated name and prove the helper either uses the requested connection or fails clearly. Consider the shortened hash collision separately.

### RED-002 — The first Redis connection is opened synchronously

- **In everyday words:** The first request to use Redis can block while the application connects over the network. The public path has no cancellation option for that connection attempt.
- **A realistic example:** A request arrives while Redis is unavailable. The first cache call waits through DNS, network and connection timeouts instead of using a connection that was prepared during startup.
- **What we checked:** `src/Naravel.Redis/RedisServiceCollectionExtensions.cs` registers `ConnectionMultiplexer.Connect`; `RedisManager.FromConnectionString` and the connection access path are synchronous. PDR-010 requires asynchronous connection work where available.
- **Why that proves the risk:** The first resolution performs network connection work inside a synchronous factory, so the caller cannot await it or cancel the wait.
- **What is not yet proven here:** The audit did not measure request latency, thread-pool pressure or cancellation under an unavailable Redis server.
- **Safe outcome and proof:** Open and prepare the connection asynchronously before synchronous consumers need it. A synchronous accessor should return only an already-created connection or fail clearly. Test with a gated async opener, cancellation, and concurrent first resolution; do not hide synchronous networking inside `Task.Run`.

### RED-003 — The raw Redis connection string remains in memory longer than the report wording suggests

- **In everyday words:** The helper says it does not store the raw connection string, but the registered factory keeps a copy of it in memory so it can open the connection later or retry.
- **A realistic example:** The string contains a password. Even after the Redis connection is open, the manager's stored factory still holds the original string.
- **What we checked:** `src/Naravel.Redis/RedisManager.cs`, around lines 50–55, registers a factory that captures the string. The Foundation registry keeps that factory for the registration lifetime. The wording in PDR-010 and `docs/en/redis.md` is also cited in the finding.
- **Why that proves the concern:** The retained delegate closes over the original string; it is not removed after a successful connection is created.
- **What is not yet proven here:** This is a source/lifetime observation, not evidence that the password was logged or exposed to another process. No memory-dump test was run.
- **Safe outcome and proof:** The owner must decide whether retaining the string for retry is acceptable. If not, successful connection creation must replace the retained factory with one that no longer needs the secret, while preserving retry after a failed first open. Do not describe the string as “never stored” unless the code actually meets that statement.
