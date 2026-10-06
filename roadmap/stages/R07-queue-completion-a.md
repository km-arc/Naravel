# R07 — Queue completion A

Parity section: `R07` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-012 (EN+FA) approved first (RabbitMQ 7.x migration pre-approved by OD-02).
Read: `src/Naravel.Queue/**`, `src/Naravel.Queue.{Redis,Database,RabbitMQ,Kafka}/**`, `tests/Naravel.Queue.Tests/**`, `tests/Naravel.Queue.Providers.Tests/**`, `roadmap/AUDIT.md` rows D-11, D-13, D-15, D-19.
Touch: `src/Naravel.Queue*/**`, `tests/Naravel.Queue*.Tests/**`, `Directory.Packages.props` (RabbitMQ.Client only), `docs/en|fa/queue.md`, PDR-012.
Out of scope: unique/overlap/rate-limit middleware (R08); encrypted jobs (R18); mail/events queue adapters (R06/R09).
Decisions: OD-01, OD-02, OD-07, S1–S3.

## Tasks
- [x] **R07.T01 — PDR-012:** decide: failed-job storage and retry/forget API, attempt accounting on reclaim, per-job `Timeout` and visibility-timeout rule, persistent batch repository and callback model (delegates are not persistable → typed callbacks registered by alias), worker controls, metric names, Queue fake, and an ergonomic registration so the quickstart is ≤ 3 lines (e.g. one builder `services.AddQueue(config, q => q.AddRedis())`). **Accept:** PDR EN+FA with the quickstart.
- [x] **R07.T02 — Failed jobs (D-11):** the worker calls `IFailedJobStore.RecordAsync` on permanent failure; persistent stores in Database (EF model-builder extension) and Redis; `RetryAsync(id)`, `RetryAllAsync`, `ForgetAsync`, `FlushAsync` re-dispatch to the original connection/queue (R00.T03 envelope). **Accept:** tests with Memory + SQLite: fail → listed → retry succeeds → removed.
- [x] **R07.T03 — Attempts and timeouts (D-13):** reclaimed reservations increment `Attempts` (Redis Lua, Database, others as applicable); add `Job.Timeout` (virtual, null = worker default); worker uses the smaller of job/worker timeout; startup validation logs a warning if visibility timeout < job timeout + margin. **Accept:** a crashing job reaches `FailAsync` after `MaxAttempts` reclaims; a job longer than its timeout is cancelled.
- [x] **R07.T04 — Batches:** persistent `IBatchRepository` (Database + Redis), `AllowFailures`, `CancelAsync`, typed then/catch/finally callbacks per PDR. **Accept:** batch state survives recreating the repository (SQLite test); cancel stops remaining jobs.
- [x] **R07.T05 — RabbitMQ async (D-15, OD-02):** move to RabbitMQ.Client 7.x async API, remove the single-channel lock and sync-over-async; keep ack/release/fail semantics; verify the latest 7.x version on NuGet before editing `Directory.Packages.props`. **Accept:** contract suite `NOT RUN` (NARAVEL_TEST_RABBITMQ unset); no `.GetAwaiter().GetResult()` in the project.
- [x] **R07.T06 — Worker controls:** options `StopWhenEmpty`, `MaxJobs`, `MaxRuntime`, `Rest`. **Accept:** Memory-driver tests for each.
- [x] **R07.T07 — Observability and fake (D-19):** `Meter "Naravel.Queue"` (processed/failed/retried counters, duration histogram), `ActivitySource "Naravel.Queue"` with trace context carried in the message, `Naravel.Queue.Testing` fake (`AssertDispatched<T>`, `AssertChained`). **Accept:** tests assert measurements via `MeterListener` and fake assertions.
- [x] **R07.T08 — Validate:** extend benchmarks, docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7.
