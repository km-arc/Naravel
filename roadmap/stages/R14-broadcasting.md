# R14 — Broadcasting

Parity section: `R14` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-018 (EN+FA) approved first. Expected: thin layer over SignalR (OD-08).
Read: `src/Naravel.Events/**` public surface; ASP.NET Core SignalR docs.
Touch: new `src/Naravel.Broadcasting/**`, tests, docs/PDR-018.
Out of scope: replacing SignalR; Redis backplane (native SignalR feature).
Decisions: Native baseline: SignalR.

Note: PDR-018 was approved on 2026-10-25 (D1 to D3 as recommended, D4 option C: presence member lists in the shared cache, D5 option A: inline at-most-once delivery).

## Tasks
- [x] **R14.T01 — PDR-018 (approved 2026-10-25):** typed `IBroadcastable` events, channel authorization callbacks (`channels.Authorize("orders.{id}", ...)`), drivers `signalr`/`log`/`null`. **Accept:** PDR EN+FA.
- [x] **R14.T02 — Implementation:** broadcaster on `Manager<,>`, SignalR driver, `BroadcastFake`. **Accept:** TestHost + SignalR client test receives the event; unauthorized channel is rejected. Done in `src/Naravel.Broadcasting` and `tests/Naravel.Broadcasting.Tests`, including presence member lists; build and tests NOT RUN locally (no .NET SDK), CI is the check.
- [x] **R14.T03 — Validate:** docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7. Evidence so far: `docs/en|fa/broadcasting.md`, README EN/FA, both parity tables, the matrix, the changelog and the roadmap check. No benchmark (S2): a broadcast is I/O-bound. The stage stays `DOING` until CI is green on main.
