# R09 — Mail

Parity section: `R09` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-013 (EN+FA) approved first.
Read: `src/Naravel.Queue/Dispatch/IJobDispatcher.cs`, `src/Naravel.Events/**` public surface, `docs/en/module-authoring-guide.md`.
Touch: new `src/Naravel.Mail/**`, `src/Naravel.Mail.Smtp/**` (heavy dependency, optional), `src/Naravel.Mail.Queue/**` (adapter), `tests/Naravel.Mail.Tests/**`, `Naravel.slnx`, docs/PDR-013.
Out of scope: view engines (the PDR chooses an `IMailRenderer` interface, no engine mandated); notifications (R10).
Decisions: OD-01 (dependency approval via PDR for the SMTP library), S1–S3.

## Tasks
- [x] **R09.T01 — PDR-013:** compare `System.Net.Mail`, MailKit, FluentEmail; define quickstart (`await mail.To(user).SendAsync(new Welcome(user))`), transports, queue adapter, `Mailable` shape. May reject building a transport and wrap a library. **Accept:** PDR EN+FA. PDR-013 was approved 2026-10-07; PDR-013a (Mailable, named transports, queue payload) was approved 2026-10-14, together with a requirement to keep usage and implementation docs current with the code.
- [x] **R09.T02 — Core (2026-10-14; tests NOT RUN locally, CI is the verification):** `IMailer` on `Manager<,>`, `Mailable`, built-in `log` and `array`(fake) transports. **Accept:** `Extend` + config-reload tests, `MailFake` assertions.
- [x] **R09.T03 — SMTP provider and queue adapter** (2026-10-17; tests NOT RUN locally, CI is the verification): `Naravel.Mail.Queue` (`QueueAsync`, merged in PR #44) and `Naravel.Mail.Smtp` (MailKit 4.18.1, TLS by default). **Accept:** queue adapter test with Memory queue; SMTP tested against an in-process SMTP server, plus an optional real-server test (`NARAVEL_TEST_SMTP`).
- [x] **R09.T04 — Validate (2026-10-17):** docs EN/FA, parity, changelog and README checked against the code; Definition of done items 1-7 walked through (benchmark not applicable: mail is I/O-bound; no shared driver contract suite exists for `log`/`array`/`smtp`, each transport has its own tests, noted for R17). **Accept:** DoD 5-7; CI on main green at 545ca02.
