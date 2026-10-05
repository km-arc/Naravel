# R06 — Events

Parity section: `R06` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-011 (EN+FA) approved first.
Read: `src/Naravel.Foundation/**` (public surface only), `src/Naravel.Queue/Dispatch/IJobDispatcher.cs`, `docs/en/module-authoring-guide.md`.
Touch: new `src/Naravel.Events/**`, optional `src/Naravel.Events.Queue/**`, `tests/Naravel.Events.Tests/**`, `Naravel.slnx`, docs/PDR-011.
Out of scope: model/observer events; queued listeners must live in the optional adapter project, never in the core (no cycle, PDR-001 rule 3).
Decisions: S2: listener lists per event type are built once and cached (no per-dispatch reflection).

## Tasks
- [ ] **R06.T01 — PDR-011:** native-first verdict (delegates/MediatR-style vs a tiny dispatcher), quickstart (`events.Listen<OrderPaid>(...)`, `await events.DispatchAsync(new OrderPaid(id))`), subscribers, ordering, stop-propagation, queued-listener adapter. **Accept:** PDR EN+FA.
- [ ] **R06.T02 — Core dispatcher:** `IEventDispatcher`, DI-resolved `IEventListener<TEvent>`, cached listener plans, `EventFake`. **Accept:** tests for order, multiple listeners, exception policy, `Extend` where applicable, fake assertions, `Meter`/`ActivitySource`.
- [ ] **R06.T03 — Queued listeners (adapter):** `Naravel.Events.Queue` dispatches listeners marked queued through `IJobDispatcher`. **Accept:** test with Memory queue runs the listener in the worker.
- [ ] **R06.T04 — Validate:** benchmark (dispatch/s, 0 allocations in steady state if achievable), docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7.
