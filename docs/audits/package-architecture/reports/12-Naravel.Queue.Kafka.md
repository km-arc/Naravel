# Review Report — Naravel.Queue.Kafka

## Review metadata

- Package ID: Naravel.Queue.Kafka.
- Source project: src/Naravel.Queue.Kafka/Naravel.Queue.Kafka.csproj.
- Baseline: main at 282a507bead92ae9ca40a9cb060baa2ee1dd7287.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source, test and package-contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; live Kafka verification pending).
- Files inspected: Kafka driver and offset tracker; project/package references; Kafka-specific tests; Queue provider contract; Queue core interfaces; Kafka package README and Queue guide.
- Direct dependency: Confluent.Kafka 2.16.0; project reference: Naravel.Queue.
- Verification performed: consumer creation, consumer-group naming, polling and error handling, delayed-message rejection, retry/failed-topic behavior, acknowledgement ordering, offset tracker and size semantics inspected.
- Verification NOT RUN: restore, build, provider tests, live Kafka broker tests, rebalance/failure injection, mutation testing and independent review.

## Executive assessment

Kafka is a log, not a traditional queue, and the adapter documents the important differences: each queue has its own consumer group, delayed delivery is rejected, retries are re-produced immediately, SizeAsync returns -1, and offsets are committed only after settlement. The offset tracker has targeted tests for out-of-order acknowledgement and gaps.

Two source-level risks remain. First, a consumer creation callback is passed to ConcurrentDictionary.GetOrAdd; concurrent callers may execute that callback more than once. The consumer that loses the dictionary race is not disposed. Second, an initial broker ConsumeException is converted into null, which is the same result as “no message arrived.” A broker failure can therefore look like an empty queue to the worker.

## Package and dependency map

- Public implementation: KafkaQueueDriver, implementing IQueueDriver and IDisposable.
- Registration: AddKafkaDriver requires BootstrapServers and defaults GroupId to netqueue-workers.
- Queue mapping: netqueue-{queue} for jobs and netqueue-{queue}-failed for dead letters. Consumer group is {GroupId}-{queue}.
- Resource ownership: one producer per driver and one consumer state per queue; each consumer has a gate, offset tracker and in-memory buffer.
- Dependency boundary: the adapter depends on Queue and Confluent.Kafka; Queue core remains independent of Kafka.

## Findings

### [P2] KFK-001 — Concurrent first use can create extra Kafka consumers that are never disposed

- Remediation state: OPEN.
- Evidence status: SEEN IN SOURCE and CHECKED AGAINST .NET CONCURRENT-DICTIONARY CONTRACT; concurrent broker reproduction NOT RUN.
- Evidence: GetConsumer in src/Naravel.Queue.Kafka/KafkaQueueDriver.cs passes a factory to _consumers.GetOrAdd(queue, ...). Inside the factory it builds a consumer and calls Subscribe; the returned consumer is wrapped in KafkaConsumerState. Only the winning state is retained. No code disposes a state created by a factory call that loses the dictionary race.
- In everyday words: Two requests can both try to create the first consumer for the same queue. One is saved; the other can be left running in the background with no owner to dispose it.
- Realistic example: Two workers call PopAsync("orders") at the same time on a cold driver. Both factory calls may build and subscribe a Kafka consumer for the same group. Only one is stored in _consumers.
- Why the evidence proves the risk: GetOrAdd may invoke its value factory more than once under concurrent calls; only one created value is inserted. The losing consumer is not returned to the caller and is not in the dictionary that Dispose later walks.
- What is not proven: the code path and .NET concurrency contract establish the risk; this audit did not force two first-use factory calls against a live broker.
- Required safe outcome: one queue must have exactly one owned consumer, and every consumer created during a race must either become the owned instance or be disposed immediately.
- Regression tests required: use a gated consumer factory so two calls both reach creation before either returns; prove one consumer is retained and the other is disposed. Verify only one active subscription for the queue group and that driver disposal closes every created consumer.
- Public API / PDR / release impact: internal lifecycle correction; no public API change expected.

### [P2] KFK-002 — A broker consume error is reported as an empty queue

- Remediation state: OPEN.
- Evidence status: SEEN IN SOURCE; injected broker-error test NOT RUN.
- Evidence: PopAsync in src/Naravel.Queue.Kafka/KafkaQueueDriver.cs catches ConsumeException around the first PollAsync call and immediately returns null. The same method also returns null when polling completes without a record.
- In everyday words: When Kafka reports an error, the driver tells Queue that there was simply no message.
- Realistic example: Kafka is unavailable or the consumer cannot read its partition. PopAsync returns null; the worker treats the queue as idle and keeps polling instead of recording a backend failure.
- Why the evidence proves the risk: a broker exception is swallowed and mapped to the same return value used for an ordinary empty poll. No diagnostic or error state is returned from this catch.
- What is not proven: source inspection confirms the catch path; the audit did not inject a broker error or verify production logging under an outage.
- Required safe outcome: distinguish “no message arrived in the poll window” from “the broker failed to deliver a poll result.” Surface the broker error through the normal Queue worker error path, or report it through a bounded diagnostic while retaining any explicitly chosen retry behavior.
- Regression tests required: inject ConsumeException on the first poll and during buffer draining. The first must not be treated as a normal empty queue; the second must not silently hide a partial read failure. Confirm the worker logs/reports backend failure and later recovers after the broker returns.

## Existing guarantees and coverage

- Delayed messages are rejected before publish; the package README explains this limitation and the reason a delayed record would block later records in a partition.
- Worker-initiated retries are published immediately, and the tests verify that the retry does not hold back later records.
- The offset tracker does not commit beyond an earlier unacknowledged record; tests cover out-of-order acknowledgements and gaps.
- SizeAsync deliberately returns -1; the shared Queue interface allows this when exact size is unavailable.
- The provider tests include the shared broker contract, cancelled empty polling, idle consumer isolation, retry timing and compatibility with old future-dated records. Those tests require an external Kafka service.

## Test assessment

- Tests inspected: KafkaDelayedDeliveryTests, KafkaOffsetCommitTrackerTests, and KafkaQueueProviderTests in QueueProviderContractTests.cs.
- Missing tests: concurrent first-consumer creation and disposal; first-poll and buffer-drain ConsumeException handling.
- Tests actually run in this review: NONE.
- Live Kafka, build, mutation testing and independent review: NOT RUN.

## Documentation and packaging assessment

- The package README explains per-queue consumer groups, the required bootstrap servers and the no-delay behavior.
- It correctly says retries are immediate rather than applying the original backoff.
- The shared Queue guide explains Kafka's at-least-once crash redelivery and that SizeAsync is not available as a cheap count.
- The documentation does not currently explain that a consume exception is returned to the worker as an empty poll.

## Risk summary and next action

- P2: KFK-001 (extra consumer may be left active and unowned); KFK-002 (broker errors can be mistaken for an idle queue).
- Recommended next task: add the deterministic consumer-factory and consume-error tests, then correct ownership and error propagation. No runtime change is authorized by this audit.

## Plain-language explanation

Kafka stores messages in an ordered log. This adapter preserves that model and makes several differences from a normal queue explicit. The two open issues are about error handling and resource ownership: one error can look like “nothing to do,” and simultaneous first requests can create a consumer that the driver never remembers or closes. The broker-backed tests were inspected but not executed in this audit.
