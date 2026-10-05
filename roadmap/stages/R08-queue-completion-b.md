# R08 — Queue completion B: coordination middleware

Parity section: `R08` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR addendum to PDR-012 (`PDR-012b`) approved first.
Read: `src/Naravel.Queue/{Middleware,Worker,Jobs}/**`, `src/Naravel.Cache/{Abstractions/ICacheLock.cs,LockManager.cs}`, R04 `IRateLimiter`.
Touch: `src/Naravel.Queue/**` (new `Middleware/` classes), `tests/Naravel.Queue*.Tests/**`, docs, PDR-012b. Queue may reference Cache only through an optional adapter project if the PDR says so.
Out of scope: cache/lock internals (R04); Redis topology (R03).
Decisions: Memory locks are single-process: never promise cross-process uniqueness from them (docs must say so).

## Tasks
- [ ] **R08.T01 — PDR-012b:** per-job middleware contract (`IJobWithMiddleware`), module boundary (adapter project vs reference), semantics of unique/overlap/debounce/rate-limited/throttle-exceptions. **Accept:** PDR EN+FA.
- [ ] **R08.T02 — Middleware:** `UniqueJob` (key + lock TTL), `WithoutOverlapping`, `RateLimited` (R04), `ThrottlesExceptions`, debounce. **Accept:** unit tests with Memory lock; **cross-process** uniqueness proven by an env-gated Redis test with two workers.
- [ ] **R08.T03 — Validate:** fake support, docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7.
