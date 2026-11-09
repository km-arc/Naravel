# Review Report — Naravel.Mail.Smtp

## Review metadata

- Package ID: Naravel.Mail.Smtp.
- Source project: src/Naravel.Mail.Smtp/Naravel.Mail.Smtp.csproj.
- Baseline: main at d3f4342db8aa7d0f8085d1205f76f6ae5bfa8ba3.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source, dependency and test review; independent fresh-session review NOT RUN.
- Status: REVIEWED — NO SOURCE-LEVEL FINDINGS (runtime verification pending).
- Files inspected: SmtpMailOptions, SmtpMailTransport, SmtpServiceCollectionExtensions, project references, SMTP tests, fake SMTP server, README and Mail guide.
- Direct dependencies: MailKit and System.Security.Cryptography.Pkcs; project reference: Naravel.Mail.
- Verification performed: configuration validation, TLS modes, authentication, MIME conversion, cancellation checks, SMTP client ownership and failure/retry behavior reviewed.
- Verification NOT RUN: restore, build, SMTP tests, live SMTP server, TLS/authentication integration, full CI, mutation testing and independent review.

## Executive assessment

The SMTP adapter keeps MailKit out of the core Mail package. Each send creates and owns a new SMTP client, connects, optionally authenticates, sends the MIME message and disconnects. This matches the MailKit client thread-safety boundary.

The adapter surfaces send failures instead of automatically retrying. That is an important choice: after a network failure during the final send step, the server may already have accepted the message. Retrying invisibly at the transport layer could send a duplicate. When mail is queued, the Queue worker applies the configured job retry policy.

No source-level correctness or architecture finding was identified in this pass.

## Package and dependency map

- Public transport: SmtpMailTransport implements IMailTransport from Naravel.Mail.
- Settings: SmtpMailOptions validates host, port, security mode, timeout and optional credentials.
- Registration: SmtpServiceCollectionExtensions registers the SMTP transport from configuration/options.
- Network client: one MailKit SmtpClient is created and disposed for each send; no shared client is used concurrently.
- Project references: Naravel.Mail; third-party references are isolated to this adapter.

## Findings

No source-level finding was established in the inspected adapter.

## Existing guarantees and coverage

- A send checks cancellation before connecting and passes the token to connect, authenticate, send and disconnect.
- The transport validates options before use.
- TLS modes map explicitly to MailKit SecureSocketOptions.
- The MIME converter preserves To, Cc and Bcc as their corresponding recipient types.
- The adapter does not automatically retry an ambiguous SMTP send failure.
- The package tests use a fake SMTP server to inspect protocol behavior and message conversion.

## Test assessment

- Tests inspected: SmtpMailTransportTests and FakeSmtpServer.
- Tests actually run in this review: NONE.
- Live SMTP/TLS/authentication, build/CI, mutation testing and independent review: NOT RUN.

## Documentation and packaging assessment

- The README documents host, port, credentials, security mode and configuration.
- The README advises keeping passwords out of source configuration and using secrets/environment variables.
- It describes failures as visible to the caller and leaves queue retries to Naravel.Mail.Queue.
- No live SMTP sample was executed.

## Risk summary and next action

- P0/P1/P2/P3/OBS/DOC: none established by source inspection.
- Recommended next task: run the SMTP adapter tests and a configured integration test in CI; keep MailKit isolated from core Mail.

## Plain-language explanation

This adapter is the part that actually connects to an SMTP server and sends an email. It creates a separate connection for each send and closes it afterwards. If sending fails, it reports the failure rather than automatically trying again, because the server may already have accepted the message and a second attempt could send a duplicate. Tests and the fake SMTP server were inspected, but they were not run in this audit.
