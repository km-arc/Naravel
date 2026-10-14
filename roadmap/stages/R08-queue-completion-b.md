# R08 — Queue completion B: coordination middleware

Parity section: `R08` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR addendum to PDR-012 (`PDR-012b`) approved first.
Read: `src/Naravel.Queue/{Middleware,Worker,Jobs}/**`, `src/Naravel.Cache/{Abstractions/ICacheLock.cs,LockManager.cs}`, R04 `IRateLimiter`.
Touch: `src/Naravel.Queue/**` (new `Middleware/` classes), `tests/Naravel.Queue*.Tests/**`, docs, PDR-012b. Owner decision 2026-10-13: the Cache-dependent middleware live in a new adapter project `src/Naravel.Queue.Coordination` (+ `tests/Naravel.Queue.Coordination.Tests`); `Naravel.Queue` itself never references Cache. Also touches the small `Naravel.Queue` core change in PDR-012b section 2 (per-job middleware, `JobContext.Release/Skip`).
Out of scope: cache/lock internals (R04); Redis topology (R03).
Decisions: Memory locks are single-process: never promise cross-process uniqueness from them (docs must say so).

## Tasks
- [x] **R08.T01 — PDR-012b:** per-job middleware contract (`IJobWithMiddleware`), module boundary (adapter project vs reference), semantics of unique/overlap/rate-limited/throttle-exceptions. **Accept:** PDR EN+FA. (Approved 2026-10-13.) Drafted 2026-10-13 as `docs/pdr/*/PDR-012b-queue-coordination-middleware.md`; awaiting owner approval (no code before approval).
- [x] **R08.T02 — Middleware:** unique jobs via `IUniqueJob` + `IUniqueJobGate` (key + lock TTL, PDR-012b implementation record), `WithoutOverlapping`, `RateLimited` (R04), `ThrottlesExceptions`. Debounce moved to `roadmap/BACKLOG.md` (owner, 2026-10-13). **Accept:** unit tests with Memory lock; **cross-process** uniqueness proven by an env-gated Redis test with two workers.
- [ ] **R08.T03 — Validate (partial 2026-10-14: Memory warning and parity table done; sample skipped, see PDR-012b; CI evidence pending):** fake support, docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7.
