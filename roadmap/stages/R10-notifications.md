# R10 — Notifications

Parity section: `R10` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-014 (EN+FA) approved first.
Read: `src/Naravel.Mail/**` public surface, `src/Naravel.Queue/Dispatch/IJobDispatcher.cs`.
Touch: new `src/Naravel.Notifications/**`, `src/Naravel.Notifications.Mail/**`, `src/Naravel.Notifications.Queue/**`, their `tests/Naravel.Notifications*.Tests/**`, `Naravel.slnx`, docs/PDR-014 (the adapters were added to Touch because PDR-014 makes Mail and Queue separate opt-in packages; AUDIT D-60).
Out of scope: model traits/magic; broadcasting channel (R14) and database channel unless the PDR adds them.
Decisions: S1: `await notifier.SendAsync(user, new InvoicePaid(inv))` is the quickstart.

## Tasks
- [x] **R10.T01 — PDR-014:** `INotification.Via()`, `ToMail()`, notifiable routing (`INotifiable`), channels, queued delivery, fake. **Accept:** PDR EN+FA. Done and owner-approved 2026-10-07; the approved PDR chose explicit typed channels named by the caller and rejected `Via()`/`INotifiable`, so those were not built (AUDIT D-60).
- [x] **R10.T02 — Core and channels:** channel manager on `Manager<,>`, `mail` channel (via R09), `log` channel, `NotificationFake`. **Accept:** `Extend` + config tests; fake assertions; failure of one channel does not stop others. Done as PDR-014 specifies: `Naravel.Notifications` (sender, typed channels as class or delegate, log channel, fake, telemetry), `Naravel.Notifications.Mail` and `Naravel.Notifications.Queue`. Differences from this line, all from the approved PDR: no manager (channels are typed DI registrations chosen by name), so the runtime-registration test replaces `Extend` and there is no config to reload; a failing channel stops the dispatch and is reported with the channels already delivered. CI is the check (no local .NET SDK): green on PR #124 head 03d2465, run 40097461927 (three OSs and the services job).
- [x] **R10.T03 — Validate:** docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7. `docs/en|fa/notifications.md` with identical headings, README EN/FA, both parity tables, the parity matrix and the changelog updated; no benchmark because dispatch is I/O-bound. CI is the check (no local .NET SDK): green on PR #124 head 03d2465, run 40097461927 (three OSs and the services job).
