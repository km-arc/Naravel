# R03 — Naravel.Redis shared connections

Parity section: `R03` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-010 (EN+FA) approved first.
Read: `src/Naravel.Cache.Redis/**`, `src/Naravel.Queue.Redis/**`, `src/Naravel.Foundation/{Manager,ManagerOptions}.cs`, `docs/pdr/en/PDR-004*.md`, `docs/pdr/en/PDR-005*.md`.
Touch: new `src/Naravel.Redis/**`, `tests/Naravel.Redis.Tests/**`, `src/Naravel.Cache.Redis/**`, `src/Naravel.Queue.Redis/**`, `Naravel.slnx`, docs/PDR-010.
Out of scope: RateLimiter (R04); Lua scripts already written in R01 stay (move only if the PDR says so); StackExchange.Redis 3.x upgrade (needs approval).
Decisions: OD-01 (env-gated tests), OD-06.

## Tasks
- [ ] **R03.T01 — PDR-010:** named-connection semantics, `IConnectionMultiplexer` ownership/disposal, config shape (`Stores` keyword per PDR-005), reload, migration path for Cache and Queue; native-first verdict (is `IConnectionMultiplexer` registered once in DI enough?). May conclude `DECLINED`. **Accept:** PDR-010 EN+FA exist with a ≤ 10-line quickstart.
- [ ] **R03.T02 — Shared module:** `Naravel.Redis` on `Manager<,>`; one multiplexer per named connection. **Accept:** `Extend` and config-reload tests; no duplicate pools (test counts multiplexer creations).
- [ ] **R03.T03 — Migration:** Cache.Redis and Queue.Redis resolve connections from the shared manager; old config keys keep working or the break is in `CHANGELOG.md`. **Accept:** R01 provider tests pass unchanged.
- [ ] **R03.T04 — Validate:** fake + env-gated tests, bilingual docs, parity/status/changelog, full verification. **Accept:** DoD items 5–7.
