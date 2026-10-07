# Routing and route middleware (`Naravel.Routing`)

A thin Laravel-style layer on top of ASP.NET Core routing. It reuses the native router and pipeline and adds what Laravel developers miss:
nested groups (`prefix`, `name`, `domain`, `middleware`), `where()` constraints, named routes with `route()`-style URL generation, route model
binding, resource routes, and a full **route middleware** system (aliases, groups, parameters, priority, `withoutMiddleware`, terminable).
Design: [PDR-009](../pdr/en/PDR-009-routing-and-http-middleware.md). Persian version: [../fa/routing.md](../fa/routing.md).

> Stage 4a passed 64 tests on 2026-10-04. Stage 4b adds the ready-made middleware and controller resource mapping described below.

## Setup

```csharp
builder.Services.AddNaravelRouting(o =>
{
    o.Middleware.Alias<EnsureAge>("age");                 // alias -> type
    o.Middleware.Group("api", "bindings", "age:18");      // group -> aliases (with arguments)
    o.Pattern("id", "[0-9]+");                            // like Route::pattern
});

var app = builder.Build();
app.UseNaravelRouting();                                  // runs route middleware (after routing)
app.MapNaravel(r => { /* routes */ });
```

`WebApplication` adds `UseRouting()` at the start of the pipeline for you. If you call `UseRouting()` yourself, call `UseNaravelRouting()` after it.

## Declaring routes

```csharp
app.MapNaravel(r =>
{
    r.Get("/", () => "home").Name("home");
    r.Post("/orders", (OrderDto dto) => Results.Created("/orders/1", dto)).Middleware("auth", "throttle:60,1");
    r.Match(new[] { "GET", "POST" }, "/form", handler);
    r.Any("/hook", handler);
    r.Redirect("/old", "/new", permanent: true);
    r.Fallback(() => Results.NotFound());
});
```

`Get` also answers `HEAD`, like Laravel. Handlers are ordinary Minimal API delegates. Nothing is mapped until the callback returns, so the order of
`.Name()`, `.Where()`, `.Middleware()` calls does not matter, and configuration errors throw **at startup**.

### Groups

```csharp
r.Prefix("admin").Name("admin.").Middleware("auth").Group(admin =>
{
    admin.Get("users/{user}", (string user) => user).Name("users.show");      // GET /admin/users/{user}, name admin.users.show
    admin.Domain("api.example.com").Group(api => api.Get("ping", () => "pong"));
});
```

`Prefix`, `Name`, `Domain`, `Middleware`, `WithoutMiddleware` chain in any order and end with `.Group(...)`. Inner groups add to the outer ones.

### Constraints

```csharp
r.Get("/items/{id}", handler).Where("id", "[0-9]+");   // regex, anchored automatically
```
`Where` for a parameter that is not in the URI throws at startup. Global patterns: `o.Pattern("id", "[0-9]+")`. (`{id:int}` from ASP.NET Core keeps working.)

### Named routes and URLs

```csharp
IUrlGenerator urls = ...;                              // injected
urls.Route("admin.users.show", new { user = 5, tab = "x" });   // "/admin/users/5?tab=x"
urls.AbsoluteRoute(httpContext, "home");                       // "https://host/"
```
Unknown name or missing required value throws `RouteNotFoundException` (never returns `null`).

### Resource routes

```csharp
r.Resource("photos", new ResourceHandlers
{
    Index = () => ..., Create = () => ..., Store = () => ...,
    Show = (string photo) => ..., Edit = ..., Update = ..., Destroy = ...,
});
r.ApiResource("photos", handlers);                    // no create / edit
r.Resource("photos", handlers, o => { o.Only = new[] { "index", "show" }; o.Parameter = "id"; });
```
Only handlers you set become routes. Names: `photos.index`, `photos.show`, ...; the parameter is the singular of the last segment (`categories` gives `category`)
unless you set `Parameter`. Wrap them in `.Prefix()/.Name()/.Middleware()` groups as usual.

For conventional MVC controllers, map all seven actions with `MapNaravelControllerResource<TController>`:

```csharp
builder.Services.AddControllers();
var app = builder.Build();
app.MapControllers();
app.MapNaravelControllerResource<PhotosController>("photos");
```

The controller uses the `Index`, `Create`, `Store`, `Show`, `Edit`, `Update`, and `Destroy` action names. The item parameter defaults to `id`; route names are `photos.index`, `photos.show`, and so on. Actions should use conventional routing rather than their own route templates.

### Route model binding

```csharp
o.Bind<User>("user", async (value, ctx) => await db.FindUserAsync(value));   // null -> 404
r.Get("/users/{user}", (HttpContext c) => c.GetRouteModel<User>("user")!.Name).Middleware("bindings");
```
The built-in alias `bindings` resolves every parameter that has a binding. Implicit binding by type hint is a PHP reflection mechanic and is **not ported**;
in .NET give your type a static `BindAsync`/`TryParse` (native Minimal API).

## Middleware

### Ready-made middleware

The built-in route aliases are `throttle`, `signed`, `maintenance`, `cache.headers`, `trim`, `convert.empty`, and `guest`.

`throttle` uses the process-local `System.Threading.RateLimiting` fixed-window limiter. The inline form uses `throttle:60,1` (permit limit, window in minutes); named policies are registered during setup:

```csharp
builder.Services.AddNaravelRouting(o => o.ConfigureThrottlePolicy("login", 5, TimeSpan.FromMinutes(1)));
app.MapNaravel(r => r.Post("/login", Login).Middleware("throttle:login"));
```

Partitions use the authenticated name identifier (or identity name), falling back to the remote IP, and are isolated by endpoint and policy. Responses include `X-RateLimit-Limit` and `X-RateLimit-Remaining`; rejected requests return 429 and `Retry-After`. This is process-local and does not share quota across app instances. A fixed window can admit up to 2x the limit across a short adjacent-window boundary interval.

`IUrlGenerator.SignedRoute(name, values)` and `TemporarySignedRoute(name, expiresAt, values)` create signed relative URLs. Attach `.Middleware("signed")` to validate them. The signature covers the path and query, not the host. Temporary URLs are rejected once expired. Data Protection keys must be persisted and shared by instances that generate and validate the same links; protect the key ring at rest in production.

`MaintenanceModeService` is a process-local switch. Enable it through DI; matching requests return 503 and `Retry-After`. An optional bypass secret is supplied in the `X-Naravel-Maintenance-Bypass` header. It has no CLI commands and does not coordinate state across instances.

`cache.headers:public,max-age=60` sets validated `Cache-Control` directives. `trim` trims query and form values; `convert.empty` turns empty query/form values into null. JSON request bodies are intentionally untouched. `guest` allows anonymous requests and returns 403 for authenticated users; it does not redirect to an Auth destination.

Benchmark the route middleware invocation path (one `DefaultHttpContext` and one pass-through route middleware) in Release mode:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*InvokeRouteMiddlewarePipeline*' --job short
```

### Writing one

```csharp
public sealed class EnsureAge : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        var min = arguments.Int(0, 18);                       // "age:21" -> 21
        if (!AgeOk(context, min)) { context.Response.StatusCode = 403; return; }   // short-circuit
        await next(context);
    }
}
```
Instances come from DI when registered, otherwise they are created with `ActivatorUtilities` (constructor injection works). Per-request scope applies.

### Every kind, Laravel to Naravel

| Laravel | Naravel |
|---|---|
| Global middleware | plain `app.Use*` (also runs for 404s) |
| Route middleware | `.Middleware("auth", "throttle:60,1")`, `.Middleware(typeof(X), "arg")`, `.Middleware(async (ctx, next, args) => ...)` |
| Aliases | `o.Middleware.Alias("auth", typeof(X))` or `Alias("x", async (ctx, next, args) => ...)` |
| Groups | `o.Middleware.Group("api", ...)`, `PrependToGroup`, `AppendToGroup`, `ReplaceInGroup`, `RemoveFromGroup` |
| Parameters | `name:a,b` gives `MiddlewareArguments` (`At(i)`, `Int(i, fallback)`) |
| Controller middleware | `[Middleware("auth", Only = new[]{"Index"}, Except = ...)]` on a controller or action |
| `withoutMiddleware` | `.WithoutMiddleware("auth")` / `(typeof(X))` on a route or group; `[WithoutMiddleware("auth")]` on controllers |
| Priority | `o.Middleware.Priority(typeof(A), typeof(B))` |
| Terminable | also implement `ITerminableMiddleware`; runs after the response via `OnCompleted` |
| Framework `IMiddleware` | usable as an alias target (arguments are ignored) |
| Native endpoints | `app.MapGet(...).WithNaravelMiddleware("auth")` / `.WithoutNaravelMiddleware("auth")` |

Rules worth knowing:
- A group name wins over an alias with the same name; groups may nest (16 levels max, cycles throw).
- Exclusion is by **type**: `WithoutMiddleware("throttle")` removes every `throttle:*` reference and every alias pointing to the same type.
- Duplicates (same type and same arguments) run once; different arguments run separately.
- Priority sorts only the listed types, inside the slots they already occupy (Laravel's algorithm differs slightly).
- An unknown alias or group throws when routes are mapped, not on the first request.
- Terminable callbacks run after the response is sent; when several exist their order is not guaranteed.
- Class-name strings (`'App\Http\Middleware\X'`) are not supported; use an alias or a type.

## Native middleware

CORS, proxy headers, host filtering, request-size limits and cookie encryption are native ASP.NET Core features and are not re-implemented (see PDR-009). Auth- and Session-backed middleware, Redis-backed throttling, `route:list`, and maintenance CLI commands belong to later modules.

## What the tests cover (`tests/Naravel.Routing.Tests`)

- Resolution (no HTTP): aliases, arguments, groups (nesting, cycles, mutators), exclusion, de-duplication, priority, per-endpoint caching, controller attributes.
- End to end on `TestServer`: verbs, groups and name prefixes, URL generation, `Where`/global patterns (anchoring), domains, fallback, redirects,
  middleware order, short-circuit, `WithoutMiddleware`, inline and aliased delegates, `IMiddleware` adapter, terminable, priority, native endpoints and groups,
    controller attributes (`Only`/`Except`/`WithoutMiddleware`), model binding, Minimal API and MVC resource routes, throttling, signed URLs, maintenance, cache headers, input normalization and guest middleware.

## Limitations of this stage

- Controller resource mapping expects conventional MVC action names and route templates; controller-specific route templates are not composed with the resource map.
- No `route:list` command yet (needs the Console module).
- Maintenance mode state and throttling are process-local; use shared infrastructure if instances need coordinated state.
- Signed URLs require a shared, durable Data Protection key ring for multi-instance use and are path/query-bound rather than host-bound.
- Middleware attached to a native `MapGroup` uses ASP.NET Core's metadata order; exclusions are order-independent.
- Route configuration is read once at startup (no hot reload).
