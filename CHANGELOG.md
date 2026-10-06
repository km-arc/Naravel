# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). All packages share one version (monorepo, lock-step).

## [Unreleased] - 0.1.0-preview

### Added
- R01 queue/cache driver contract suites, environment-gated provider integrations, SQLite queue coverage, and Redis atomicity/Kafka offset-order regressions; CI now runs cross-OS fast tests and service-container integration tests.
- BenchmarkDotNet baselines and a sample queue connection selector for running the same queue workflow with File, Redis, RabbitMQ or Kafka.
- `Naravel.Foundation`: `Manager<TDriver,TOptions>`, `IDriverRegistry<TDriver>`, hot reload, runtime `Extend`.
- `Naravel.Cache` (PDR-007): named Memory stores, `CacheManager`/`LockManager` on Foundation, tagged and scoped cache views, `RememberAsync`, and token-owned locks. Opt-in `Naravel.Cache.Redis` and `Naravel.Cache.Memcached` providers.
- R04 Cache rate limiting (PDR-007a): named fixed-window limiter, atomic TTL-aware counters in Memory/Redis/Memcached, subject-key hashing, provider-backed locking, Fake, metrics and activities.
- R04 Cache RateLimiter (PDR-007a): named fixed-window limits with atomic TTL-aware counters for Memory/Redis/Memcached, explicit subject hashing, provider-backed locks, `Naravel.Cache.Testing.RateLimiterFake`, metrics and activity spans.
- `Naravel.Queue` with Memory, File, Redis, Database (EF Core), RabbitMQ and Kafka drivers; worker with retry/backoff,
  chaining, batching, middleware and lifecycle events.
- R07 Queue completion: failed-job stores and retry commands, reservation attempt accounting, per-job timeouts, persistent Database/Redis batches, worker controls, metrics/tracing, and `Naravel.Queue.Testing.QueueFake`.
- `Naravel.Queue.RabbitMQ` migrated to RabbitMQ.Client 7.2.2 async APIs with per-delivery channels and async disposal.
- Crash recovery (`VisibilityTimeoutSeconds`) for the File and Database drivers; atomic file writes.
- Worker hardening: survives driver errors, no double execution after acknowledgement, releases in-flight jobs on shutdown.
- Monorepo scaffolding: central package management, shared build props, CI workflow, `.editorconfig`, `.gitignore`, `AGENTS.md`.

- `Naravel.Routing` (Stage 4a, PDR-009): Laravel-style registrar, groups, named routes + `IUrlGenerator`, `where`, route model binding, resource routes
  and a route middleware engine (aliases, groups, parameters, priority, `withoutMiddleware`, terminable, controller attributes); 64 tests; EN+FA docs.

- `Naravel.Queue` migrated onto `Naravel.Foundation` (PDR-006): `QueueManager : Manager<IQueueDriver, QueueOptions>`, shared `AddQueueDriver` registration helper, config key `Connections` -> `Stores` (PDR-005), hot-reload-aware worker.

### Changed
- `ICacheStore` adds `IncrementAsync(key, by, ttl)`; external custom stores must implement the TTL-aware atomic counter operation.
- `ConfigureQueueJobs()` now maps failed-job and batch tables and `QueueJobs` connection/trace columns; apply an EF migration after upgrading.
- Pre-1.0 Queue interfaces gained durable-store and batch operations. Custom `IFailedJobStore` and `IBatchRepository` implementations must add the new members; `SleepWhenEmpty` remains an alias for `Rest`.
- `RabbitMQ.Client` is centrally upgraded from 6.8.1 to 7.2.2; the provider now uses async connection/channel APIs.

### Security
- Pin the test-only SQLite native library to `SQLitePCLRaw.lib.e_sqlite3` 2.1.13 to avoid the high-severity advisory affecting 2.1.11.
- `Naravel.Filesystem`: fixed a path-traversal vulnerability in `LocalStorageDriver.GetFullPath` (paths such as `../../etc/passwd` resolved outside the disk root). Paths outside the root now throw `UnauthorizedAccessException`; 7 regression test cases added.
- `Naravel.Filesystem` migrated onto Foundation under approved PDR-008: `Default`/`Stores`, runtime `Extend`, config reload, manager-owned driver disposal, S3 region handling, and 13 additional focused tests. EN/FA filesystem docs added.

### Documentation
- Reconciled README (EN/FA), `AGENTS.md`, `PROGRESS.md`, parity tables and next-agent prompts with the implemented Cache state and current test counts.
- Test counts are now counted from source; see README for the table and for the verification caveat.
- Added `ROADMAP.md` as the authoritative project plan with dependency/gate-aware priorities R00–R17, a Persian owner overview, parity matrix, audit, backlog, stage briefs, and a roadmap consistency checker. The checker passed for all 18 stages and dependencies; prior verified Filesystem and Cache work is recorded without treating unfinished RateLimiter work as complete.

### Fixed
- `Naravel.Queue.Redis`: atomically push, promote delayed work, reclaim reservations, reserve, ack, release and fail with Lua scripts.
- `Naravel.Queue.Kafka`: serialize consumer operations, commit only contiguous acknowledged offsets per partition, and prioritize up to 256 records already available in a poll batch.
- `Naravel.Queue.RabbitMQ`: honor queue message priority using broker priority queues. Existing ready queues created without `x-max-priority` must be recreated before upgrade.
- `Naravel.Queue`: replace assembly-qualified job type resolution with an explicit alias registry, reject unknown/non-job payload types before deserialization, and deserialize at most once per attempt. This changes the queue wire identifier; worker-only processes must register their jobs, and legacy payloads need an explicit compatibility alias.
- `Naravel.Queue`: preserve the original connection, queue, priority, and max-attempt setting through static and runtime chain continuation; add optional source-generated JSON metadata.
- Cache tag reads/writes use a stable version zero until the first invalidation, preventing concurrent first-use requests from writing under divergent versions.
- Named Memory stores now default to store-name prefixes; Memcached null TTL means no expiry. Memcached server lists are validated because the provider shares one pooled client across named stores.

### Changed (dependencies and build hygiene; verified by build + tests on 2026-10-04)
- `Directory.Packages.props`: Microsoft.Extensions.* / EF Core / AspNetCore.TestHost 10.0.0 -> 10.0.10, StackExchange.Redis 2.8.16 -> 2.13.17, Confluent.Kafka 2.5.3 -> 2.15.1, Microsoft.NET.Test.Sdk 17.14.1 -> 18.0.1. Removed the unreferenced `Microsoft.Extensions.Configuration.Memory` entry.
- NuGet vulnerability audit is now enabled repo-wide (`Directory.Build.props`, mode `all`); the per-project `NuGetAudit=false` in 7 projects was removed. (With audit disabled, `dotnet list package --vulnerable` can never report anything.)
- `nuget.config`: removed a machine-specific local cache source (`/home/<user>/nuget-cache`) that broke restore on any other machine and on CI.
- `Naravel.Foundation`: replaced `FrameworkReference Microsoft.AspNetCore.App` (no ASP.NET code is used) with explicit `Microsoft.Extensions.DependencyInjection.Abstractions`, `Options`, `Options.ConfigurationExtensions` references, so non-web hosts do not pull in the ASP.NET shared framework.
- `Naravel.Filesystem`: removed `TreatWarningsAsErrors` (no other project had it; with audit on, a vulnerability warning would have failed the build). Decide the repo-wide warning policy in PDR-008.

### Verified
- 2026-10-06, R07: `dotnet restore Naravel.slnx`, Release build and full tests succeeded; 216 passed, 7 skipped, 0 failed. Redis, Memcached, RabbitMQ and Kafka service tests were skipped because their `NARAVEL_TEST_*` variables were unset. `roadmap/check.py` passed.
- 2026-10-05, R01: full restore/build/test passed with 202/205 tests (Redis and SQLite live; RabbitMQ/Kafka/Memcached skipped); `roadmap/check.py` and the NuGet vulnerable-package audit passed. Sample queue dispatch ran against Redis.
- 2026-10-05, after the Cache consistency fixes: `dotnet build Naravel.slnx -c Release --no-restore` built all 20 projects without warnings/errors; `dotnet test Naravel.slnx -c Release --no-restore` passed 199/199, including 27 Cache tests. Live Redis/Memcached/AWS integration was not run.
- 2026-10-04, **after** the changes above (.NET runtime 10.0.12, fresh restore): `dotnet build Naravel.slnx -c Release` built all 16 projects cleanly; `dotnet test` 159/159 passed. NuGet audit was on and produced no NU19xx vulnerability warnings, so the Foundation `FrameworkReference` removal and every version bump compiled and passed.
- 2026-10-04, after the approved Filesystem migration: `dotnet build Naravel.slnx -c Release` built all 16 projects; `dotnet test` passed 172/172, including 20 Filesystem tests. Live AWS/S3 integration was not run.

### Known gaps
- `dotnet list Naravel.slnx package --vulnerable|--outdated` printed no package table on the owner's machine (twice). Treat "no vulnerabilities" as supported only by the clean audited build above, and try the commands per project (for example `dotnet list src/Naravel.Queue.RabbitMQ package --outdated`).
- `AWSSDK.S3` (3.7.300 -> 4.x) and StackExchange.Redis 3.x are intentionally **not** upgraded; they need separate approval.
- Redis failed-job/batch, RabbitMQ, Kafka and Memcached live provider tests require the CI service job or matching local services and were not run in the R07 verification. Ready-made HTTP middleware remains staged in R05.
