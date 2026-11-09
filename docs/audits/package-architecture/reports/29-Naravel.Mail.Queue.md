# Review Report — Naravel.Mail.Queue

## Review metadata

- Package ID: Naravel.Mail.Queue.
- Source project: src/Naravel.Mail.Queue/Naravel.Mail.Queue.csproj.
- Baseline: main at d3f4342db8aa7d0f8085d1205f76f6ae5bfa8ba3.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source, serialization and test review; independent fresh-session review NOT RUN.
- Status: REVIEWED — NO SOURCE-LEVEL FINDINGS (runtime verification pending).
- Files inspected: MailQueueJsonContext, MailQueueOptions, MailQueueServiceCollectionExtensions, QueuedMailDispatcher, SendQueuedMailJob, project references, queue-mail tests, README and Mail guide.
- Dependencies: Naravel.Mail and Naravel.Queue; no separate broker client dependency.
- Verification performed: queued message shape, source-generated JSON metadata, transport selection, queue placement, cancellation and retry contract reviewed.
- Verification NOT RUN: restore, build, tests, live queue providers, full CI, mutation testing and independent review.

## Executive assessment

The adapter converts a fully built MailMessage into a queue job with plain fields. It does not serialize a Mailable object, runtime type name or delegate. The worker rebuilds a MailMessage (validating its addresses again) and sends it through the selected mailer.

Delivery is at-least-once. If SMTP accepts the message but the worker stops before acknowledging the queue job, a later worker can send the same message again. This is explicitly documented as a queue delivery guarantee, not a hidden transport retry.

No source-level correctness or architecture finding was identified in this pass.

## Package and dependency map

- Queue adapter: QueuedMailDispatcher implements IMailQueue.
- Job: SendQueuedMailJob stores sender, To, Cc, Bcc, subject, text/HTML body and optional mailer name.
- JSON: MailQueueJsonContext provides source-generated metadata for the job.
- Registration: MailQueueServiceCollectionExtensions connects the adapter to IMailQueue.
- Project references: Naravel.Mail and Naravel.Queue.

## Findings

No source-level finding was established in the inspected adapter.

## Existing guarantees and coverage

- QueueAsync builds the Mailable into a MailMessage before enqueueing.
- The queued job contains plain message data rather than a CLR type name or executable delegate.
- The message is reconstructed and address validation is applied again before sending.
- The optional mailer name is retained so the worker can select the intended transport.
- Queue connection and queue name can be configured through MailQueueOptions.
- Duplicate delivery after an ambiguous worker failure is documented as at-least-once behavior.

## Test assessment

- Tests inspected: MailJobMetadataTests and MailQueueTests.
- Tests actually run in this review: NONE.
- Live queue providers, build/CI, mutation testing and independent review: NOT RUN.

## Documentation and packaging assessment

- Package README explains the queue adapter and how to configure its connection and queue.
- It documents that the job stores message data rather than a Mailable object.
- Duplicate delivery remains covered by the Queue guide's general at-least-once contract.
- No queue-backed sample was executed.

## Risk summary and next action

- P0/P1/P2/P3/OBS/DOC: none established by source inspection.
- Recommended next task: retain the plain-data job format and source-generated metadata path; run the provider-backed queue tests before release.

## Plain-language explanation

This adapter lets an email be sent later by a queue worker. It builds the email first and stores only its addresses, subject and body, not the original C# object that created it. If the mail server accepts the email but the worker stops before the queue records success, another worker may send it again. That duplicate possibility is part of the documented at-least-once queue behavior. Tests were inspected but not run.
