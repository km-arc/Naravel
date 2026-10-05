# R13 — Console and Scheduling

Parity section: `R13` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-017 (EN+FA) approved first.
Read: `src/Naravel.Cache/{LockManager.cs,Abstractions/ICacheLock.cs}`, `src/Naravel.Queue/Worker/**`, ASP.NET Core Generic Host docs.
Touch: new `src/Naravel.Console/**` and/or `src/Naravel.Scheduling/**`, tests, docs/PDR-017.
Out of scope: queue worker internals (R07); `route:list` only if routing exposes endpoint metadata (R05).
Decisions: Native baseline: `System.CommandLine`, Generic Host, hosted services; compare with Quartz/Hangfire.

## Tasks
- [ ] **R13.T01 — PDR-017:** decide Scheduling (fluent `schedule.Call(...).EveryFiveMinutes().WithoutOverlapping()` over Cache locks, hosted service) and whether Commands add value over `System.CommandLine`. Each half may be `DECLINED` separately. **Accept:** PDR EN+FA.
- [ ] **R13.T02 — Scheduling:** timer-driven hosted service with lock-based overlap prevention and single-server mode. **Accept:** fake-clock tests (inject `TimeProvider`), overlap test using two instances over a shared lock.
- [ ] **R13.T03 — Commands (only if kept):** typed command registration. **Accept:** invocation tests.
- [ ] **R13.T04 — Validate:** docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7.
