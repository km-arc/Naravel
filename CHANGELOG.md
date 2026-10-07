# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). All packages share one version (monorepo, lock-step).

## [Unreleased] - 0.1.0-preview

### Added
- R01 queue/cache driver contract suites, environment-gated provider integrations, SQLite queue coverage, and Redis atomicity/Kafka offset-order regressions; CI now runs cross-OS fast tests and service-container integration tests.
- BenchmarkDotNet baselines and a sample queue connection selector for running the same queue workflow with File, Redis, RabbitMQ or Kafka.
- `Naravel.Foundation`: `Manager<TDriver,TOptions>`, `IDriverRegistry<TDriver>`, hot reload, runtime `Extend`.
- `Naravel.Cache` (PDR-007): named Memory stores, `CacheManager`/`LockManager` on Foundation, tagged and scoped cache views, `RememberAsync`, and token-owned locks. Opt-in `Naravel.Cache.Redis` and `Naravel.Cache.Memcached` providers.
- R04 Cache rate limiting (PDR-007a): named fixed-window limits with atomic TTL-aware counters for Memory/Redis/Memcached, explicit subject hashing, provider-backed locks, `Naravel.Cache.Testing.RateLimiterFake`, metrics and activity spans. A boundary burst up to 2x is documented; benchmark the Memory path before merge, and defer a limiter-specific Redis-atomic fast path.
- R05 Routing (PDR-009): local fixed-window throttle, Data Protection signed/temporary URLs, maintenance mode, cache headers, query/form normalization, guest middleware, and conventional MVC controller resource routes.
- `Naravel.Queue` with Memory, File, Redis, Database (EF Core), RabbitMQ and Kafka drivers; worker with retry/backoff,
  chaining, batching, middleware and lifecycle events.
- R07 Queue completion: failed-job stores and retry commands, reservation attempt accounting, per-job timeouts, persistent Database/Redis batches, worker controls, metrics/tracing, and `Naravel.Queue.Testing.QueueFake`.
- `Naravel.Queue.RabbitMQ` migrated to RabbitMQ.Client 7.2.2 async APIs with per-delivery channels and async disposal.
- Crash recovery (`VisibilityTimeoutSeconds`) for the File and Database drivers; atomic file writes.
- Worker hardening: survives driver errors, no double execution after acknowledgement, releases in-flight jobs on shutdown.
- Monorepo scaffolding: central package management, shared build props, CI workflow, `.editorconfig`, `.gitignore`, `AGENTS.md`.

- `Naravel.Routing` (Stage 4a, PDR-009): Laravel-style registrar, groups, named routes + `IUrlGenerator`, `where`, route model binding, resource routes
  and a route middleware engine (aliases, groups, parameters, priority, `withoutMiddleware`, terminable, controller attributes); EN+FA docs.

- `Naravel.Queue` migrated onto `Naravel.Foundation` (PDR-006): `QueueManager : Manager<IQueueDriver, QueueOptions>`, shared `AddQueueDriver` registration helper, config key `Connections` -> `Stores` (PDR-005), hot-reload-aware worker.

### Changed
- `ICacheStore` adds `IncrementAsync(key, by, ttl)`; external custom stores must implement the TTL-aware atomic counter operation.
- `ConfigureQueueJobs()` now maps failed-job and batch tables and `QueueJobs` connection/trace columns; apply an EF migration after upgrading.
- Pre-1.0 Queue interfaces gained durable-store and batch operations. Custom `IFailedJobStore` and `IBatchRepository` implementations must add the new members; `SleepWhenEmpty` remains an alias for `Rest`.
- `RabbitMQ.Client` is centrally upgraded from 6.8.1 to 7.2.2; the provider now uses async connection/channel APIs.

### Security
- Pin the test-only SQLite native library to `SQLitePCLRaw.lib.e_sqlite3` 2.1.13 to avoid the high-severity advisory affecting 2.1.11.
- `Naravel.Filesystem`: fixed a path-traversal vulnerability in `LocalStorageDriver.GetFullPath` (paths such as `../../etc/passwd` resolved outside the disk root). Paths outside the root now throw `UnauthorizedAccessException`; regression coverage added.
- `Naravel.Filesystem` migrated onto Foundation under approved PDR-008: `Default`/`Stores`, runtime `Extend`, config reload, manager-owned driver disposal, and S3 region handling. EN/FA filesystem docs added.

### Documentation
- Reconciled README (EN/FA), `AGENTS.md`, `PROGRESS.md`, parity tables and next-agent prompts with the implemented Cache state; CI workflow runs are linked for verification results.
- CI workflow runs are the source for build and test verification results: [GitHub Actions](https://github.com/km-arc/Naravel/actions/workflows/ci.yml).
- Added `ROADMAP.md` as the authoritative project plan with dependency/gate-aware priorities R00–R17, a Persian owner overview, parity matrix, audit, backlog, stage briefs, and a roadmap consistency checker. CI reports checker results; prior Filesystem and Cache work is recorded without treating unfinished RateLimiter work as complete.

### Fixed
- `Naravel.Queue.Redis`: atomically push, promote delayed work, reclaim reservations, reserve, ack, release and fail with Lua scripts.
- `Naravel.Queue.Kafka`: serialize consumer operations, commit only contiguous acknowledged offsets per partition, and prioritize up to 256 records already available in a poll batch.
- `Naravel.Queue.RabbitMQ`: honor queue message priority using broker priority queues. Existing ready queues created without `x-max-priority` must be recreated before upgrade.
- `Naravel.Queue`: replace assembly-qualified job type resolution with an explicit alias registry, reject unknown/non-job payload types before deserialization, and deserialize at most once per attempt. This changes the queue wire identifier; worker-only processes must register their jobs, and legacy payloads need an explicit compatibility alias.
- `Naravel.Queue`: preserve the original connection, queue, priority, and max-attempt setting through static and runtime chain continuation; add optional source-generated JSON metadata.
- Cache tag reads/writes use a stable version zero until the first invalidation, preventing concurrent first-use requests from writing under divergent versions.
- Named Memory stores now default to store-name prefixes; Memcached null TTL means no expiry. Memcached server lists are validated because the provider shares one pooled client across named stores.

### Changed (dependencies and build hygiene)
- Added `Microsoft.CodeAnalysis.PublicApiAnalyzers` as a private source-project analyzer and enabled SDK package validation for source packages.
- `Directory.Packages.props`: Microsoft.Extensions.* / EF Core / AspNetCore.TestHost 10.0.0 -> 10.0.10, StackExchange.Redis 2.8.16 -> 2.13.17, Confluent.Kafka 2.5.3 -> 2.15.1, Microsoft.NET.Test.Sdk 17.14.1 -> 18.0.1. Removed the unreferenced `Microsoft.Extensions.Configuration.Memory` entry.
- Package audit updates: Microsoft.Extensions.* and EF Core packages from 10.0.10 to 10.0.12, `EnyimMemcachedCore` from 3.2.0 to 3.5.1, `MessagePack` from 2.5.301 to patched 3.1.7 (required by EnyimMemcachedCore 3.5.1), and `Microsoft.NET.Test.Sdk` from 18.0.1 to 18.10.1. Removed the unused `Microsoft.Extensions.Hosting` version entry.
- Added the test-only `coverlet.collector` package to collect Cobertura coverage reports as CI artifacts; no coverage threshold is enforced.
- `Naravel.Cache.Memcached.csproj` declares `MessagePack` and `Newtonsoft.Json`; no C# source usages were found. Both references are retained for their provider dependency requirements.
- NuGet vulnerability audit is now enabled repo-wide (`Directory.Build.props`, mode `all`); the per-project `NuGetAudit=false` in 7 projects was removed. (With audit disabled, `dotnet list package --vulnerable` can never report anything.)
- `nuget.config`: removed a machine-specific local cache source (`/home/<user>/nuget-cache`) that broke restore on any other machine and on CI.
- `Naravel.Foundation`: replaced `FrameworkReference Microsoft.AspNetCore.App` (no ASP.NET code is used) with explicit `Microsoft.Extensions.DependencyInjection.Abstractions`, `Options`, `Options.ConfigurationExtensions` references, so non-web hosts do not pull in the ASP.NET shared framework.
- `Naravel.Filesystem`: removed `TreatWarningsAsErrors` (no other project had it; with audit on, a vulnerability warning would have failed the build). Decide the repo-wide warning policy in PDR-008.

### Verification
- Build, test, and workflow verification results are available from [GitHub Actions](https://github.com/km-arc/Naravel/actions/workflows/ci.yml).

### Known gaps
- Package audits report no vulnerable packages. Major-version updates needing owner approval remain unchanged: `StackExchange.Redis` 3.x, `AWSSDK.S3` 4.x, `SQLitePCLRaw.lib.e_sqlite3` 3.x, `FluentAssertions` 8.x, and `xunit.runner.visualstudio` 4.x.
- `AWSSDK.S3` (3.7.300 -> 4.x) and StackExchange.Redis 3.x are intentionally **not** upgraded; they need separate approval.
- Redis failed-job/batch, RabbitMQ, Kafka and Memcached live provider tests require the CI service job or matching local services and were not run in the R07 verification. Ready-made HTTP middleware remains staged in R05.
