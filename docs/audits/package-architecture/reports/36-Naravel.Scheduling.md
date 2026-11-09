# Review Report — Naravel.Scheduling

## Review metadata

- Package ID: Naravel.Scheduling.
- Source project: src/Naravel.Scheduling/Naravel.Scheduling.csproj.
- Baseline: main at d3f4342db8aa7d0f8085d1205f76f6ae5bfa8ba3.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source, concurrency and test review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; multi-instance lock expiry not reproduced).
- Files inspected: Schedule, SchedulePattern, ScheduledTask, Scheduler, SchedulerHostedService, SchedulingOptions, telemetry, fake, frequency/hosting/scheduler tests, README and Scheduling guide.
- Dependencies: Naravel.Cache lock abstractions, Microsoft.Extensions.Hosting/DI/logging, and Cronos.
- Verification performed: schedule matching/time-zone behavior, concurrent execution, overlap lock, one-server slot lock, options validation, failure hooks and telemetry inspected.
- Verification NOT RUN: restore, build, tests, Redis/Memcached multi-server timing, lock-expiry interleaving, full CI, mutation testing and independent review.

## Executive assessment

The scheduler evaluates each due minute and runs due tasks concurrently. It has separate locks for preventing overlap and for ensuring one server runs a task in a given minute. It rejects OnOneServer when the configured lock driver is process-local.

The per-minute lock is intentionally not released early. Its default lifetime is five minutes, and the option documentation says it must outlive the minute and clock differences between servers. However, the constructor checks only that SlotLockTtl is positive. A caller can set it to 30 seconds, which is shorter than the scheduling slot. A delayed second instance can then acquire the same slot lock while the original minute is still being processed.

## Package and dependency map

- Schedule definition: Schedule and ScheduledTask.
- Time rules: Cronos-backed SchedulePattern with explicit time-zone handling.
- Runner: Scheduler and SchedulerHostedService.
- Locks: ICacheLock, with one-server and overlap keys.
- Testing: SchedulerFake and manual-time test support.
- Dependencies: Cache lock abstraction and hosting/DI/logging abstractions; no concrete Redis/Memcached client dependency.

## Findings

### [P2] SCH-001 — A short positive SlotLockTtl can expire while the same minute is still eligible to run

- Remediation state: OPEN.
- Evidence status: SEEN IN SOURCE; multi-instance short-TTL reproduction NOT RUN.
- Evidence: SchedulingOptions.SlotLockTtl defaults to five minutes; its XML documentation says the lock must outlive the minute and clock difference between servers. Scheduler.RunAsync acquires a lock keyed by task name and the minute, and intentionally never releases it early. Scheduler's constructor rejects zero/negative values but accepts any positive TimeSpan. SchedulerTests.Options_must_be_positive tests zero only; the OnOneServer tests use the default TTL.
- In everyday words: The scheduler lets an operator configure a one-server lock that lasts less than the minute it is supposed to protect. That setting is accepted even though it cannot reliably cover the whole scheduling slot.
- Realistic example: Server A starts the 12:00 task and acquires a 30-second slot lock. Server B is delayed and reaches the same 12:00 slot after 12:00:30. The lock has expired, so B can also run the task even though both executions belong to the same minute.
- Why the evidence proves the risk: the lock key is shared for the whole minute, but its lifetime is freely configurable down to any positive value. Once the lease expires, the lock backend allows another server to acquire it. No scheduler-level check ensures the lock still covers the active slot.
- What is not proven: source inspection confirms the accepted configuration and lock key; no two-server test with a short positive SlotLockTtl was run.
- Required safe outcome: the one-server slot lock must outlive the whole minute plus the maximum supported clock difference and scheduler start delay, or the system must reject/document values that cannot meet that requirement. The minimum safe value needs an explicit owner-approved bound; do not guess an arbitrary skew allowance.
- Regression tests required: run two Scheduler instances against one shared controllable lock; let A acquire near the beginning of a minute, advance past a short configured TTL while still representing the same minute for B, and prove the invalid setting is rejected or B remains blocked. Also test the minimum approved value and default five-minute behavior.
- Public API / PDR / release impact: validation/contract clarification for SchedulingOptions.SlotLockTtl. Owner decision is needed to define the supported clock-skew/start-delay bound before enforcing a minimum.

## Existing guarantees and coverage

- Tasks that are due run concurrently; a slow task does not prevent another due task from starting.
- A failure in one task is logged and isolated from other tasks.
- WithoutOverlapping uses a separate lock and releases it after normal failure/cancellation; a TTL protects against a crashed process holding it forever.
- OnOneServer checks whether the lock driver is shared and refuses a process-local lock.
- The per-minute slot lock is intentionally retained until expiry so a second pass in the same minute is skipped.
- Telemetry tags use task name and outcome; exception detail is not used as a metric tag.
- Tests cover frequency rules, timezone behavior, overlap, one-server selection, local/shared lock validation, task failures/hooks and metrics.

## Test assessment

- Tests inspected: FrequencyTests, HostingTests and SchedulerTests.
- Missing test: a short but positive SlotLockTtl across two scheduler instances.
- Tests actually run in this review: NONE.
- Live Redis/Memcached, build/CI, mutation testing and independent review: NOT RUN.

## Documentation and packaging assessment

- The package README and guide explain the one-server lock and that it must outlive the minute plus clock difference.
- The default is five minutes.
- The code currently accepts a positive lifetime shorter than the documented requirement, so the options validation does not enforce the written operational rule.

## Risk summary and next action

- P2: SCH-001.
- Recommended next task: owner decision on the maximum supported clock difference/start delay, then add validation and the two-server regression test. No runtime change is authorized by this audit.

## Plain-language explanation

Scheduling can use a shared lock so only one server runs a task in a particular minute. The default lock lasts five minutes, but the setting currently accepts any positive value. If someone changes it to 30 seconds, a delayed second server can arrive later in the same minute after the first lock expired and run the same task too. The code and missing test were inspected; the scenario was not reproduced against a live shared lock.
