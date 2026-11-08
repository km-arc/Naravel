# Naravel.Routing

Laravel-style routing and middleware for ASP.NET Core: fluent route groups, named routes, route model binding, resource routes and a middleware engine with aliases, groups, parameters, priority, withoutMiddleware and terminable middleware.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Routing
```

Targets .NET 10. Add [`Naravel.Routing.Cache`](https://www.nuget.org/packages/Naravel.Routing.Cache) to share the `throttle` quota between application instances through a Naravel.Cache store such as Redis.

## Use

```csharp
using Naravel.Routing;

builder.Services.AddNaravelRouting(o =>
{
    o.Middleware.Alias<EnsureTenant>("tenant");   // EnsureTenant is your own route middleware, shown below
    o.ConfigureThrottlePolicy("login", 5, TimeSpan.FromMinutes(1));
});

app.UseNaravelRouting();   // runs the route middleware; if you call UseRouting() yourself, call this after it

app.MapNaravel(r =>
{
    r.Get("/", () => "home").Name("home");
    r.Post("/login", () => Results.Ok()).Middleware("throttle:login");

    r.Prefix("api").Middleware("tenant").Group(api =>
    {
        api.Get("orders/{id}", (int id) => Results.Ok(id)).Where("id", "[0-9]+").Name("orders.show");
    });
});

public sealed class EnsureTenant : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        if (!context.Request.Headers.ContainsKey("X-Tenant"))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;   // short-circuit
            return;
        }

        await next(context);
    }
}
```

Nothing is mapped until the `MapNaravel` callback returns, so `Name`, `Where` and `Middleware` can come in any order, and a mistake such as `Where` for a parameter that is not in the URI throws at startup. The built-in middleware aliases are `throttle`, `signed`, `maintenance`, `cache.headers`, `trim`, `convert.empty` and `guest`; there is no built-in `auth`, so register your own with `o.Middleware.Alias`.

`throttle` counts attempts in the process by default, so with several instances each one has its own count. To share one count, add the `Naravel.Routing.Cache` package and call `services.AddNaravelSharedThrottle("shared")`, where `"shared"` is the name of a `Naravel.Cache` store that every instance uses (a Redis or Memcached store; a Memory store is still per process). The [routing guide](https://github.com/km-arc/Naravel/blob/main/docs/en/routing.md#throttle-behind-more-than-one-server) shows the full setup. A request over the limit gets a 429 response.

## Documentation

- Routing: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/routing.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
