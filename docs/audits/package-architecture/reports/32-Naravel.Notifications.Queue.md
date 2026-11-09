# Review Report — Naravel.Notifications.Queue

## Review metadata

- Package ID: Naravel.Notifications.Queue.
- Source project: src/Naravel.Notifications.Queue/Naravel.Notifications.Queue.csproj.
- Baseline: main at d3f4342db8aa7d0f8085d1205f76f6ae5bfa8ba3.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source, serialization and test review; independent fresh-session review NOT RUN.
- Status: REVIEWED — NO SOURCE-LEVEL FINDINGS (runtime verification pending).
- Files inspected: INotificationQueue, NotificationQueueJsonContext, NotificationQueueOptions, registration, QueuedNotificationDispatcher, QueuedNotificationRegistry, SendQueuedNotificationJob, queue tests, README and Notifications guide.
- Dependencies: Naravel.Notifications and Naravel.Queue.
- Verification performed: alias registration, duplicate/unknown alias rejection, payload shape, generated metadata, dispatch routing, worker execution and cancellation reviewed.
- Verification NOT RUN: restore, build, adapter tests, live queue providers, full CI, mutation testing and independent review.

## Executive assessment

The adapter queues notifications by storing a registered channel alias and a plain serialized payload. It does not put an arbitrary CLR type name or executable delegate in the queue message. The worker resolves the alias from the registered notification/channel list and delivers through the core notification sender.

Queue delivery is at-least-once, so a notification may be delivered more than once if a worker stops after sending but before acknowledging the job. This follows the underlying Queue contract and is not a hidden second retry system.

No source-level correctness or architecture finding was identified in this pass.

## Package and dependency map

- Dispatch: QueuedNotificationDispatcher.
- Worker job: SendQueuedNotificationJob.
- Registration/allow-list: QueuedNotificationRegistry and NotificationQueueServiceCollectionExtensions.
- Serialization: NotificationQueueJsonContext generated metadata.
- Options: NotificationQueueOptions controls queue placement.
- Dependencies: core Notifications and Queue; no transport client dependency.

## Findings

No source-level finding was established in the inspected adapter.

## Existing guarantees and coverage

- Registration requires an explicit notification type/channel alias.
- Duplicate aliases and duplicate notification/channel registrations are rejected.
- Unknown aliases fail visibly at worker execution.
- The queued payload contains plain data and generated JSON metadata; it does not use runtime type loading from the payload.
- The adapter passes the queued notification to the normal NotificationSender, preserving channel ordering and partial-delivery errors.
- At-least-once delivery and possible duplicates after worker interruption are inherited from Queue.

## Test assessment

- Tests inspected: NotificationJobMetadataTests, QueuedNotificationTests and QueuedNotificationTypeInfoTests.
- Tests actually run in this review: NONE.
- Build/CI, live provider tests, mutation testing and independent review: NOT RUN.

## Documentation and packaging assessment

- The package README documents aliases and the plain payload approach.
- The Notifications guide describes queued delivery as deferred work handled by Queue.
- No package example or broker-backed test was executed.

## Risk summary and next action

- P0/P1/P2/P3/OBS/DOC: none established by source inspection.
- Recommended next task: preserve the alias allow-list and generated JSON path; run the adapter tests with the supported Queue providers.

## Plain-language explanation

This adapter lets a notification be delivered later by a queue worker. The queue stores a short registered name and the notification data, not an arbitrary C# type name. The worker looks up that name in the list of allowed registrations and then uses the normal notification sender. A worker interruption after delivery may cause a duplicate, as with other queue jobs. The tests were inspected but not run.
