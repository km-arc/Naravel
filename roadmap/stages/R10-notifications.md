# R10 — Notifications

Parity section: `R10` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-014 (EN+FA) approved first.
Read: `src/Naravel.Mail/**` public surface, `src/Naravel.Queue/Dispatch/IJobDispatcher.cs`.
Touch: new `src/Naravel.Notifications/**`, `tests/Naravel.Notifications.Tests/**`, `Naravel.slnx`, docs/PDR-014.
Out of scope: model traits/magic; broadcasting channel (R14) and database channel unless the PDR adds them.
Decisions: S1: `await notifier.SendAsync(user, new InvoicePaid(inv))` is the quickstart.

## Tasks
- [ ] **R10.T01 — PDR-014:** `INotification.Via()`, `ToMail()`, notifiable routing (`INotifiable`), channels, queued delivery, fake. **Accept:** PDR EN+FA.
- [ ] **R10.T02 — Core and channels:** channel manager on `Manager<,>`, `mail` channel (via R09), `log` channel, `NotificationFake`. **Accept:** `Extend` + config tests; fake assertions; failure of one channel does not stop others.
- [ ] **R10.T03 — Validate:** docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7.
