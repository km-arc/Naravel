# PDR-012a — Kafka delayed delivery and retry backoff (R07.T11, D-29)

**Native-first verdict:** Kafka has no per-message delay and no per-message acknowledgement: a consumer group keeps one committed offset per partition. Nothing in Confluent.Kafka or the .NET runtime changes that, so "native" gives no way to delay one record without also holding back every record behind it. Naravel must therefore either build delay on top of extra topics or stop pretending Kafka can delay.

Status: **Proposed, awaiting owner approval** (drafted 2026-10-13). No code may be written for R07.T11 before approval (AGENTS.md hard rule 1).

## Context

`KafkaQueueDriver` (`src/Naravel.Queue.Kafka/KafkaQueueDriver.cs`) has two paths that create a future `AvailableAt`:

1. **Delayed dispatch.** `PushAsync` writes the record to the tail of `netqueue-<queue>`. When `PopAsync` later reads a record whose `AvailableAt` is in the future, `BufferIfReady` returns false and the driver seeks back to that offset and returns `null`.
2. **Retry backoff.** The worker calls `ReleaseAsync(message, ComputeBackoff(...))` (`QueueWorkerService.cs:346`). The driver re-produces the message to the tail with `AvailableAt = now + backoff` and commits the original offset.

Because a partition is read strictly in offset order and only one offset can be committed, a not-yet-due record at the head stops everything behind it. With the default backoff of 5–60 s one retrying job stalls a single-partition queue for that long. Characterization tests pin this today: `Delayed_record_at_the_partition_head_blocks_ready_records_behind_it_until_due` and `A_retry_backoff_blocks_records_produced_after_it_until_the_backoff_elapses` (`NARAVEL_TEST_KAFKA`). The current class summary and `docs/*/queue.md` mention only "a long delay blocks head-of-line", which understates the retry case.

Laravel surface: `delay()`, `Job::$backoff`, `release($delay)`. Every other Naravel driver supports them.

## Options

**(a) Companion delay topics.** Move delayed and backed-off records to separate topics and re-publish them to the main topic when due. A single delay topic only moves the problem (its head blocks the same way). It works if delays are rounded **up to fixed tiers** (for example 5 s, 30 s, 5 min, 30 min), one topic per tier: within a tier every record has the same delay, so due times are monotonic and head-of-line blocking is exactly correct. A background loop per tier waits for the head to be due, then produces to the main topic and commits. Cost: more topics to provision, delay precision limited to the tiers, a forwarding loop that must itself be at-least-once (duplicates on crash), and a considerable amount of new concurrent code that cannot be validated without a live broker.

**(b) Pause/resume the partition and poll on a timer.** This removes the 200 ms seek-and-reread churn, but Kafka still cannot skip over a record to read the ones behind it without losing the single-offset commit model. It does **not** fix head-of-line blocking, only its CPU cost. Rejected as a fix for D-29; it may still be a cheap improvement under (a) or (c).

**(c) Make Kafka an immediate-delivery driver.** State plainly that the Kafka driver does not support delayed delivery, and fail fast instead of blocking:
- A delayed dispatch to a Kafka store is rejected with a clear exception at dispatch time (`PushAsync` with `AvailableAt` in the future), not accepted and silently stalled.
- A worker retry on Kafka is re-produced with no delay. Backoff is **not applied** and this is documented; a job that needs spaced retries should use a store whose driver supports delay.
- Drivers declare the capability through an optional interface (name subject to API review, `PublicApiAnalyzers` applies), so the worker logs one warning when a job with a non-empty `Backoff` runs on a store without delay support.

## Proposed decision

Adopt **(c) now** and keep **(a) as a backlog item** to be revisited after the first preview release and only if users ask for delayed Kafka jobs.

Reasons: (c) removes the only way a user can hit a hidden multi-second stall, is small enough to verify with the existing characterization tests, and needs no new topics or background loops. (a) is a legitimate design but is the largest unverifiable piece of code in the queue, for a driver that is rarely the right choice for delayed work. For delayed or spaced-retry workloads the documented answer is Redis, Database or RabbitMQ.

### Behavior after the change

| Case | Today | After (c) |
|---|---|---|
| `DispatchAsync(job, delay)` on Kafka | accepted; blocks head-of-line until due | rejected with a clear error naming the store and driver |
| Retry with `Backoff` on Kafka | re-produced with future `AvailableAt`; blocks the queue | re-produced immediately; backoff ignored; warning logged once per job type |
| Records already in a topic with a future `AvailableAt` (written by an older version) | blocks head-of-line | still honored for compatibility (the existing seek path stays), documented as legacy |

Keeping the old read path avoids stranding records already produced by earlier previews. This is a pre-1.0 breaking change and goes in `CHANGELOG.md` (OD-09).

### Quickstart impact (S1)

None for the common case. Kafka stores keep their three-line registration; only delayed dispatch changes.

## Related decisions that still need the owner (not decided here)

- **D-21 blocking `Consume`.** `Confluent.Kafka` has no asynchronous `Consume`. The fix is to run the poll on a dedicated long-running task per queue and hand records over through a bounded `Channel<T>`, which also lets `PopAsync` honor `CancellationToken` promptly. Recommended as its own small PDR after this one; independent of the delay decision.
- **D-27 RabbitMQ ready queue type.** A classic queue cannot use `delivery-limit`. Recommended: add an opt-in store setting for the queue type, default unchanged (classic), documenting that an existing queue must be deleted and recreated because RabbitMQ fixes the type at declaration. The owner decides whether the default should ever change.

## Alternatives considered

1. **Leave as is and document harder.** Rejected: the retry case hides a stall behind the default backoff.
2. **Single delay topic.** Rejected: same head-of-line blocking one level down.
3. **Delay inside the worker (sleep before processing).** Rejected: holds a worker slot and an uncommitted offset, and loses the delay on restart.
4. **Remove the Kafka driver.** Rejected as out of scope; it remains valid for immediate, ordered, high-throughput work.

## Verification required after approval

Invert the two characterization tests: delayed dispatch to Kafka is rejected; a worker retry is delivered immediately and does not block later records. Add a test for the legacy-record read path and for the once-per-job-type warning. Update the Kafka class summary, `docs/en|fa/queue.md` (limitations and the attempt table), `CHANGELOG.md`, `roadmap/AUDIT.md` D-29, and tick R07.T11. Run the full verification and `roadmap/check.py`. External-service tests that were not run are reported as NOT RUN.

## Approval

Pending. Record the owner's decision and date here, then proceed under R07.T11.
