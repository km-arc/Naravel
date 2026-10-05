# R12 — Auth-lite route middleware

Parity section: `R12` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-016 (EN+FA) approved first. Expected outcome per OD-08: tiny or `DECLINED`.
Read: `src/Naravel.Routing/Middleware/{MiddlewareOptions,MiddlewarePipelineFactory}.cs`; ASP.NET Core Authorization docs.
Touch: `src/Naravel.Routing/**` (aliases only) or new `src/Naravel.Auth/**` if the PDR demands, tests, docs/PDR-016.
Out of scope: a new identity system, password hashing (R15), sessions (R11).
Decisions: Native baseline: ASP.NET Core Authentication/Authorization/Identity.

## Tasks
- [ ] **R12.T01 — PDR-016:** can `auth`, `guest`, `can:policy`, `verified` be plain aliases to `RequireAuthorization`-style policies in the existing engine? If yes, the PDR may conclude `DECLINED` plus a docs recipe. **Accept:** PDR EN+FA with a verdict.
- [ ] **R12.T02 — Aliases (only if not declined):** register the aliases. **Accept:** end-to-end 401/403/200 tests.
- [ ] **R12.T03 — Validate:** docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7 (or DECLINED evidence).
