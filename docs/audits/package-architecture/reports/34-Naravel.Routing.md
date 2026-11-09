# Review Report — Naravel.Routing

## Review metadata

- Package ID: `Naravel.Routing`.
- Source project: `src/Naravel.Routing/Naravel.Routing.csproj`.
- Source-review baseline: `main` at `198b36f9e2753ccfb68354a02480cdc25e7eabc7`; this audit branch contains that commit.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source and contract review; independent second review NOT RUN.
- Status: REVIEWED WITH FINDINGS (1 P2; source inspection only).
- Files inspected: routing endpoint extensions; middleware options/pipeline/resolved middleware/metadata; built-in middleware; throttle guard/breaker/store; route, middleware and throttle tests; README and guide..
- Dependencies: ASP.NET Core shared framework and Data Protection. Optional `Naravel.Routing.Cache` adds a shared throttle store..
- Verification performed: source-to-contract tracing and inspection of the listed tests and documentation.
- Verification NOT RUN: .NET restore/build/test, live provider or service execution unless explicitly stated below, full CI and mutation testing.

## Executive assessment

The package runs route-specific middleware through endpoint metadata, aliases, groups, exclusions and priorities. Throttle supports in-process and pluggable stores, timeout and breaker behavior. Tests cover middleware resolution and throttle failures. Fallback-created middleware instances are not tracked by request DI and have no disposal path.

## Package and dependency map

- Registration: `MapNaravel`, `UseNaravelRouting`, `WithNaravelMiddleware`.
- Pipeline: `MiddlewarePipelineFactory`, `ResolvedMiddleware`.
- Contract: `IRouteMiddleware`; optional `ITerminableMiddleware`.
- Throttle: `IThrottleStore`, `ThrottleGuard`, `ThrottleBreaker`.

## Findings


### [P2] RTG-001 — Middleware created outside DI is not disposed

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; runtime reproduction NOT RUN.
- Exact source evidence: `ResolvedMiddleware.Bind` first looks up `middlewareType` in `context.RequestServices`; if missing it creates an object through `ActivatorUtilities.CreateFactory`. The instance is held only in a local variable. The code registers `OnCompleted` for `ITerminableMiddleware` but has no `Dispose` or `DisposeAsync` path for the fallback-created instance. `MiddlewareTypes.Validate` permits concrete classes implementing either middleware interface, including disposable ones.
- Affected API/configuration: Type-based aliases through `MiddlewareOptions.Alias` and `MiddlewareSpec.OfType`.
- User impact: A middleware that opens a stream, timer, socket or other resource can leave it alive after each request; under load, resources can accumulate.
- Trigger/example: Register a custom disposable middleware by type but do not register it as a DI service, then send repeated requests.
- Why this proves the concern: Objects created directly by `ActivatorUtilities` are not owned by the request service scope, and the caller never disposes them.
- Existing test gap: Existing tests cover invocation, DI dependency injection and ordering, but do not verify disposal of an unregistered fallback-created disposable middleware.
- Required safe outcome: Define one ownership rule: require registration in DI with a clear error, or dispose fallback-created instances after request/termination work. Preserve the normal scope disposal for DI-registered middleware.
- Regression tests required: Unregistered `IDisposable` and `IAsyncDisposable` instances dispose once; DI-registered scoped instances are disposed by scope once; terminable fallback middleware is disposed only after termination completes.
- Owner decision: Owner decision is needed if fallback activation is removed and DI registration becomes mandatory.

#### Plain-language explanation

**In everyday words:** When middleware is not registered in the service container, Routing creates it itself. The request scope does not know it owns that object, so it is not cleaned up.

**A realistic example:** The custom middleware opens a stream for every request. Each request creates a new middleware instance, but no code closes its stream after the response.

**What we checked:** `ResolvedMiddleware.Bind` first looks up `middlewareType` in `context.RequestServices`; if missing it creates an object through `ActivatorUtilities.CreateFactory`. The instance is held only in a local variable. The code registers `OnCompleted` for `ITerminableMiddleware` but has no `Dispose` or `DisposeAsync` path for the fallback-created instance. `MiddlewareTypes.Validate` permits concrete classes implementing either middleware interface, including disposable ones.

**Why that proves the concern:** Objects created directly by `ActivatorUtilities` are not owned by the request service scope, and the caller never disposes them.

**What is not yet proven:** the review inspected source and existing test code only. The behavior was not reproduced by running a test or a live service in this review.

**What should change:** Define one ownership rule: require registration in DI with a clear error, or dispose fallback-created instances after request/termination work. Preserve the normal scope disposal for DI-registered middleware.

**How we will prove the fix:** Unregistered `IDisposable` and `IAsyncDisposable` instances dispose once; DI-registered scoped instances are disposed by scope once; terminable fallback middleware is disposed only after termination completes.


## Test assessment

- Existing tests inspected: middleware resolution/end-to-end tests, `BindingsAndResourceTests`, `ThrottleStoreTests`, `ThrottleFailureTests`, routing and signed URL tests..
- Coverage seen in source: alias/group expansion, exclusions, ordering, arguments, attributes, route binding, maintenance, throttle timeout/breaker/failure modes and client cancellation..
- Gaps: unregistered fallback-created `IDisposable`/`IAsyncDisposable` middleware lifetime and its relationship to termination callbacks.
- Tests actually run for this review: NONE.
- Build/restore: NOT RUN.
- External service/provider integration: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Documentation and packaging assessment

Guide explains DI resolution and type-based middleware, but does not define ownership of objects created by the fallback activator.

## Risk summary and next action

- P2: RTG-001.
- P0 / P1 / P3 / OBS / DOC: none recorded.

Recommended next task: define ownership and dispose fallback-created middleware exactly once after request and termination work. Do not change runtime code in this audit without owner approval.

## Plain-language explanation

This package adds middleware to individual ASP.NET Core routes. Source and tests were inspected; no build or tests ran. The finding concerns disposal of middleware objects created outside DI.
