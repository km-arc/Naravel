# Review Report — Naravel.Queue.RabbitMQ

## Review metadata

- Package ID: Naravel.Queue.RabbitMQ.
- Source project: src/Naravel.Queue.RabbitMQ/Naravel.Queue.RabbitMQ.csproj.
- Baseline: main at 282a507bead92ae9ca40a9cb060baa2ee1dd7287.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source, test and package-contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection and external RabbitMQ contract check; broker runtime verification pending).
- Files inspected: driver, registration, project/package references, RabbitMQ provider tests, shared Queue contract, Queue core IQueueDriver contract, Queue guide and package README.
- Direct dependency: RabbitMQ.Client 7.2.2; project reference: Naravel.Queue.
- Verification performed: settlement ordering, message persistence properties, durable topology, TTL/dead-letter flow, configuration parsing, queue-size semantics, test coverage and package boundary reviewed.
- Verification NOT RUN: restore, build, unit tests, live RabbitMQ provider contract, broker restart/durability test, publisher-confirm failure injection, mutation testing and independent review.

## Executive assessment

The driver uses a clear topology: ready, delay and failed queues are separate; delayed messages return through RabbitMQ dead-lettering; ready messages use manual acknowledgement; and each outstanding delivery keeps the channel that received it.

The main correctness risk is message loss during retry or permanent-failure handoff. The driver acknowledges the old delivery before it publishes the replacement. If the new publish fails after the acknowledgement succeeds, RabbitMQ no longer has the old message and the replacement was not stored. The failed-message path has the same ordering. There are also two contract gaps: failed messages are published without the persistent flag used for normal messages, and SizeAsync counts only ready messages even though the shared contract says ready plus delayed.

## Package and dependency map

- Public implementation: RabbitMqQueueDriver; public queue type: RabbitMqQueueType (Classic or Quorum).
- Registration: AddRabbitMqDriver reads each store's host, port, credentials, virtual host, queue type and optional delivery limit.
- Topology: netqueue.ready.{queue}, netqueue.delay.{queue}, netqueue.failed.{queue}, and the shared direct exchange.
- Resource ownership: the driver owns one shared connection and one channel per in-flight delivery; DisposeAsync disposes outstanding channels and the connection.
- Dependency boundary: the optional adapter depends on Queue and RabbitMQ.Client; Queue core does not depend on RabbitMQ.

## Findings

### [P1] RQM-001 — Release and fail acknowledge the original message before the replacement is published

- Remediation state: OPEN.
- Architecture disposition: proposed veto AV-020 — an original queue message must not be acknowledged until its replacement or failed copy has been safely accepted by the broker.
- Evidence status: SEEN IN SOURCE; deterministic broker-failure reproduction NOT RUN.
- Evidence: src/Naravel.Queue.RabbitMQ/RabbitMqQueueDriver.cs, methods ReleaseAsync and FailAsync. Both remove the delivery from _deliveries, call BasicAckAsync and dispose that channel before calling PushAsync or publishing to the failed queue.
- Affected contract: IQueueDriver.ReleaseAsync and IQueueDriver.FailAsync.
- In everyday words: The driver throws away the broker's only copy first, then tries to save the new copy. If saving the new copy fails, the job is gone.
- Realistic example: A job fails once. RabbitMQ confirms the acknowledgement of the old delivery. The connection then fails while the driver tries to publish the retry. The original is already acknowledged and the retry was never accepted.
- Why the evidence proves the risk: the two operations are separate and the acknowledgement comes first. There is no transaction or recovery step that restores the acknowledged message when the later publish throws.
- What is not proven: the source ordering is confirmed; no live broker fault was injected between the acknowledgement and publish. This report does not claim that every publish fails or that a normal run loses messages.
- Required safe outcome: publish the replacement first and wait for the broker's acceptance confirmation before acknowledging the old delivery. If the acknowledgement then fails, a duplicate is safer than losing the only copy; the Queue contract already has at-least-once behavior.
- Regression tests required: pause or reject the replacement publish after a successful old delivery; prove the old message remains available. Then prove successful publish followed by failed acknowledgement may produce a duplicate but never zero copies. Cover both retry and permanent-failure handoff, including broker restart between the steps.
- Public API / PDR / release impact: reliability and at-least-once delivery guarantee. No public API change is necessarily required, but publisher-confirm behavior and duplicate handling must be explicit.

### [P2] RQM-002 — Failed messages are published as transient messages to a durable queue

- Remediation state: OPEN.
- Evidence status: SEEN IN SOURCE and CHECKED AGAINST RABBITMQ DOCUMENTATION; broker-restart reproduction NOT RUN.
- Evidence: FailAsync in src/Naravel.Queue.RabbitMQ/RabbitMqQueueDriver.cs publishes with new BasicProperties() and does not set DeliveryMode = DeliveryModes.Persistent. In the same file, normal PushAsync explicitly sets the persistent delivery mode. EnsureTopologyAsync declares the failed queue as durable.
- In everyday words: The failed queue is configured to survive a broker restart, but the messages sent to it are not marked to survive that restart.
- Realistic example: A job permanently fails and is written to netqueue.failed.orders. RabbitMQ restarts. The queue still exists, but this transient message can be discarded during recovery.
- Why the evidence proves the risk: RabbitMQ's durability rules require both a durable queue and persistent messages. Its documentation says transient messages are discarded during recovery even when they were stored in durable queues: https://www.rabbitmq.com/docs/queues.
- What is not proven: this is a source/API-contract conclusion supported by RabbitMQ documentation; no real broker restart was performed in this audit.
- Required safe outcome: failed-message copies must use persistent delivery properties whenever the normal queue path promises durable message storage.
- Regression tests required: fail a job, inspect the failed message's delivery mode, restart a real RabbitMQ broker and prove the failed message is still present. Keep the normal ready/retry durability checks.
- Public API / PDR / release impact: provider reliability behavior; no public API change expected.

### [P2] RQM-003 — SizeAsync omits messages waiting in the delay queue

- Remediation state: OPEN.
- Evidence status: SEEN IN SOURCE; delayed-size provider test NOT RUN.
- Evidence: SizeAsync in RabbitMqQueueDriver.cs passively reads only netqueue.ready.{queue}. PushAsync routes future messages to netqueue.delay.{queue}. The shared IQueueDriver.SizeAsync contract in src/Naravel.Queue/Drivers/IQueueDriver.cs defines the value as approximate pending work: ready plus delayed.
- In everyday words: A job waiting for its scheduled time is still waiting in the queue, but the reported size ignores it.
- Realistic example: A queue has no ready messages and 20 messages scheduled for later. SizeAsync reports zero.
- Why the evidence proves the mismatch: delayed work is stored in a different queue, and the method returns only the ready queue's message count.
- What is not proven: the code path is confirmed; no live RabbitMQ delayed-size check was run.
- Required safe outcome: include the delay queue count, or explicitly return an unavailable marker if the provider cannot calculate the shared meaning.
- Regression tests required: count ready-only, delay-only and mixed ready/delayed messages; verify the reported approximate size includes both queues.
- Public API / PDR / release impact: implementation correction to the existing IQueueDriver contract.

### [P3] RQM-004 — A misspelled DeliveryLimit silently disables the broker delivery limit

- Remediation state: OPEN.
- Evidence status: SEEN IN SOURCE; invalid-configuration test NOT RUN.
- Evidence: AddRabbitMqDriver in RabbitMqQueueDriver.cs uses int.TryParse(store["DeliveryLimit"], out var parsed) ? parsed : null. A non-empty invalid value is therefore treated as if the optional setting was absent. The constructor validates zero/negative values only when a number was successfully parsed.
- In everyday words: An operator can type a delivery limit incorrectly and the application quietly runs without that limit.
- Realistic example: DeliveryLimit: "five" becomes null, so the quorum queue is declared without an explicit delivery limit.
- Why the evidence proves the risk: the parsing branch maps malformed text to the same value as an omitted option.
- What is not proven: this is source-level config behavior; no startup test with malformed text was run.
- Required safe outcome: an omitted or blank value may keep the broker default; a non-empty invalid or overflowing value must fail configuration with the store and setting named.
- Regression tests required: omitted value, valid positive value, zero, negative, malformed text and overflow; verify invalid values never silently disable the setting.
- Public API / PDR / release impact: fail-fast configuration validation; no public API change.

## Test assessment

- Tests inspected: RabbitMqQueueTypeTests (classic/quorum arguments, delivery-limit validation and registration); RabbitMQ's environment-gated Shared_contract in QueueProviderContractTests.cs.
- Missing tests: publish failure between ack and replacement, failed-message persistence after broker restart, SizeAsync with delayed messages, malformed DeliveryLimit.
- Tests actually run in this review: NONE.
- Live RabbitMQ, build, mutation testing and independent review: NOT RUN.

## Documentation and packaging assessment

- Package README shows connection settings, classic/quorum selection, password handling and the need to drain/delete an existing queue before changing queue type.
- README and class comments explain delayed-message TTL/dead-letter behavior and the head-of-line delay caveat.
- The package page does not explain the broker-restart persistence distinction for failed messages or the size count's delayed-message limitation.

## Risk summary and next action

- P1: RQM-001 (message loss can occur during retry/fail handoff).
- P2: RQM-002 (failed copy can disappear after broker restart); RQM-003 (queue size undercounts delayed work).
- P3: RQM-004 (invalid configuration silently disables the delivery limit).
- Recommended next task: owner approval for a settlement/durability correction and deterministic RabbitMQ integration tests. No runtime change is authorized by this audit.

## Plain-language explanation

The main issue is about the order of two actions: the driver acknowledges the old job before it has safely stored the new retry or failed copy. A temporary broker error in that gap can lose the job. The other findings concern what survives a broker restart, what SizeAsync counts, and whether a configuration typo is visible. All are source-level findings; no RabbitMQ broker tests were run in this audit pass.
