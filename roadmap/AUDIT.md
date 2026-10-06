# Roadmap audit

This file records verified gaps found while preparing or executing stages. It does not change scope or authorize work;
each open finding must be owned by a roadmap stage.

## Open findings

| ID | Finding | Evidence / impact | Owner |
|---|---|---|---|
| D-01 | Queue deserialization lacks an explicit job-type/interface guard at the worker boundary. | `IJobSerializer.Deserialize` returns `object`; `QueueWorkerService` casts deserialized values to `IJob`. Invalid or mismatched serializer output should fail with a clear, intentional error and regression test rather than an incidental cast failure. | R00.T01 |
| D-02 | Chained job metadata does not persist the effective connection chosen for the original chain dispatch. | `ChainLink` contains type and payload only, while chain continuation rebuilds dispatch using the message queue; verify/fix effective connection propagation so follow-up jobs stay on the requested connection. Add a regression test using distinguishable stores. | R00.T03 |
| D-04 | Queue feature parity is incomplete. | Failed-job repository/retry commands, queue middleware coverage, driver gaps, chain/batch features, and worker controls remain part of the staged scope; see R07/R08. | R07.T01 |
| D-05 | Shared Redis connection/client lifecycle is not extracted for reuse. | Cache and Queue have provider-specific Redis use; a shared connection module requires an approved dependency/boundary decision and provider tests. | R03.T01 |
| D-06 | Cache RateLimiter integration is not implemented. | Cache/tagging/scoping/locks are complete under PDR-007, but stage R04 is intentionally incomplete until RateLimiter scope and dependencies are addressed. | R04.T01 |
| D-07 | Ready-made HTTP middleware and routing follow-ups are incomplete. | Routing stage status is tracked in ROADMAP; Stage 4b middleware and controller resource mapping are absent and R05 has an owner `GO` gate. Verification results are reported in [GitHub Actions](https://github.com/km-arc/Naravel/actions/workflows/ci.yml). | R05.T01 |
| D-08 | Remaining Laravel-inspired modules/features have not been implemented. | Events, Mail, Notifications, Session, Auth-lite, Console/Scheduling, Broadcasting, Hashing, Pagination, and release hardening are staged in R06–R17, each behind its PDR/approval gate. | R06.T01 |
| D-09 | **Security:** worker resolves `Type.GetType(message.JobType)` from the queue store and deserializes into that arbitrary type *before* any `IJob` check. | `QueueWorkerService` L123/186/210/238 via `JsonJobSerializer.ResolveType`. Anyone who can write to Redis/DB/file queue can make the worker instantiate any public type. Guard must run **before** `Deserialize` (`typeof(IJob).IsAssignableFrom`), ideally with an optional allow-list. Widens D-01. | R00.T01 |
| D-10 | Chain continuation drops all dispatch options except queue. | `ContinueChainAsync` (L176-187) passes only `OnQueue(message.Queue)`: connection, priority, max-attempts and delay of the original dispatch are lost, for both runtime `context.Then` and static chains. Widens D-02. | R00.T03 |
| D-11 | `IFailedJobStore` is registered but never called. | Only reference outside its file is `TryAddSingleton` in `Queue/Extensions/ServiceCollectionExtensions.cs:28`; the worker never calls `RecordAsync`. Interface is dead code today. | R07.T02 |
| D-13 | Reclaimed reservations do not count an attempt; visibility timeout is unrelated to job timeout. | Redis `ReclaimExpiredReservationsAsync` and Database stale-reserve `ExecuteUpdate` only clear `ReservedAt`. A crashing job (poison) loops forever; a job running longer than the visibility timeout is executed twice. | R07.T03 |
| D-15 | RabbitMQ driver is sync-over-async behind a single-channel `lock`. | `RabbitMQ.Client` 6.8.1 sync API (`BasicGet/BasicAck` under `_channelLock`) conflicts with AGENTS rule 7 (async-first). Migrating to the async client is a breaking dependency change -> needs approval. Package versions here could not be checked against NuGet (no network). | R07.T05 |
| D-17 | Public XML documentation completeness is not enforced. | R01 now treats compiler warnings as errors for `src/**` and sets SDK SourceLink properties in CI; however `CS1591` remains suppressed and package validation is not enabled, so missing XML docs still need a dedicated hardening decision. | R17.T02 |
| D-18 | Competing sources of truth for status and test counts. | README status now points to ROADMAP, and README test totals were removed. `docs/*/laravel-parity.md` and `PROGRESS*.md` still contain repeated status or verification details; keep this finding open until those sources are reconciled. | R00.T04 |
| D-19 | No observability and no shipped fakes. | No `ActivitySource`/`Meter` anywhere in `src`; no `Queue/Cache/Storage` fake test doubles although the Definition of Done requires one per *new* module. Per-job timeout is absent (only global `JobTimeout`). | R07.T07 (queue) / every stage via DoD S3 |
| D-20 | Several Laravel 13 namespaces have no recorded disposition. | Encryption, Validation, Http client, Log, Translation, View, Pipeline, Concurrency, Process, Cookie, Database, Image, JsonSchema, Testing: see the coverage table in `PARITY-MATRIX.md`. `ShouldBeEncrypted` jobs need an Encryption decision. | R18 (Encryption), BACKLOG (rest); decisions OD-03/OD-04 |
| D-21 | Kafka polling still blocks a worker thread during `IConsumer.Consume`. | R01 serialized the shared consumer and fixed contiguous commits, but the Confluent consumer API used here is synchronous; `PopAsync` may block its caller for up to the 200 ms poll timeout. | R07.T05 |

## Resolved / historical notes

- R02 / PDR-008 Filesystem migration was completed; live S3 was not run. Build and test verification is reported in [GitHub Actions](https://github.com/km-arc/Naravel/actions/workflows/ci.yml).
- PDR-007 Cache implementation was accepted; live Redis/Memcached were not run locally. Build and test verification is reported in [GitHub Actions](https://github.com/km-arc/Naravel/actions/workflows/ci.yml).
- Routing Stage 4a / PDR-009's reported CS1734 warning was fixed. R05 tracks the still-open Stage 4b scope; build and test verification is reported in [GitHub Actions](https://github.com/km-arc/Naravel/actions/workflows/ci.yml).
- R00 documentation/status references and the historical CS1734 warning have already been reconciled; retain their
  checklist items as completed-within-stage context and verify no conflicting authoritative pointer remains.
- R01.T01/T03 added reusable queue/cache provider contracts, always-on SQLite queue coverage and env-gated external provider tests. Build and test results are reported in [GitHub Actions](https://github.com/km-arc/Naravel/actions/workflows/ci.yml).
- R01.T05 replaced Redis multi-command transitions with Lua scripts and serialized Kafka consumer operations with contiguous per-partition offset commits; unit tests cover script use and out-of-order ack tracking.
- R01.T04 added a cross-OS fast CI matrix, Ubuntu service containers, source-project warning-as-error enforcement and SDK SourceLink properties; full workflow execution is a CI responsibility.
- R01.T06 recorded the local BenchmarkDotNet baseline in `docs/en/benchmarks.md` and `docs/fa/benchmarks.md`.
- R01.T07's restore/build/test and roadmap verification is reported in [GitHub Actions](https://github.com/km-arc/Naravel/actions/workflows/ci.yml); live provider coverage depends on configured services.
- R00.T05 added the single CI workflow; fast and service jobs now cover local-free and external-provider test matrices.
