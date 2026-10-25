# R03 — Naravel.Redis shared connections

Parity section: `R03` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-010 (EN+FA) approved first.
Read: `src/Naravel.Cache.Redis/**`, `src/Naravel.Queue.Redis/**`, `src/Naravel.Foundation/{Manager,ManagerOptions}.cs`, `docs/pdr/en/PDR-004*.md`, `docs/pdr/en/PDR-005*.md`.
Touch: new `src/Naravel.Redis/**`, `tests/Naravel.Redis.Tests/**`, `src/Naravel.Cache.Redis/**`, `src/Naravel.Queue.Redis/**`, `Naravel.slnx`, docs/PDR-010.
Out of scope: RateLimiter (R04); Lua scripts already written in R01 stay (move only if the PDR says so); StackExchange.Redis 3.x upgrade (needs approval).
Decisions: OD-01 (env-gated tests), OD-06.

Note: PDR-010 was approved on 2026-10-25 (D1 to D4 as recommended). The StackExchange.Redis 3.x upgrade was already done in the dependency refresh (AUDIT D-65, CI green), so it is no longer out of scope to know about, only to do again.

## Tasks
- [x] **R03.T01 — PDR-010 (approved 2026-10-25, D1 to D4 as recommended):** named-connection semantics, `IConnectionMultiplexer` ownership/disposal, config shape (`Stores` keyword per PDR-005), reload, migration path for Cache and Queue; native-first verdict (is `IConnectionMultiplexer` registered once in DI enough?). May conclude `DECLINED`. **Accept:** PDR-010 EN+FA exist with a ≤ 10-line quickstart.
- [x] **R03.T02 — Shared module:** `Naravel.Redis` on `Manager<,>`; one multiplexer per named connection. **Accept:** `Extend` and config-reload tests; no duplicate pools (test counts multiplexer creations). Done: `RedisManager`, `IRedisConnection`, `AddNaravelRedis`, `FromConnectionString`; `RedisManagerTests` (one multiplexer per name, default, implicit hash sharing, disposal once, unknown name, missing string, `Extend`, reload, unrelated edit). CI is the check (no local .NET SDK): green on PR #134 at ec81d8a, run 40189845619, all legs including the three `database` legs and the provider integration tests.
- [x] **R03.T03 — Migration:** Cache.Redis and Queue.Redis resolve connections from the shared manager; old config keys keep working or the break is in `CHANGELOG.md`. **Accept:** R01 provider tests pass unchanged. Done: `Connection` key and `ConnectionString` de-duplicated in Cache.Redis and Queue.Redis, `ownsConnection` flag, `SharedConnectionMigrationTests`; the R01 provider tests pass unchanged (the queue driver keeps its default of not disposing a given connection, AUDIT D-66). CI is the check (no local .NET SDK): green on PR #134 at ec81d8a, run 40189845619, all legs including the three `database` legs and the provider integration tests.
- [x] **R03.T04 — Validate:** fake + env-gated tests, bilingual docs, parity/status/changelog, full verification. **Accept:** DoD items 5–7. Done: `docs/en|fa/redis.md`, README EN/FA, both parity tables, PARITY-MATRIX, CHANGELOG, AUDIT D-66. Build and tests NOT RUN locally; CI is the check (no local .NET SDK): green on PR #134 at ec81d8a, run 40189845619, all legs including the three `database` legs and the provider integration tests.
