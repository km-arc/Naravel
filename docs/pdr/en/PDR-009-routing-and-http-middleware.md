# PDR-009 — Routing and HTTP middleware (`Naravel.Routing`)

Status: **Accepted.** The owner approved the plan in chat (architecture, package scope, feature scope, stage order) and then told the agent to continue
after reading the three sign-off items below, which is recorded as approval of them. On 2026-10-04, the owner consolidated ready-made HTTP middleware
into `Naravel.Routing`; Stage 4b remains pending and will extend that project.

PDR-007 (Cache) and PDR-008 (Filesystem) were approved and implemented (see `PROGRESS.md`). This PDR is numbered 009 so those numbers stay stable.

## Context

Laravel's routing layer gives: fluent route registration, nested groups (`prefix`, `name`, `domain`, `middleware`), named routes
and URL generation, route model binding, resource routes, and a middleware system (global, group, route, controller,
parameterised, terminable, aliased, prioritised, excludable).

ASP.NET Core already ships routing (Minimal API, controllers, `MapGroup`, `RequireHost`, route constraints), a middleware pipeline,
endpoint filters, `System.Threading.RateLimiting`, CORS, forwarded headers, host filtering, Antiforgery and Data Protection.
Rule zero applies: port only what adds value.

## Options considered

**A. Thin layer on ASP.NET Core (chosen).** Reuse the native router and pipeline; add the missing Laravel ergonomics.
**B. Standalone router and HTTP pipeline.** Rejected: duplicates Kestrel/routing, loses the ecosystem (OpenAPI, auth, MVC), huge cost.
**C. Middleware only, routing stays native.** Rejected: aliases, groups and `withoutMiddleware` must attach to routes, so the two
features are one design.

## Decision

### Packages
| Package | Contents | Depends on |
|---|---|---|
| `Naravel.Routing` | fluent registrar, groups, named routes + `IUrlGenerator`, model binding, resource routes, middleware engine, and ready-made HTTP middleware (catalog below) | `Microsoft.AspNetCore.App` framework reference |

Routing is **not** driver-based, so it does not use `Manager<TDriver,TOptions>` and needs no `Naravel.Foundation` reference. If the
throttle store later becomes a driver (after `Naravel.Cache`), that goes in a separate PDR.

### Feature mapping
| Laravel | Naravel / .NET | Status |
|---|---|---|
| `Route::get/post/put/patch/delete/options`, `match`, `any` | `IRouteRegistrar` methods, mapped onto `IEndpointRouteBuilder` | Adapted |
| `Route::group`, `prefix`, `name`, `domain`, `middleware` (nested) | `r.Prefix(..).Name(..).Domain(..).Middleware(..).Group(g => ..)` | Adapted |
| `->name()` / `route('name', [...])` | `.Name()` + `IUrlGenerator.Route(name, values)` (wraps `LinkGenerator`) | Adapted |
| `->where()` / `Route::pattern()` | `.Where(param, regex)` + global patterns, emitted as anchored route constraints | Adapted |
| Explicit route model binding | `RoutingOptions.Bind` + `bindings` middleware; no ORM dependency | Adapted |
| Implicit route model binding | Minimal API `BindAsync`/`TryParse` | Native |
| `Route::resource` / `apiResource` | Minimal API first (`ResourceHandlers`); controller mapping is a follow-up | Adapted |
| `Route::fallback`, `redirect`, `permanentRedirect` | `Fallback`, `Redirect` | Adapted |
| `Route::view` | no Blade | Rejected |
| `route:cache`, `route:list` | not needed / `route:list` later with the Console module | Rejected / Open |
| String handlers `'UserController@show'` | typed delegates and controller methods | Rejected (PHP mechanic) |
| Static `Route::` facade | not provided; the registrar is the argument of `MapNaravel`. Facades stay **Open** (own PDR) | Rejected here |

### Middleware kinds (the "all kinds like Laravel" requirement)
| Laravel kind | Naravel form |
|---|---|
| Global middleware | native `app.Use*` (runs for every request, including 404) |
| Route middleware | `.Middleware("auth", "throttle:60,1")`, a type, or an inline lambda |
| Aliases (`$middlewareAliases`) | `o.Middleware.Alias("auth", typeof(..))` |
| Groups (`web`, `api`; prepend/append/replace/remove) | `o.Middleware.Group("api", ..)` and matching mutators |
| Parameters (`name:a,b`) | `MiddlewareArguments` passed to `InvokeAsync` |
| Controller middleware | `[Middleware("auth", Only = .., Except = ..)]` attribute |
| `withoutMiddleware` | `.WithoutMiddleware(..)` on route or group (also removes group members), `[WithoutMiddleware]` on controllers |
| Priority (`$middlewarePriority`) | `o.Middleware.Priority(..)`; sorts only listed types, inside the slots they occupy |
| Terminable (`terminate()`) | `ITerminableMiddleware.TerminateAsync`, run via `Response.OnCompleted` |
| Framework `IMiddleware` classes | adapter, so existing ASP.NET middleware can be aliased |

Mechanism: a single `app.UseNaravelRouting()` reads per-endpoint metadata, resolves the ordered pipeline once per endpoint (cached) and runs it.
It works for Minimal API and controllers. Contract:
`IRouteMiddleware.InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)`.

### Ready-made HTTP middleware catalog (Stage 4b, implemented inside `Naravel.Routing`)
| Laravel | State |
|---|---|
| `bindings` (SubstituteBindings) | **Done in Stage 4a**, in `Naravel.Routing` |
| `throttle` (`throttle:60,1`, named limiters, `X-RateLimit-*`, `Retry-After`) | **Now (Stage 4b)**, on `System.Threading.RateLimiting` (in-memory) |
| `signed` (ValidateSignature) + signed URL generation | **Now (Stage 4b)**, on Data Protection |
| Maintenance mode (middleware + service) | **Now (Stage 4b)**; `down`/`up` commands wait for the Console module |
| `TrimStrings`, `ConvertEmptyStringsToNull` | **Now (Stage 4b)** for query and form; JSON bodies are out of scope for v1 |
| `cache.headers` | **Now (Stage 4b)** |
| `guest` | **Now (Stage 4b)** (uses `HttpContext.User` only) |
| `HandleCors`, `TrustProxies`, `TrustHosts`, `ValidatePostSize`, `EncryptCookies` | **Native**: CORS, `ForwardedHeaders`, `HostFiltering`, Kestrel limits, Data Protection. Nothing ported |
| `auth`, `auth.basic`, `can`, `verified`, `password.confirm` | **Later**: Auth module ships them |
| `StartSession`, `VerifyCsrfToken`, `ShareErrorsFromSession` | **Later**: Session module (CSRF is native Antiforgery until then) |
| Redis-backed throttling | **Later**: after `Naravel.Cache.Redis` |
| `AddQueuedCookiesToResponse`, Precognition | **Rejected**: little value in .NET |

### Relation to `Naravel.Queue`
`IJobMiddleware` (job pipeline) stays separate: it wraps a different context. Not merged. Sharing limiter logic is a possible later PDR.

## Sign-off items (approved)
1. A `FrameworkReference` to `Microsoft.AspNetCore.App` (not a NuGet package, but beyond `Microsoft.Extensions.*` in hard rule 8).
2. No static `Route` facade in this phase.
3. Resource routes: Minimal API first, controllers afterwards.

## Addendum - decisions made while implementing Stage 4a
1. **Groups are composed by the registrar, not by `MapGroup`.** Metadata order across nested `MapGroup` levels is not something we can rely on, so
   `Prefix/Name/Domain/Middleware` are combined inside `MapNaravel` and each route is mapped once with a single `RouteMiddlewareMetadata`. Native `MapGroup`
   still works through `WithNaravelMiddleware`; exclusions there are order-independent.
2. **The registrar is the argument of `MapNaravel(r => ...)`.** Routes are mapped when the callback returns; this makes `.Where()` and out-of-order fluent calls
   possible and lets configuration errors (unknown alias, `Where` on a missing parameter) fail at startup.
3. **Implicit binding is native, not ported** (Laravel reads PHP type hints). Explicit `Bind` + the `bindings` middleware are provided.
4. **Controller `Resource` mapping is deferred.** Minimal API resources are done; controllers already get `[Middleware]`/`[WithoutMiddleware]`.
5. **Test-only dependency:** `Microsoft.AspNetCore.TestHost` (central version in `Directory.Packages.props`). No shipped package references it.
6. Configuration is code-time (`AddNaravelRouting(o => ...)`), read once; no hot reload.
7. **Package scope amended 2026-10-04:** ready-made HTTP middleware belongs in the existing `Naravel.Routing` package, not a separate package. Stage 4b implementation work is still pending.

## Consequences
- One routing runtime project and its test project in `Naravel.slnx`; Stage 4b extends these projects. No change to Foundation, Queue, Filesystem or Cache.
- Public types carry XML docs (purpose, Laravel equivalent, why, what was not ported); docs in EN and FA.
- Tests are mandatory per stage; if `dotnet` cannot run, the stage report must say tests were not run.
