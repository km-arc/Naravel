# R04 — RateLimiter on Cache

Parity section: `R04` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-007 addendum `docs/pdr/{en,fa}/PDR-007a-rate-limiter.md` approved first. Cache/tags/scopes/locks are done — do not touch them except where listed.
Read: `src/Naravel.Cache/{Abstractions/ICacheStore.cs, Stores/MemoryCacheStore.cs, CacheManager.cs}`, `src/Naravel.Cache.Redis/RedisCacheStore.cs`, `src/Naravel.Cache.Memcached/MemcachedCacheStore.cs`, `docs/pdr/en/PDR-007-cache-port.md`.
Touch: `src/Naravel.Cache*/**`, `tests/Naravel.Cache.Tests/**`, `benchmarks/**`, `docs/en|fa/cache.md`, PDR-007a.
Out of scope: HTTP throttle middleware (R05), queue rate-limit middleware (R08), shared Redis (R03).
Decisions: OD-01, OD-06; native baseline `System.Threading.RateLimiting` is in-process only — the value here is shared counters across instances.

## Tasks
- [x] **R04.T01 — PDR-007a:** map Laravel `RateLimiter` (`attempt`, `tooManyAttempts`, `hit`, `remaining`, `availableIn`, `clear`, named limiters) to the smallest .NET API; state quickstart; note that `ICacheStore.IncrementAsync` has **no TTL**, so a fixed window needs an atomic increment-with-expiry — decide between a new `IncrementAsync(key, by, ttl)` overload or an `AddAsync` + increment pair, and the per-provider implementation (Memory per-key lock, Redis Lua `INCRBY`+`PTTL`/`PEXPIRE` when missing, Memcached add+incr). **Accept:** PDR EN+FA.
- [x] **R04.T02 — Store primitive:** implement the approved atomic increment-with-expiry in Memory, Redis, Memcached. **Accept:** contract test: N concurrent increments → exact count; key expires after TTL; works on all three (Redis/Memcached env-gated).
- [x] **R04.T03 — RateLimiter:** `IRateLimiter` over a named store (fixed window; sliding only if the PDR says so) plus `Fake`. **Accept:** boundary tests (exactly `max`, `max+1`, window rollover, `availableIn`), named stores isolated, `Meter` counters present.
- [ ] **R04.T04 — Validate:** benchmark (hits/second on Memory store), bilingual docs, parity/status/changelog, full verification. **Accept:** DoD items 5–7.
