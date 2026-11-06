# Routing and route middleware (`Naravel.Routing`)

A thin Laravel-style layer on top of ASP.NET Core routing. It reuses the native router and pipeline and adds what Laravel developers miss:
nested groups (`prefix`, `name`, `domain`, `middleware`), `where()` constraints, named routes with `route()`-style URL generation, route model
binding, resource routes, and a full **route middleware** system (aliases, groups, parameters, priority, `withoutMiddleware`, terminable).
Design: [PDR-009](../pdr/en/PDR-009-routing-and-http-middleware.md). Persian version: [../fa/routing.md](../fa/routing.md).

> Routing status and verification are tracked in [ROADMAP.md](../../ROADMAP.md) (R05). This guide describes the implemented behavior.

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

`throttle` counts attempts in an `IThrottleStore`. The default store is the process-local `System.Threading.RateLimiting` fixed-window limiter; the separate package `Naravel.Routing.Cache` adds a store that all instances share (see below). The inline form uses `throttle:60,1` (permit limit, window in minutes); named policies are registered during setup:

```csharp
builder.Services.AddNaravelRouting(o => o.ConfigureThrottlePolicy("login", 5, TimeSpan.FromMinutes(1)));
app.MapNaravel(r => r.Post("/login", Login).Middleware("throttle:login"));
```

Partitions use the authenticated name identifier (or identity name), falling back to the remote IP, and are isolated by endpoint and policy. Responses include `X-RateLimit-Limit` and `X-RateLimit-Remaining`; rejected requests return 429 and `Retry-After`. A fixed window can admit up to 2x the limit across a short adjacent-window boundary interval. With the default store the counters live in the memory of each process; read the next section before running more than one instance.

#### Throttle behind more than one server

The default store counts in the memory of each process:

- **One quota per instance.** With N instances every caller is counted N times separately. Behind a round-robin load balancer a caller can send up to N times the limit in one window, and gets 429 from one server while another still answers 200. The limit is looser than configured, not stricter.
- **A restart resets every quota.** Counters are not persisted: a deploy, a crash, a scale-in or a scale-out starts the affected instance with empty counters, so callers get a fresh window.

Whichever store counts, the caller is identified the same way:

- **Callers behind a proxy.** An anonymous caller is identified by `RemoteIpAddress`. Behind a load balancer or reverse proxy that is the proxy's address unless you enable ASP.NET Core's forwarded headers (`UseForwardedHeaders` with the proxy listed in `KnownProxies` or `KnownNetworks`); without it every anonymous caller shares one bucket and the limit is far too strict. Naravel does not re-implement this (PDR-009).

##### Share one quota between instances (`Naravel.Routing.Cache`)

`Naravel.Routing.Cache` counts `throttle` attempts in a [Naravel.Cache](cache.md) store. Every instance that uses the same store shares one quota per policy and caller, and a restarted instance keeps it. Routing itself does not reference Cache, so nobody who only wants the in-process throttle pulls it in. The package is new and is not in the 0.1.1 packages; it ships with the next version (see [CHANGELOG.md](../../CHANGELOG.md)).

```csharp
// appsettings.json: "Cache": { "Stores": { "shared": { "Driver": "redis", "ConnectionString": "localhost:6379", "Prefix": "myapp" } } }
builder.Services.AddNaravelCache(builder.Configuration);
builder.Services.AddNaravelRedisCache(builder.Configuration);
builder.Services.AddNaravelRouting(o =>
{
    o.ConfigureThrottlePolicy("api", 120, TimeSpan.FromMinutes(1));
    o.ConfigureThrottlePolicy("login", 5, TimeSpan.FromMinutes(1), ThrottleFailureMode.FailClosed);
});
builder.Services.AddNaravelSharedThrottle("shared");
```

Policies, `throttle:login`, `throttle:60,1`, the `X-RateLimit-*` headers and the 429 response stay as they are. `AddNaravelSharedThrottle` may come before or after `AddNaravelRouting`; the last call wins. An unknown store name throws `DriverNotRegisteredException` (it lists the stores that exist) the first time the throttle is used.

How it counts:

- **One atomic call, no lock.** Each request makes one `ICacheStore.IncrementAsync` call on the named store. Nothing waits on a lock, so a burst from one caller is not queued behind itself. A rejected attempt is counted too.
- **A hashed key.** The key is `throttle:{SHA-256 of policy|endpoint|subject}:{window index}`: a fixed length, accepted by every provider (Memcached included) and without the client address in it. The counter expires with its window.
- **A window aligned to the clock.** The window index is `floor(UtcNow / window)`, so one-minute windows start at the top of each minute on every instance, not at a caller's first request. `Retry-After` is the time left in the window.
- **What that costs.** A caller can use the limit at the end of one window and again at the start of the next: up to 2x the limit in a short time, as with the default store. There is no sliding window and no token bucket. Instances need clocks within the usual NTP skew; a skew of a few milliseconds moves that instance's window boundary by that much and does nothing else. If a deploy changes a policy's window, old and new instances count in different keys (the window index differs, so no counter is ever read with the wrong meaning); until the old instances are gone each side enforces the limit on its own, so a caller can get up to the sum of both.
- **Memory is process-local.** `AddNaravelSharedThrottle("memory")` counts per process and shares nothing, like the default store; use it in development and tests. Nothing checks at run time whether a store is shared (the cache contract does not say), so name one that is. Redis is the safer choice for a critical quota; read the backend table in [cache.md](cache.md) first.
- **Maintenance mode is still process-local.** Only `throttle` has a shared store; the state of `maintenance` is not shared.

##### When the shared store fails

A throttle sits on the request path, so a slow or unavailable backend must not take the application down, and must not silently remove the limit either (Laravel lets the cache exception escape, which is a 500). Each store call is limited by `Throttle.StoreTimeout` (250 milliseconds by default). A call that throws or times out is a failure and opens one breaker for the store, shared by every policy, for `Throttle.BreakerPeriod` (5 seconds by default): the store is not called in that time. Then one request probes the store; if it answers, the breaker closes, otherwise it opens for another period. A client that went away is not a failure. While the breaker is open, each request follows its policy's `ThrottleFailureMode`:

| Mode | A request while the store is failing | Use it for |
|---|---|---|
| `DegradeToLocal` (the default) | This instance's in-process limiter, with the policy's numbers. Never unlimited, never a 500 | Most routes |
| `FailClosed` | `503` with `Retry-After` (the time the breaker stays open, at least 1 second) and no `X-RateLimit-*` headers | Credential endpoints: admitting more attempts than the shared limit is worse than refusing |
| `FailOpen` | Admitted with no limit | Only as an explicit opt-in; it removes the limit for as long as the store is down |

The mode is set per policy, as in the snippet above; the inline `throttle:60,1` form always degrades to the local limiter. Both timing numbers are options on `RoutingOptions.Throttle`, read once at startup:

```csharp
builder.Services.AddNaravelRouting(o =>
{
    o.Throttle.StoreTimeout = TimeSpan.FromMilliseconds(100);
    o.Throttle.BreakerPeriod = TimeSpan.FromSeconds(10);
});
```

Each time the breaker opens, one warning is logged with the exception. The meter `Naravel.Routing` counts `naravel.routing.throttle` (tags `policy` and `outcome`: `allowed`, `rejected` or `degraded`; a `FailClosed` refusal counts as `rejected`, a `FailOpen` admission as `allowed`) and `naravel.routing.throttle.store_failures` (tags `policy` and `reason`: `error` or `timeout`). The tags are never the caller, the address or the key. The default in-process store, which cannot fail, is counted too.

The first use of a store creates it. The package does that off the request thread, so a Redis that cannot be reached at that moment costs a request at most `StoreTimeout`.

##### Which one to use

1. Behind more than one instance, register `AddNaravelSharedThrottle`. Keep the default store where roughly N times the limit is acceptable (damping scrapers, protecting a single instance's CPU); it is not a security boundary.
2. Opt credential endpoints (logins, one-time codes, password resets) into `FailClosed`.
3. For an explicit attempt counter in a handler (failed logins per account), the Cache-backed `IRateLimiter` stays the tool: see the tested login example in [cache.md](cache.md). It takes a lock per subject and makes several store calls, so it suits attempt counters, not a blanket throttle on a busy route.
4. Or enforce the limit at the edge (reverse proxy or API gateway), where all traffic passes exactly once.

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

## Complete example

One setup block (an alias with an argument, a middleware group, a throttle policy, a global pattern and a model binding) and the routes that use it. `IBookStore` is your own data access. The `auth` alias used in the snippets above is not built in; register your own the same way as `role` here.

```csharp
// 1. A route middleware with an argument: "role:admin".
public sealed class RequireRole : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        var role = arguments.At(0) ?? "user";
        if (context.Request.Headers["X-Role"] != role)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;      // short-circuit
            return;
        }
        await next(context);
    }
}

public sealed record Book(int Id, string Title);

// 2. Program.cs: aliases, a group, a throttle policy and a model binding, declared once.
builder.Services.AddNaravelRouting(o =>
{
    o.Middleware.Alias<RequireRole>("role");
    o.Middleware.Group("api", "bindings", "throttle:60,1");
    o.ConfigureThrottlePolicy("login", 5, TimeSpan.FromMinutes(1));
    o.Pattern("id", "[0-9]+");
    o.Bind<Book>("book", async (value, ctx) => await ctx.RequestServices.GetRequiredService<IBookStore>().FindAsync(int.Parse(value)));   // null -> 404
});

var app = builder.Build();
app.UseNaravelRouting();

// 3. Routes: a group with a prefix, a name prefix and middleware; a resource; named URLs.
app.MapNaravel(r =>
{
    r.Get("/", () => "home").Name("home");
    r.Post("/login", (HttpContext c) => Results.Ok()).Middleware("throttle:login");

    r.Prefix("api").Name("api.").Middleware("api").Group(api =>
    {
        api.Get("books/{book}", (HttpContext c) => c.GetRouteModel<Book>("book")).Name("books.show");     // GET /api/books/{book}

        api.ApiResource("authors", new ResourceHandlers
        {
            Index   = () => Results.Ok(new[] { "Ada", "Alan" }),
            Show    = (string author) => Results.Ok(author),
            Destroy = (string author) => Results.NoContent(),
        });                                                                                                  // names: api.authors.index, ...

        api.Prefix("admin").Name("admin.").Middleware("role:admin").Group(admin =>
            admin.Get("stats", () => Results.Ok("stats")).Name("stats"));                                    // GET /api/admin/stats, 403 without X-Role: admin
    });

    r.Get("/links", (IUrlGenerator urls) => Results.Ok(new
    {
        book  = urls.Route("api.books.show", new { book = 7 }),                                              // "/api/books/7"
        stats = urls.Route("api.admin.stats"),
    })).Name("links");

    r.Fallback(() => Results.NotFound());
});
```

## Native middleware

CORS, proxy headers, host filtering, request-size limits and cookie encryption are native ASP.NET Core features and are not re-implemented (see PDR-009). Auth- and Session-backed middleware are native too: route authorization is `RequireAuthorization` through `NaravelRoute.Configure` and sessions are `AddSession` (PDR-016 and PDR-015, both `DECLINED`; see [auth.md](auth.md) and [session.md](session.md)). There is no Naravel Console package (PDR-017 declines it in favour of `System.CommandLine`), so there are no `route:list` or maintenance `down`/`up` commands. A shared store for `throttle` is the separate package `Naravel.Routing.Cache` (PDR-024), so Routing needs no Cache or Redis reference.

## What the tests cover (`tests/Naravel.Routing.Tests`, `tests/Naravel.Routing.Cache.Tests`)

- The guide's "Complete example" (group with prefix, name prefix and middleware, route model binding, API resource, an alias with an argument, a throttle policy, named URLs): `DocsExampleTests.cs`. Change one, change the other.
- Resolution (no HTTP): aliases, arguments, groups (nesting, cycles, mutators), exclusion, de-duplication, priority, per-endpoint caching, controller attributes.
- End to end on `TestServer`: verbs, groups and name prefixes, URL generation, `Where`/global patterns (anchoring), domains, fallback, redirects,
  middleware order, short-circuit, `WithoutMiddleware`, inline and aliased delegates, `IMiddleware` adapter, terminable, priority, native endpoints and groups,
    controller attributes (`Only`/`Except`/`WithoutMiddleware`), model binding, Minimal API and MVC resource routes, throttling, signed URLs, maintenance, cache headers, input normalization and guest middleware.
- The throttle store seam and its failure handling (`ThrottleStoreTests.cs`, `ThrottleFailureTests.cs`): a test double of `IThrottleStore` that counts calls, throws or hangs, and a manual `TimeProvider`, so no test waits for a breaker period.
- The shared store (`tests/Naravel.Routing.Cache.Tests`): one store contract on the in-process store, the Memory store, `CacheFake` and Redis; the clock-aligned window and the 2x boundary with a manual clock; one increment per request and no lock; hashed keys; two hosts sharing one quota; an outage following the policy; the falling Memory entry count; and the snippets of "Share one quota between instances" (`SharedThrottleDocsExampleTests.cs`, with a Memory store in place of Redis; change one, change the other). The Redis cases need `NARAVEL_TEST_REDIS`: they run in the provider job and show as skipped, never as passed, without it.

## Limitations of this stage

- Controller resource mapping expects conventional MVC action names and route templates; controller-specific route templates are not composed with the resource map.
- No `route:list` command: there is no Naravel Console package (PDR-017), and whether to add one over the endpoint metadata is not decided.
- Maintenance mode state is process-local. So is throttling with the default store: with N instances it admits up to N times its limit and every restart resets it; `Naravel.Routing.Cache` shares the throttle quota, not the maintenance state (see "Throttle behind more than one server").
- Signed URLs require a shared, durable Data Protection key ring for multi-instance use and are path/query-bound rather than host-bound.
- Middleware attached to a native `MapGroup` uses ASP.NET Core's metadata order; exclusions are order-independent.
- Route configuration is read once at startup (no hot reload).
