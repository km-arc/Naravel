# R09 — Mail

Parity section: `R09` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-013 (EN+FA) approved first.
Read: `src/Naravel.Queue/Dispatch/IJobDispatcher.cs`, `src/Naravel.Events/**` public surface, `docs/en/module-authoring-guide.md`.
Touch: new `src/Naravel.Mail/**`, `src/Naravel.Mail.Smtp/**` (heavy dependency, optional), `src/Naravel.Mail.Queue/**` (adapter), `tests/Naravel.Mail.Tests/**`, `Naravel.slnx`, docs/PDR-013.
Out of scope: view engines (the PDR chooses an `IMailRenderer` interface, no engine mandated); notifications (R10).
Decisions: OD-01 (dependency approval via PDR for the SMTP library), S1–S3.

## Tasks
- [ ] **R09.T01 — PDR-013:** compare `System.Net.Mail`, MailKit, FluentEmail; define quickstart (`await mail.To(user).SendAsync(new Welcome(user))`), transports, queue adapter, `Mailable` shape. May reject building a transport and wrap a library. **Accept:** PDR EN+FA.
- [ ] **R09.T02 — Core:** `IMailer` on `Manager<,>`, `Mailable`, built-in `log` and `array`(fake) transports. **Accept:** `Extend` + config-reload tests, `MailFake` assertions.
- [ ] **R09.T03 — SMTP provider and queue adapter:** `Naravel.Mail.Smtp`, `Naravel.Mail.Queue` (`QueueAsync`). **Accept:** queue adapter test with Memory queue; SMTP test env-gated (`NARAVEL_TEST_SMTP`).
- [ ] **R09.T04 — Validate:** docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7.
