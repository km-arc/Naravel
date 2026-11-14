# Review Report — Naravel.Scheduling

## Review metadata

- Package ID: `Naravel.Scheduling`.
- Source project: `src/Naravel.Scheduling/Naravel.Scheduling.csproj`.
- Source-review baseline: `main` at `198b36f9e2753ccfb68354a02480cdc25e7eabc7`; this audit branch contains that commit.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source and contract review; independent second review NOT RUN.
- Status: REVIEWED — NO SOURCE-LEVEL FINDINGS.
- Files inspected: `Schedule`, `ScheduledTask`, `SchedulePattern`, `Scheduler`, hosted service, options, registration, telemetry, fake, frequency/hosting/scheduler tests, package README and guide..
- Dependencies: Naravel.Cache lock abstractions, Cronos and Microsoft.Extensions Hosting/DI/Logging/Options..
- Verification performed: source-to-contract tracing and inspection of the listed tests and documentation.
- Verification NOT RUN: .NET restore/build/test, live provider or service execution unless explicitly stated below, full CI and mutation testing.

## Executive assessment

The scheduler builds named tasks at startup and runs those due at minute boundaries. Each run receives a new DI scope. Overlap locks are released on completion, failure or cancellation; one-server slot locks are retained until expiry for that minute. Startup rejects OnOneServer when the lock reports it is process-local. No source-level finding was confirmed.

## Package and dependency map

- Definition: `Schedule.Call` and `ScheduledTask` frequency/filter/guard methods.
- Parsing: Cronos for five-field cron; minute intervals align to Unix epoch.
- Runtime: `SchedulerHostedService` and `Scheduler`.
- Guards: `WithoutOverlapping` and `OnOneServer` use `ICacheLock`.
- Fake: `SchedulerFake`.

## Findings

No source-level findings were confirmed in the inspected scope. This is not a claim of runtime or production verification.

## Test assessment

- Existing tests inspected: `FrequencyTests`, `HostingTests`, `SchedulerTests`..
- Coverage seen in source: cron/intervals, time zones/DST, weekday and time filters, scopes, failures/hooks, overlap/expiry, one-server selection, host timer, cancellation and shutdown waiting..
- Gaps: real multi-process Redis/Memcached lock verification; current two-instance unit tests use a shared in-memory lock double.
- Tests actually run for this review: NONE.
- Build/restore: NOT RUN.
- External service/provider integration: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Documentation and packaging assessment

README and guide state missed minutes are skipped and one-server mode requires a shared lock. No source/doc mismatch was confirmed.

## Risk summary and next action

No source-level findings confirmed.

Recommended next task: run package tests and a real multi-process Redis or Memcached lock test before claiming deployed cross-process exclusivity. Do not change runtime code in this audit without owner approval.

## Plain-language explanation

This package runs named tasks on a schedule and can avoid overlapping runs or make one instance win a minute. Source and tests were inspected; no build or tests ran, and real multi-process locking remains unverified.
