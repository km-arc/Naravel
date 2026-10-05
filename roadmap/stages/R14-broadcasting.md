# R14 — Broadcasting

Parity section: `R14` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-018 (EN+FA) approved first. Expected: thin layer over SignalR (OD-08).
Read: `src/Naravel.Events/**` public surface; ASP.NET Core SignalR docs.
Touch: new `src/Naravel.Broadcasting/**`, tests, docs/PDR-018.
Out of scope: replacing SignalR; Redis backplane (native SignalR feature).
Decisions: Native baseline: SignalR.

## Tasks
- [ ] **R14.T01 — PDR-018:** typed `IBroadcastable` events, channel authorization callbacks (`channels.Authorize("orders.{id}", ...)`), drivers `signalr`/`log`/`null`. **Accept:** PDR EN+FA.
- [ ] **R14.T02 — Implementation:** broadcaster on `Manager<,>`, SignalR driver, `BroadcastFake`. **Accept:** TestHost + SignalR client test receives the event; unauthorized channel is rejected.
- [ ] **R14.T03 — Validate:** docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7.
