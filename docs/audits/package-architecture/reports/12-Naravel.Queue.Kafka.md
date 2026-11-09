# Review Report — Naravel.Queue.Kafka

## Review metadata

- Package ID: `Naravel.Queue.Kafka`.
- Source project: `src/Naravel.Queue.Kafka/Naravel.Queue.Kafka.csproj`.
- Source-review baseline: `main` at `198b36f9e2753ccfb68354a02480cdc25e7eabc7`; this audit branch contains that commit.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source and contract review; independent second review NOT RUN.
- Status: REVIEWED WITH FINDINGS (1 P2; source inspection only).
- Files inspected: `KafkaQueueDriver.cs`, `KafkaOffsetCommitTracker`, `IQueueDriver`, Queue worker polling/stop logic, `KafkaDelayedDeliveryTests`, `KafkaOffsetCommitTrackerTests`, `KafkaQueueProviderTests` in `QueueProviderContractTests.cs`, Queue guide and NuGet README..
- Dependencies: `Naravel.Queue` and `Confluent.Kafka`..
- Verification performed: source-to-contract tracing and inspection of the listed tests and documentation.
- Verification NOT RUN: .NET restore/build/test, live provider or service execution unless explicitly stated below, full CI and mutation testing.

## Executive assessment

The provider treats Kafka as a log, not a classic queue. It documents immediate retry, no delayed dispatch, per-queue consumer groups, tail-of-topic retries, `SizeAsync=-1` and at-least-once crash redelivery. The offset tracker waits for earlier deliveries before committing later acknowledgements. One error path reports a failed poll as an empty queue.

## Package and dependency map

- Implementation: `KafkaQueueDriver : IQueueDriver, IDisposable`.
- Topics: `netqueue-{queue}` and `netqueue-{queue}-failed`.
- One consumer state and consumer group per logical queue.
- Retries are published to the topic tail before committing the old offset.

## Findings


### [P2] KFK-001 — A broker read error is reported as an empty queue

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; runtime reproduction NOT RUN.
- Exact source evidence: `KafkaQueueDriver.PopAsync` catches `ConsumeException` around its first `PollAsync` and returns `null`; the later drain catches the same exception and stops draining. The Queue worker treats `null` as an empty poll; with `StopWhenEmpty` it exits.
- Affected API/configuration: `KafkaQueueDriver.PopAsync` and `QueueWorkerOptions.StopWhenEmpty`.
- User impact: A broker or topic error can quietly stop a worker that was configured to stop only when there are no jobs. Without `StopWhenEmpty`, it still hides the reason no job was returned.
- Trigger/example: Kafka throws `ConsumeException` during poll. The provider returns `null`, indistinguishable from a healthy empty poll.
- Why this proves the concern: The provider converts an error into the same return value used for a valid empty queue. The worker only logs errors that escape the driver as exceptions.
- Existing test gap: Provider tests cover healthy brokers and cancellation, not a `ConsumeException` injection or worker reaction.
- Required safe outcome: Propagate a provider failure or return a distinct unavailable result. Only a successful poll with no record should mean empty.
- Regression tests required: Inject a poll error with `StopWhenEmpty=true` and prove the worker stays alive or reports a failure; prove a genuinely empty queue still stops; keep cancellation distinct.
- Owner decision: Not required to record this finding; required before changing runtime behavior.

#### Plain-language explanation

**In everyday words:** The provider says “there are no jobs” when it really means “Kafka could not answer.”

**A realistic example:** The worker polls during a broker interruption. It receives `null` and exits as if the queue had drained, even though the broker may recover later.

**What we checked:** `KafkaQueueDriver.PopAsync` catches `ConsumeException` around its first `PollAsync` and returns `null`; the later drain catches the same exception and stops draining. The Queue worker treats `null` as an empty poll; with `StopWhenEmpty` it exits.

**Why that proves the concern:** The provider converts an error into the same return value used for a valid empty queue. The worker only logs errors that escape the driver as exceptions.

**What is not yet proven:** the review inspected source and existing test code only. The behavior was not reproduced by running a test or a live service in this review.

**What should change:** Propagate a provider failure or return a distinct unavailable result. Only a successful poll with no record should mean empty.

**How we will prove the fix:** Inject a poll error with `StopWhenEmpty=true` and prove the worker stays alive or reports a failure; prove a genuinely empty queue still stops; keep cancellation distinct.


## Test assessment

- Existing tests inspected: `KafkaDelayedDeliveryTests`; `KafkaOffsetCommitTrackerTests`; `KafkaQueueProviderTests.Shared_contract`, `Pop_on_an_empty_queue_honours_cancellation_without_waiting_for_the_poll_window`, `Idle_consumer_of_another_queue_does_not_stall_a_new_queue`, retry-immediate and legacy delayed-record tests..
- Coverage seen in source: delayed dispatch rejection, immediate retry, offset ordering, cancellation, queue isolation, consumer startup, batch priority and old-envelope compatibility..
- Gaps: no injected `ConsumeException` or worker test that checks broker failure with `StopWhenEmpty=true`.
- Tests actually run for this review: NONE.
- Build/restore: NOT RUN.
- External service/provider integration: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Documentation and packaging assessment

The Queue guide accurately explains Kafka-specific limits. `SizeAsync=-1` is allowed by the shared contract and is documented; it is not a finding.

## Risk summary and next action

- P2: KFK-001.
- P0 / P1 / P3 / OBS / DOC: none recorded.

Recommended next task: preserve the distinction between an empty topic and a failed poll, and add a worker-level error test. Do not change runtime code in this audit without owner approval.

## Plain-language explanation

This provider stores jobs in Kafka topics. It intentionally does not support delayed dispatch and retries go to the end of the topic. The code and tests were inspected, but no build, automated test or live broker check was run.
