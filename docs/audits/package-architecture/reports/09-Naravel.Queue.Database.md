# Review Report — Naravel.Queue.Database

## Review metadata

- Package ID: `Naravel.Queue.Database` (SDK default from project/assembly name; no explicit `PackageId`).
- Source project: `src/Naravel.Queue.Database/Naravel.Queue.Database.csproj`.
- Source-review baseline: `main` at `c2b7e88b552088b7606286173fa6e51156163f1d`.
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source, contract and provider-test review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; runtime verification pending).
- Files inspected: package project metadata and references; shipped/unshipped API; queue, failed-job and batch record entities; DatabaseQueueDriver; transactional DatabaseJobEnqueuer; failed-job and batch repositories; model/schema extensions; SQLite DDL/resource; DI registration/default configuration; shared queue contract; SQLite provider contract and schema tests; transactional enqueue tests; Queue guide, package README, PDR-012 and PDR-023.
- Dependency map: references `Naravel.Queue` and EF Core abstractions/relational packages; no concrete database provider is referenced. The consuming application supplies SQL Server, PostgreSQL, SQLite or MySQL provider packages. Package remains optional.
- Verification performed: reservation/reclaim/ack/release/fail control-flow review; CAS/fencing path; SizeAsync semantics compared with IQueueDriver contract and other providers; transactional enqueue message creation and downstream chain/retry routing; SQLite value-conversion requirement; schema helper and DDL/test alignment.
- Verification NOT RUN: `dotnet restore`, `dotnet build`, `dotnet test`, SQL Server/Postgres/MySQL service integration, SQLite boundary reproductions, high-concurrency claim tests, mutation testing and independent review.

## Executive assessment

- The core Database driver is provider-agnostic and uses an atomic conditional update to claim rows. Stale reservations increment Attempts; a reservation token fences late acknowledgements/releases/failures; cancellation tokens are passed into EF operations.
- The opt-in SQLite schema helper is safely restricted to Development/Test/Testing. Production schema creation is delegated to consumer-managed EF migrations; no automatic DDL runs at startup.
- Three correctness/contract gaps remain: non-positive visibility timeouts can reclaim live reservations immediately; SizeAsync counts rows currently reserved by workers despite the documented pending-only contract; and DatabaseJobEnqueuer persists a Connection value that PDR-023 says must be ignored, which can route chained jobs and failed-job retries to a different backend.
- The package-specific README presents SQLite in its quickstart but omits the UTC-ticks value converter required by the shipped SQLite DDL and polling query. The full English Queue guide includes the converter, but the package README quickstart does not.
- The PDR-012 implementation record still says no SQL scripts are checked in, although a SQLite script is now embedded and shipped beside the source. It also leaves open whether a SQLite-only development/test schema helper satisfies the approved “SQL scripts” deliverable.
- No runtime code, tests, dependencies or public APIs were changed by this review.

## Findings

### [P2] DBQ-001 — Non-positive visibility timeout can cause immediate concurrent redelivery

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE-LEVEL CONTROL-FLOW ANALYSIS; duplicate-delivery reproduction NOT RUN.
- Evidence: `src/Naravel.Queue.Database/DatabaseQueueDriver.cs:24-27` assigns the supplied visibility timeout (or a five-minute default) without validating that it is positive. In `PopAsync` at lines 42-50, `staleBefore` is calculated as `now - _visibilityTimeout` and every reserved row with `ReservedAt < staleBefore` is reset to unreserved and has Attempts incremented. DI configuration at `src/Naravel.Queue.Database/ServiceCollectionExtensions.cs:64-65` parses `VisibilityTimeoutSeconds` and passes `TimeSpan.FromSeconds` directly to the constructor without a range check.
- Affected contract: the Database driver's visibility/reclaim guarantee for constructor and configured `VisibilityTimeoutSeconds` values less than or equal to zero.
- User impact: with a zero or negative value, a later PopAsync can clear a reservation while the original worker is still executing. Another worker can then reserve and run the same message concurrently. The ReservationId protects stale settlement after a reclaim; it cannot prevent the duplicate execution that the invalid timeout itself triggers.
- Trigger / failure scenario: configure `VisibilityTimeoutSeconds = "0"`, let worker A reserve a row, then worker B calls PopAsync moments later. Since `staleBefore` is now, worker A's earlier ReservedAt is already less than that value; worker B clears the reservation and receives the same job before A has completed.
- Why existing tests do not catch it: provider tests use positive 100 ms/600 ms timeouts and the default five-minute timeout. There is no constructor or DI test for zero/negative values and no two-worker test proving the invalid setting is rejected before a reservation can be reclaimed.
- Required correction: reject zero, negative, NaN and non-finite visibility values before a driver is registered. Validate both the public constructor and the configuration boundary, with an error identifying the store and `VisibilityTimeoutSeconds`. Keep the positive default.
- Regression tests required: constructor rejects TimeSpan.Zero and negative values; configuration rejects zero/negative/non-finite values; a positive small timeout still reclaims after its expected boundary; two workers cannot concurrently receive a live reservation.
- Public API / PDR / release impact: fail-fast configuration validation; no public API change is needed.

### [P2] DBQ-002 — SizeAsync counts reserved/in-flight jobs as pending

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE AND INTERFACE CONTRACT; reserved-row reproduction NOT RUN.
- Evidence: `src/Naravel.Queue.Database/DatabaseQueueDriver.cs:133-136` counts rows where `Queue == queue && FailedAt == null` but does not filter `ReservedAt == null`. `src/Naravel.Queue/Drivers/IQueueDriver.cs` documents SizeAsync as the approximate number of pending (ready + delayed) messages. The File driver counts files in the pending directory only; Memory removes a message from its available queue when it is popped. The shared provider contract checks size before Pop and after Ack, but not while a message is reserved.
- Affected contract: `IQueueDriver.SizeAsync` for the Database provider while one or more jobs are being processed.
- User impact: dashboards, autoscalers and queue-depth alerts observe a reserved job as still pending. The count can therefore overstate work available for another worker and disagree with other providers. During long-running jobs, a queue with no waiting messages may report a non-zero backlog indefinitely.
- Trigger / failure scenario: push one message, call PopAsync and keep its reservation live, then call SizeAsync. The current query returns 1 even though the message is reserved and cannot be popped by another worker.
- Why existing tests do not catch it: `QueueDriverContractTests` asserts size 1 after push and 0 after ack/fail; no test asserts the value between PopAsync and AckAsync.
- Required correction: make the Database definition match the interface's pending semantics by excluding currently reserved rows, or explicitly change the shared contract and all providers if the owner intends SizeAsync to include in-flight work. The recommendation is to preserve the existing documented ready+delayed meaning.
- Regression tests required: push then pop a single message and assert SizeAsync is zero while the reservation is live; add a delayed message and assert it remains counted until reserved; after stale reservation reclaim it should become pending again; keep failed rows excluded.
- Public API / PDR / release impact: behavioral correction to an existing provider implementation; no public API change expected.

### [P2] DBQ-003 — Transactional enqueue preserves Connection even though PDR-023 requires it to be ignored

- Remediation state: OPEN — OWNER DECISION FOR CONNECTION BINDING.
- Evidence status: CONFIRMED BY SOURCE-LEVEL CONTROL-FLOW ANALYSIS; end-to-end chain/retry reproduction NOT RUN.
- Evidence: `DatabaseJobEnqueuer.Enqueue` calls `_factory.CreateMessage(job, configure)` and stores the resulting envelope unchanged through `JobRecordMapper.ToRecord`. The factory is the normal `JobDispatcher.CreateMessage` path: `BuildOptions` copies `Job.Connection` and applies the caller's configure action; `BuildMessage` then sets `Connection = options.Connection ?? _manager.DefaultConnectionName`. This value is persisted in `JobRecord.Connection`. PDR-023 `Contract` explicitly says `options.Connection` is ignored because the job is stored in this database and must be consumed by a worker on the database connection. Downstream, `QueueWorkerService.ApplyChainOptions` uses `message.Connection` for chain follow-ups, and `FailedJobManager.RequeueAsync` republishes through `_queues.Connection(message.Connection)`.
- Affected contract: `DatabaseJobEnqueuer.Enqueue(DbContext, IJob, configure)` for an application whose default queue connection is not the Database store, or whose job/configure callback selects another connection.
- User impact: the initial job is written to the caller's database transaction as intended, but the persisted envelope can claim a Redis/Memory/other connection. If it later creates chained jobs, those follow-ups can be dispatched to the other backend. If it reaches permanent failure, FailedJobManager retry can republish it through that different connection instead of the database queue. This breaks the transactional enqueue routing guarantee and can split one logical job flow across stores unintentionally.
- Trigger / failure scenario: the application's queue default is `redis` and the Database driver is registered as a separate store `db`. Calling `DatabaseJobEnqueuer.Enqueue(db, new MyJob())` uses the ordinary message factory, which records Connection `redis`. The row itself is committed in the database and consumed by the `db` worker, but any chain created from its envelope uses `redis`; failed-job retry likewise resolves `redis`. An explicit `configure: o => o.OnConnection("redis")` has the same issue.
- Why existing tests do not catch it: `TransactionalEnqueueTests` uses a `StubFactory` whose hard-coded Connection is already `database`. It does not use the real registered `JobDispatcher`/`IJobMessageFactory` with a non-Database default or explicit connection. It verifies transaction atomicity, not downstream routing.
- Owner decision required: the enqueuer needs a stable identity for the Database connection that owns the row. Options are (a) require/register one explicit database connection name with the enqueuer and overwrite job/configure Connection, or (b) store no Connection and define how chain and failed-job retry recover the exact database connection when multiple named Database stores exist. A blanket null may route retries through the manager default, which may also be non-Database.
- Architect recommendation: bind the enqueuer to an explicit named Database queue connection (or a narrowly scoped factory whose owner is known) and force the stored envelope to that connection; reject an incompatible selected store instead of silently persisting an external route. Do not let a caller's unrelated OnConnection option override the database transaction boundary.
- Regression tests required: use the real message factory with a Redis default and a separately named Database store; enqueue a plain job, a Job with Connection set, and a configure action with OnConnection; verify the stored envelope retains the owning Database connection; run a chain follow-up and a failed-job retry and assert both stay on that connection.
- Public API / PDR / release impact: likely additive API/DI configuration or factory registration change. Owner decision AD-015 is required before selecting the contract.

### [DOC] DBQ-004 — SQLite package quickstart omits the required UTC-ticks value converter

- Remediation state: OPEN — DOCUMENTATION.
- Evidence status: CONFIRMED.
- Evidence: the package README `docs/nuget/Naravel.Queue.Database.md` demonstrates `UseSqlite` and `modelBuilder.ConfigureQueueJobs()` but does not add any DateTimeOffset converter. The shipped SQLite DDL `src/Naravel.Queue.Database/Schema/sqlite.sql` stores `CreatedAt`, `AvailableAt`, `ReservedAt` and `FailedAt` for `JobRecord` as INTEGER ticks. The full guide `docs/en/queue.md` explicitly states that SQLite cannot compare/order DateTimeOffset values stored as text for the polling query and instructs consumers to add a `ValueConverter<DateTimeOffset, long>` for all four JobRecord properties. The test context in `tests/Naravel.Queue.Providers.Tests/QueueProviderContractTests.cs` also applies the converter manually.
- Affected contract: the Database package quickstart when a consumer follows the shown SQLite example and uses the shipped SQLite script or an equivalent migration without the required converter.
- User impact: the package README's apparently complete SQLite setup omits a required model mapping. Consumers can create the schema and register the driver successfully yet encounter provider translation/comparison failures or incorrect ordering when the worker queries DateTimeOffset columns. The full Queue guide has the missing instruction, but the package README's own quickstart does not surface it.
- Recommended correction: add the SQLite-only UTC-ticks converter in the README's SQLite example and clarify that it is not needed for providers with native DateTimeOffset support. Keep the package README and full Queue guide synchronized.
- Regression / verification required: compile/run the package README's SQLite snippet against a real SQLite schema; assert the model uses integer UTC ticks and that ready/delayed/reclaimed timestamp predicates and ordering work.
- Public API / PDR / release impact: documentation-only.

### [OBS — OWNER SCOPE DECISION] DBQ-005 — PDR-012's SQL-script implementation record is stale and the deliverable scope is ambiguous

- Remediation state: OPEN — OWNER DECISION / PDR RECORD UPDATE.
- Evidence status: CONFIRMED SOURCE AND PDR TEXT.
- Evidence: `PDR-012` owner implementation record item d still says no checked-in SQL scripts or provider migration files were found. Current source contains `src/Naravel.Queue.Database/Schema/sqlite.sql`, embeds it as `Naravel.Queue.Database.Schema.sqlite.sql`, and exposes it through `QueueSchema.SqliteScript` plus an opt-in Development/Test/Testing-only helper. It covers QueueJobs, QueueFailedJobs and QueueBatches. The docs state that production schema changes use consumer-managed EF Core migrations for every provider; no checked-in SQL Server, PostgreSQL or MySQL scripts are present.
- Affected contract: PDR-012's approved schema deliverables and the accuracy of its implementation record.
- User impact: the PDR currently records a completed SQLite schema helper as absent, while the broad wording “Ship SQL scripts for the queue tables” does not state whether SQLite-only is the accepted development/test deliverable or whether the owner expected vendor-specific scripts for all supported providers.
- Owner decision required: confirm whether the SQLite DDL + opt-in development/test helper satisfies the approved scope, with EF migrations as the production path for other providers, or whether provider-specific SQL scripts are still expected. The recommendation is to accept the current SQLite-only helper plus consumer-managed EF migrations, then correct item d in the PDR; avoid maintaining duplicate vendor DDL where EF migrations already provide the supported production schema path.
- Regression / verification required: once scope is agreed, keep the PDR implementation record accurate; existing DatabaseSchemaTests verify the SQLite helper is opt-in/idempotent and its columns match the model. No provider-specific schema script should be claimed unless it exists and has corresponding integration verification.
- Public API / PDR / release impact: PDR scope clarification; owner decision AD-016 required.

## Existing guarantees and verified coverage

- Queue rows are claimed using a compare-and-swap update after selecting at most 20 ready candidates, and the reservation token fences stale Ack/Release/Fail operations.
- Stale reservation reclaim clears the reservation token and increments Attempts; release writes the worker's Attempts and Error values back to the row.
- Cancellation tokens are passed to EF async query, update, save and delete calls.
- The package supplies entities/model mappings for QueueJobs, QueueFailedJobs and QueueBatches and a SQLite-specific idempotent DDL script/helper restricted to Development/Test/Testing.
- Production schema changes are application-managed EF Core migrations; the helper never runs automatically on startup or service registration.
- PDR-023's DatabaseJobEnqueuer is an outbox-by-construction for the Database queue: job rows are tracked by the caller's DbContext and commit/rollback with business rows. Tests cover commit, rollback, delayed availability, queue options, uniqueness rejection, null arguments and unmapped context.
- Shared provider contract tests cover push/pop/ack/release/fail/priority/delay/concurrency; SQLite tests cover stale reservation attempt increments and stale-owner fencing. Schema tests compare DDL/model column names and run the helper twice in Development/Test/Testing while refusing Production/Staging.
- Gaps: no zero/negative timeout tests; no SizeAsync assertion while reserved; transactional enqueue tests replace the production message factory with a stub that hardcodes Connection; package README SQLite example does not compile the UTC-ticks mapping; the PDR schema record is stale/underspecified.

## Test assessment

- Reviewed: Database schema tests; SQLite queue provider contract; transactional enqueue tests; shared QueueDriverContractTests; DatabaseQueueDriver, DatabaseFailedJobStore, DatabaseBatchRepository and DatabaseJobEnqueuer source; package metadata/API baseline; SQLite DDL; Queue guide and NuGet README; PDR-012 and PDR-023.
- Tests run for this review: NONE.
- Build/CI: NOT RUN by this audit session.
- SQL Server/Postgres/MySQL service tests: NOT RUN by this audit session.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Risk summary and next action

- P1: none established in this package review.
- P2: DBQ-001 (non-positive visibility timeout); DBQ-002 (SizeAsync includes reserved rows); DBQ-003 (transactional enqueue misroutes chains/retries through a preserved Connection).
- DOC: DBQ-004 (SQLite README omits UTC-ticks converter).
- OBS / owner decision: DBQ-005 (PDR schema deliverable scope and implementation record).
- Recommended next task: obtain AD-015 and AD-016 before changing the enqueuer API or PDR scope; add regressions for invalid timeout, reserved SizeAsync and real-factory transactional routing; correct the SQLite README converter example. Do not change runtime code or add provider-specific DDL during this audit pass.
