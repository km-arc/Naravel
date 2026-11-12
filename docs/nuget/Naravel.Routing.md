# Naravel.Routing

[![NuGet](https://img.shields.io/nuget/v/Naravel.Routing.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Routing)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Routing.svg)](https://www.nuget.org/packages/Naravel.Routing)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Laravel-style routing and middleware for ASP.NET Core: fluent route groups, named routes, route model binding, resource routes and a middleware engine with aliases, groups, parameters, priority, withoutMiddleware and terminable middleware.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Routing` |
| **Targets** | .NET 10 |
| **Needs** | ASP.NET Core (the `Microsoft.AspNetCore.App` shared framework) and no other Naravel package |
| **Add-on** | [`Naravel.Routing.Cache`](https://www.nuget.org/packages/Naravel.Routing.Cache) shares the `throttle` quota between application instances |

## What you get

- **Fluent routes on Minimal APIs.** Groups (`Prefix`, `Name`, `Domain`, `Middleware`), named routes with a URL generator, `Where` constraints, resource routes and route model binding.
- **Mistakes fail at startup.** Nothing is mapped until the `MapNaravel` callback returns, so the order of `Name`, `Where` and `Middleware` does not matter, and a `Where` for a parameter that is not in the URI throws when the application starts.
- **Ready-made middleware.** `throttle`, `signed`, `maintenance`, `cache.headers`, `trim`, `convert.empty` and `guest`; your own `IRouteMiddleware` classes register under an alias.
- **A throttle that scales out.** It counts in the process by default, and `Naravel.Routing.Cache` makes every instance share one count.
- **Easy to test.** A route middleware is a plain class you can call without a host, and `TestServer` runs the real pipeline in memory.

## Quick start

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

There is no built-in `auth` middleware, so register your own with `o.Middleware.Alias`.

`throttle` counts attempts in the process by default, so with several instances each one has its own count. To share one count, add the `Naravel.Routing.Cache` package and call `services.AddNaravelSharedThrottle("shared")`, where `"shared"` is the name of a `Naravel.Cache` store that every instance uses (a Redis or Memcached store; a Memory store is still per process). The [routing guide](https://github.com/km-arc/Naravel/blob/main/docs/en/routing.md#throttle-behind-more-than-one-server) shows the full setup. A request over the limit gets a 429 response.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Routing.Cache`](https://www.nuget.org/packages/Naravel.Routing.Cache) | A throttle store that every application instance shares, counting in a Naravel.Cache store such as Redis |

## Learn more

- Routing: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/routing.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
