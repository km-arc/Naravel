# R11 — Session (flash data and distributed stores)

Parity section: `R11` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-015 (EN+FA) approved first. Expected outcome per OD-08: `DECLINED` or a very small package.
Read: `src/Naravel.Routing/Middleware/**`, `src/Naravel.Cache/**` public surface; ASP.NET Core `ISession` docs.
Touch: new `src/Naravel.Session/**`, `tests/Naravel.Session.Tests/**`, docs/PDR-015.
Out of scope: authentication (R12); cookie encryption beyond Data Protection defaults (R18).
Decisions: Native baseline `ISession` + `IDistributedCache` first.

## Tasks
- [ ] **R11.T01 — PDR-015:** list exactly what `ISession` lacks (flash data, old input, store selection via Naravel.Cache). If nothing worth building: conclude `DECLINED`, set the row, and stop. **Accept:** PDR EN+FA with a verdict.
- [ ] **R11.T02 — Implementation (only if not declined):** flash/old-input helpers and a Cache-backed store. **Accept:** end-to-end tests with TestHost; `Fake`.
- [ ] **R11.T03 — Validate:** docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7 (or DECLINED evidence).
