# PROGRESS-ROUTING — start here for the Routing + HTTP Middleware work

> **فارسی (خلاصه):** این فایل سابقهٔ جزئیات روتینگ و میدلور است. اولویت و وضعیت جاری فقط در [ROADMAP.md](ROADMAP.md) ثبت می‌شود؛
> این فایل را برای تصمیم‌های تأییدشده و سابقهٔ Stage 4a/4b بخوانید.

> Historical tracker: the authoritative project-wide stage status and `NEXT` pointer now live in
> [ROADMAP.md](ROADMAP.md), with routing completion tracked as R05. Keep this file as the detailed record
> of PDR-009, Stage 4a, and the pending owner-gated middleware scope; do not use its historical pointer below.

## Historical verification note (2026-10-04)

Stage 4a-verify completed after the dependency changes. The CS1734 warning in
`src/Naravel.Routing/Middleware/IRouteMiddleware.cs` was fixed. R05 in `ROADMAP.md` is the authoritative routing priority.

## Read first
1. `AGENTS.md` (hard rules), then `docs/pdr/en/PDR-009-routing-and-http-middleware.md` (the binding design, **Accepted**).
2. `docs/en/routing.md` (what Stage 4a delivered) and `docs/en/module-authoring-guide.md` (monorepo checklist).
3. `PROGRESS.md` (owner-approved decisions for the rest of the repo; do not re-ask them).
4. This file's stage list below.

## Owner-approved decisions (do not re-litigate)
- Architecture: **thin layer on ASP.NET Core** (not a standalone router; not middleware-only).
- Package: **`Naravel.Routing`** (routing API, middleware engine, and ready-made HTTP middleware).
- Middleware scope: implement the ones that are possible **now**; list the rest (PDR-009 catalog) and leave them to the modules they need.
- Sign-off items 1-3 of PDR-009 (AspNetCore framework reference, no static facade, Minimal API resources first) were approved.
- Working style: **one stage per pass**: code + tests + docs for that stage only, update this file, deliver a zip, **stop**.
- Ask the owner before any large decision; do not guess.

## How to mark a stage completed (mandatory protocol)
1. Tick its checklist items (`[x]`) and change its row in the status table to `DONE`.
2. Fill the row's **Completed** date and **Verification** (`dotnet build/test: passed` / `NOT RUN: <reason>`). Never write "passed" for tests you did not run.
3. Update the **NEXT** pointer above to the following stage.
4. Update the Module status table in `AGENTS.md`, both parity tables (`docs/en|fa/laravel-parity.md`) and `PROGRESS.md` if they changed.
5. Stop. Report what was and was not verified.

## Stage status

| Stage | Scope | Status | Completed | Verification |
|---|---|---|---|---|
| 4.0 | PDR-009 (EN+FA), this tracker, status tables | **DONE** | 2026-10-03 | Docs only. |
| 4.0b | Owner reviews PDR-009 and its 3 sign-off items | **DONE** | 2026-10-03 | Owner told the agent to continue after seeing them. |
| 4a | `Naravel.Routing` core + middleware engine (code + 64 tests + EN/FA docs) | **DONE (verified)** | 2026-10-04 | `dotnet test`: 64/64 Routing tests passed (159/159 repo-wide); `dotnet build Naravel.slnx -c Release`: succeeded cleanly with no CS1734 warning. Run by the owner, .NET runtime 10.0.12. |
| 4a-verify | Build + test Stage 4a, fix findings | **DONE** | 2026-10-04 | Build and all tests passed; the CS1734 issue was fixed and no warning remains. |
| 4b | Ready-made HTTP middleware in `Naravel.Routing` | NOT STARTED (scope consolidated 2026-10-04; waits for the owner's go-ahead) | - | Source/docs inspection: no ready-made implementations found. |
| 4c | Deferred middleware (Auth/Session/Redis throttle) | BLOCKED on other modules | - | - |

---

## Stage 4.0 - PDR and tracking (DONE)
- [x] `docs/pdr/en|fa/PDR-009-routing-and-http-middleware.md`
- [x] `PROGRESS-ROUTING.md` (this file), pointers in `AGENTS.md` and `PROGRESS.md`
- [x] Module status, parity tables, README and CHANGELOG updated

## Stage 4a - `Naravel.Routing` (DONE, verified 2026-10-04)
- [x] Projects `src/Naravel.Routing`, `tests/Naravel.Routing.Tests`; both in `Naravel.slnx`; `FrameworkReference Microsoft.AspNetCore.App`; `Microsoft.AspNetCore.TestHost` version in `Directory.Packages.props`.
- [x] `AddNaravelRouting(...)`, `UseNaravelRouting()`, `MapNaravel(r => ...)`, `WithNaravelMiddleware` / `WithoutNaravelMiddleware` for native endpoints.
- [x] `IRouteRegistrar`: get/post/put/patch/delete/options/match/any, fallback, redirect; nested `Prefix/Name/Domain/Middleware/WithoutMiddleware/Group`.
- [x] Named routes + `IUrlGenerator.Route/AbsoluteRoute`; `.Where()` (anchored) and global patterns.
- [x] Middleware engine: `IRouteMiddleware`, `MiddlewareArguments`, aliases, groups (+ prepend/append/replace/remove), `alias:a,b` parameters, inline delegates,
      priority, `WithoutMiddleware`, `ITerminableMiddleware`, `[Middleware]`/`[WithoutMiddleware]` attributes (Only/Except), `IMiddleware` adapter.
- [x] Per-endpoint pipeline resolved once and cached; Minimal API and controllers.
- [x] Explicit model binding (`RoutingOptions.Bind`) + `bindings` middleware + `GetRouteModel`.
- [x] Resource / apiResource for Minimal API (Only/Except/Parameter, names).
- [ ] **Deferred follow-up:** controller `Resource` mapping (recorded in PDR-009 addendum; do it in 4b or later, do not hide it).
- [x] Tests (64): see `docs/en/routing.md`. Docs EN+FA, XML docs on public members.
- [x] **Verified 2026-10-04:** build and all 64 tests passed, so the risk areas below turned out fine. (Kept for history.) Risk areas that were checked:
      1. `TestServer` + `WebApplication` wiring in `tests/Naravel.Routing.Tests/TestSupport.cs` (`UseTestServer`, `IServer` cast).
      2. Controller tests: `AddApplicationPart` + `MapControllers`, and that `ControllerActionDescriptor.ActionName` is `Index`/`Show`/`Skip`.
      3. The terminable test waits on `Response.OnCompleted` under TestServer.
      4. `RoutePatternFactory.Parse(uri, null, policiesDictionary)` accepting a `RouteValueDictionary` of `RegexRouteConstraint`.
      5. `Domain` tests: `RequireHost` with absolute-URI requests through `TestServer.CreateClient()`.
      6. XML-doc `cref` warnings (not errors).

### Runnable sample coverage
- [x] `samples/Naravel.Sample` demonstrates the implemented route registrar, route groups, constraints, named URLs, model binding, resource routes,
      middleware variants, controller attributes, and native ASP.NET Core endpoint integration.
- [ ] Controller `Resource` mapping remains a deferred routing follow-up; the sample uses Minimal API resource handlers as supported today.
- [ ] `route:list` needs the Console module; signed URL generation belongs to Stage 4b; auth/session middleware waits on those modules.
- [ ] View helpers, string controller actions, route caching, scoped binding, missing-model callbacks, and authorization policies remain unsupported or intentionally unported;
      do not represent the sample as full Laravel parity.

## Stage 4b - Ready-made HTTP middleware in `Naravel.Routing` (NOT STARTED)
Precondition: Stage 4a-verify is DONE (it is, 2026-10-04) and the owner says go.
- [x] Status checked 2026-10-04: `Naravel.Routing/Middleware` contains the route-middleware engine (`MiddlewareOptions`, aliases/groups, pipeline and contracts),
      not the ready-made HTTP middleware below. The separate package was removed from the plan; throttle/signed/maintenance/TrimStrings implementations do not exist yet.
- [ ] Extend `src/Naravel.Routing` and `tests/Naravel.Routing.Tests`; no separate middleware project or test project is planned.
- [ ] `throttle` (`throttle:60,1`, named limiters, `X-RateLimit-*`, `Retry-After`) on `System.Threading.RateLimiting`.
- [ ] `signed` + signed URL generation (Data Protection).
- [ ] Maintenance mode middleware + service (no CLI commands yet).
- [ ] `TrimStrings`, `ConvertEmptyStringsToNull` (query + form; JSON out of scope, document it).
- [ ] `cache.headers`, `guest`.
- [ ] Register ready-made middleware through the existing `AddNaravelRouting(...)` configuration surface; do not create a separate package registration API.
- [ ] Tests for each; docs EN+FA; list the Native items (CORS, TrustProxies, TrustHosts, ValidatePostSize, EncryptCookies) in the docs with the .NET equivalent.
- [ ] Optional: controller `Resource` mapping follow-up from 4a.
- [ ] Mark stage completed; set NEXT to "wait for owner".

## Stage 4c - Deferred (do not start)
Blocked until the owning module exists: `auth`, `auth.basic`, `can`, `verified`, `password.confirm` (Auth); `StartSession`, `VerifyCsrfToken`,
`ShareErrorsFromSession` (Session); Redis-backed throttle (`Naravel.Cache.Redis`); `down`/`up` commands (Console).

## Roadmap pointer

The old broad feature inventory and sequencing are superseded by [ROADMAP.md](ROADMAP.md). New candidates that
are not in R00–R17 belong in [roadmap/BACKLOG.md](roadmap/BACKLOG.md), not in this historical routing tracker.
