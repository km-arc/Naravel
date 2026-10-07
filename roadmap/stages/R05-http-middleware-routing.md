# R05 — HTTP middleware and routing completion (Stage 4b)

Parity section: `R05` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: GO — owner must explicitly authorize before implementation. PDR-009 is accepted.
Read: `src/Naravel.Routing/{Middleware/*, Url/*, Extensions/*, Options/RoutingOptions.cs}`, `tests/Naravel.Routing.Tests/{TestSupport,MiddlewareEndToEndTests}.cs`, `PROGRESS-ROUTING.md` (decisions only), `docs/pdr/en/PDR-009*.md`.
Touch: `src/Naravel.Routing/**`, `tests/Naravel.Routing.Tests/**`, `docs/en|fa/routing.md`, `benchmarks/**`.
Out of scope: Session/Auth-backed middleware (R11/R12); CLI `route:list` (R13); Filesystem (R02).
Decisions: PDR-009; S2: middleware must be allocation-light and benchmarked.

## Tasks
- [x] **R05.T01 — Scope confirmation:** owner approved all listed middleware/features, including controller `Resource` mapping, on 2026-10-06. Throttle uses local `System.Threading.RateLimiting` as specified by PDR-009, not R04's Cache-backed `IRateLimiter`. **Accept:** in-scope items recorded here.
- [x] **R05.T02 — `throttle`:** use `System.Threading.RateLimiting`; set `X-RateLimit-Limit/Remaining` and `Retry-After`, return 429; support `throttle:60,1` and named limiters. **Accept:** end-to-end tests for allow, block, header values, named limiter.
- [x] **R05.T03 — Signed URLs:** `IUrlGenerator.SignedRoute` / `TemporarySignedRoute` and `signed` middleware, signature via ASP.NET Core Data Protection (no custom crypto). **Accept:** tampered query → 403, expired → 403, valid → 200.
- [x] **R05.T04 — Maintenance & content middleware:** `PreventRequestsDuringMaintenance` (flag + secret bypass + `Retry-After`), `SetCacheHeaders`, `TrimStrings`/`ConvertEmptyStringsToNull` (verify minimal-API vs MVC behaviour first; ship only if it adds value), and `guest` (uses `HttpContext.User`). **Accept:** one end-to-end test each.
- [x] **R05.T05 — Controller `Resource` mapping:** owner accepted this in T01. **Accept:** route table test for the seven resource routes.
- [x] **R05.T06 — Validate:** benchmark for the middleware pipeline, EN/FA docs, XML docs, parity/status/changelog, full verification. **Accept:** DoD items 5–7.
