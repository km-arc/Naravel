# Review Report — Naravel.Queue.RabbitMQ

## Review metadata

- Package ID: `Naravel.Queue.RabbitMQ`.
- Source project: `src/Naravel.Queue.RabbitMQ/Naravel.Queue.RabbitMQ.csproj`.
- Source-review baseline: `main` at `198b36f9e2753ccfb68354a02480cdc25e7eabc7`; this audit branch contains that commit.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source and contract review; independent second review NOT RUN.
- Status: REVIEWED WITH FINDINGS (1 P1, 1 P2; source inspection only).
- Files inspected: `RabbitMqQueueDriver.cs`, provider project file, `IQueueDriver`, shared queue contract in `tests/Naravel.Queue.Tests/DriverTests.cs`, `RabbitMqQueueTypeTests`, RabbitMQ section in `docs/en/queue.md` and NuGet README..
- Dependencies: `Naravel.Queue` and `RabbitMQ.Client`; optional provider package..
- Verification performed: source-to-contract tracing and inspection of the listed tests and documentation.
- Verification NOT RUN: .NET restore/build/test, live provider or service execution unless explicitly stated below, full CI and mutation testing.

## Executive assessment

The provider stores ready jobs, delayed jobs and failed jobs in separate durable queues. It keeps one channel for each in-flight delivery. It supports classic and quorum ready queues. Delays use per-message expiry and dead-lettering; the guide warns that a long-delay message can hold back shorter delays. Two contract gaps remain: acknowledging before publishing a retry can lose the only copy after a crash, and the reported queue size ignores delayed jobs.

## Package and dependency map

- Contract: `IQueueDriver`; implementation: `RabbitMqQueueDriver`.
- Registration: `AddRabbitMqDriver`.
- Topology: `netqueue.ready.*`, `netqueue.delay.*`, `netqueue.failed.*` and `netqueue.direct`.
- Ownership: one shared RabbitMQ connection plus one channel per active delivery; the driver implements `IAsyncDisposable`.

## Findings


### [P1] RQM-001 — Retry can lose the message between acknowledgement and republish

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; runtime reproduction NOT RUN.
- Exact source evidence: `RabbitMqQueueDriver.ReleaseAsync` calls `BasicAckAsync` on the original delivery, disposes its channel, then calls `PushAsync` for the retry. `FailAsync` also acknowledges before publishing to the failed queue.
- Affected API/configuration: `IQueueDriver.ReleaseAsync` and `IQueueDriver.FailAsync`.
- User impact: A failed job can disappear if the process stops after RabbitMQ accepts the acknowledgement but before it stores the replacement.
- Trigger/example: The worker retries a failed job. The broker acknowledges removal of the old message. The application process stops before the new copy is published.
- Why this proves the concern: These are two separate awaited broker operations, and the first completes before the second starts. No source code ties them into one atomic operation.
- Existing test gap: `RabbitMqQueueProviderTests.Shared_contract` checks a successful full call. It does not interrupt the process between the acknowledgement and the publish.
- Required safe outcome: Confirm that RabbitMQ has durably accepted the retry or failed copy before acknowledging the original. A duplicate after a crash is safer than losing the only copy.
- Regression tests required: Inject a failure before replacement publish and prove the original remains deliverable; inject a crash after confirmed publish but before acknowledgement and prove at least one copy remains; cover both retry and failed transfer.
- Owner decision: Owner approval should be obtained before implementing the durability change.

#### Plain-language explanation

**In everyday words:** The provider tells RabbitMQ that the old message is finished before it has made sure the replacement exists. A crash in between can remove the only copy.

**A realistic example:** A job fails. The worker acknowledges the old message, then the process is killed before the retry is stored. RabbitMQ no longer has a message to deliver.

**What we checked:** `RabbitMqQueueDriver.ReleaseAsync` calls `BasicAckAsync` on the original delivery, disposes its channel, then calls `PushAsync` for the retry. `FailAsync` also acknowledges before publishing to the failed queue.

**Why that proves the concern:** These are two separate awaited broker operations, and the first completes before the second starts. No source code ties them into one atomic operation.

**What is not yet proven:** the review inspected source and existing test code only. The behavior was not reproduced by running a test or a live service in this review.

**What should change:** Confirm that RabbitMQ has durably accepted the retry or failed copy before acknowledging the original. A duplicate after a crash is safer than losing the only copy.

**How we will prove the fix:** Inject a failure before replacement publish and prove the original remains deliverable; inject a crash after confirmed publish but before acknowledgement and prove at least one copy remains; cover both retry and failed transfer.


### [P2] RQM-002 — SizeAsync ignores jobs waiting in the delay queue

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; runtime reproduction NOT RUN.
- Exact source evidence: `IQueueDriver.SizeAsync` describes pending count as ready plus delayed; `RabbitMqQueueDriver.SizeAsync` reads only `netqueue.ready.{queue}` and never reads `netqueue.delay.{queue}`.
- Affected API/configuration: `RabbitMqQueueDriver.SizeAsync`.
- User impact: Dashboards or controls can report an empty or smaller queue while delayed jobs are still waiting.
- Trigger/example: One ready job and one five-minute delayed job exist. The current method reports one, not two.
- Why this proves the concern: The delayed messages are stored in a separate queue in the provider's own topology, and the size method does not query it.
- Existing test gap: The shared contract tests size for an immediate message and after acknowledgement, but not while a delayed message exists.
- Required safe outcome: Include the delay queue count in the approximate pending size, or obtain owner approval to narrow the shared interface contract.
- Regression tests required: Check size with one ready plus one delayed job, after acknowledging ready, after delay expiry and after delivery.
- Owner decision: Not required to record this finding; required before changing runtime behavior.

#### Plain-language explanation

**In everyday words:** The size method looks only at jobs that can run now. It ignores jobs RabbitMQ is holding until their delay ends.

**A realistic example:** If one job is ready and one is scheduled for later, size reports one even though two jobs are pending.

**What we checked:** `IQueueDriver.SizeAsync` describes pending count as ready plus delayed; `RabbitMqQueueDriver.SizeAsync` reads only `netqueue.ready.{queue}` and never reads `netqueue.delay.{queue}`.

**Why that proves the concern:** The delayed messages are stored in a separate queue in the provider's own topology, and the size method does not query it.

**What is not yet proven:** the review inspected source and existing test code only. The behavior was not reproduced by running a test or a live service in this review.

**What should change:** Include the delay queue count in the approximate pending size, or obtain owner approval to narrow the shared interface contract.

**How we will prove the fix:** Check size with one ready plus one delayed job, after acknowledging ready, after delay expiry and after delivery.


## Test assessment

- Existing tests inspected: `QueueDriverContractTests` (found in `tests/Naravel.Queue.Tests/DriverTests.cs`), `RabbitMqQueueTypeTests`, and `RabbitMqQueueProviderTests.Shared_contract` in `tests/Naravel.Queue.Providers.Tests/QueueProviderContractTests.cs`..
- Coverage seen in source: normal acknowledgement, release, failure, queue isolation, priority, concurrent consumers, queue declaration arguments, classic/quorum selection and configuration..
- Gaps: no crash/fault injection between acknowledgement and replacement publish; no size assertion while a delayed message remains pending.
- Tests actually run for this review: NONE.
- Build/restore: NOT RUN.
- External service/provider integration: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Documentation and packaging assessment

The guide describes delayed-message head-of-line behavior and distinguishes worker retries from crash redelivery. The current source's acknowledgement-before-republish ordering is not neutralized by that documentation. No package archive was built.

## Risk summary and next action

- P1: RQM-001.
- P2: RQM-002.
- P3 / OBS / DOC: none recorded.

Recommended next task: confirm replacement publication before acknowledging the original, and count delayed messages in `SizeAsync`. Do not change runtime code in this audit without owner approval.

## Plain-language explanation

This package connects queue jobs to RabbitMQ. It uses separate broker queues for ready, delayed and failed jobs. The source and tests were inspected, but no build, automated test or live broker check was run in this review.
