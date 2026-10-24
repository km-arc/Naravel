# R13 — Console and Scheduling

Parity section: `R13` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-017 (EN+FA) approved first.
Read: `src/Naravel.Cache/{LockManager.cs,Abstractions/ICacheLock.cs}`, `src/Naravel.Queue/Worker/**`, ASP.NET Core Generic Host docs.
Touch: new `src/Naravel.Console/**` and/or `src/Naravel.Scheduling/**`, tests, docs/PDR-017.
Out of scope: queue worker internals (R07); `route:list` only if routing exposes endpoint metadata (R05).
Decisions: Native baseline: `System.CommandLine`, Generic Host, hosted services; compare with Quartz/Hangfire.

## Tasks
- [x] **R13.T01 — PDR-017:** decide Scheduling (fluent `schedule.Call(...).EveryFiveMinutes().WithoutOverlapping()` over Cache locks, hosted service) and whether Commands add value over `System.CommandLine`. Each half may be `DECLINED` separately. **Accept:** PDR EN+FA. PDR-017 approved 2026-10-24 (EN+FA): Scheduling accepted as a thin module (Cronos for `Cron`, overlap TTL one hour), Commands `DECLINED` with a recipe; the reasons are in the PDR's Approval section.
- [x] **R13.T02 — Scheduling:** timer-driven hosted service with lock-based overlap prevention and single-server mode. **Accept:** fake-clock tests (inject `TimeProvider`), overlap test using two instances over a shared lock. Added `Naravel.Scheduling` (hosted service, fluent frequencies and filters, `WithoutOverlapping`, `OnOneServer`, hooks, `SchedulerFake`, meter and activity source), `ICacheLock.IsShared`, tests and docs EN/FA; build and tests NOT RUN locally (no .NET SDK); CI is the check. Decisions: frequencies are cron or epoch-aligned intervals (Cronos parses cron, pinned to 0.11.0 in `Directory.Packages.props`, confirmed on NuGet; 0.13.0 is the newest and was not evaluated); tests use a small `ManualTimeProvider` instead of `FakeTimeProvider` to avoid a second unverified package; filtered or not-due tasks are not counted in the metric; missed minutes are skipped.
- [x] **R13.T03 — Commands (only if kept):** typed command registration. **Accept:** invocation tests. Not kept: PDR-017 D2 is `DECLINED`, so there is no code and no invocation test. Done as a recipe: a Commands section in `docs/en|fa/scheduling.md` (System.CommandLine over the Generic Host, an Artisan to .NET table, `schedule:list` and `schedule:run` equivalents); the snippet is documentation and was not compiled.
- [x] **R13.T04 — Validate:** docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7. Docs `scheduling.md` EN/FA (same headings), README EN/FA section and package row, both parity tables, the parity matrix and AUDIT D-63 are updated; the stage status is closed in a later patch that cites the CI run. Build and tests NOT RUN locally (no .NET SDK).
