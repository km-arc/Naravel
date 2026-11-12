# Naravel.Routing.Cache

[![NuGet](https://img.shields.io/nuget/v/Naravel.Routing.Cache.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Routing.Cache)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Routing.Cache.svg)](https://www.nuget.org/packages/Naravel.Routing.Cache)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

A shared store for the Naravel.Routing throttle middleware, counting in a Naravel.Cache store (for example Redis) so every application instance uses one quota.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Routing.Cache` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Routing` and `Naravel.Cache`, which NuGet installs with it, and ASP.NET Core (the `Microsoft.AspNetCore.App` shared framework) |
| **Providers** | a Naravel.Cache provider such as [`Naravel.Cache.Redis`](https://www.nuget.org/packages/Naravel.Cache.Redis) makes the store shared between instances |

## What you get

- **One quota for every instance.** The `throttle` middleware of Naravel.Routing counts in a Naravel.Cache store instead of in the process.
- **Nothing else changes.** Policies and `throttle:login` stay as they are.
- **Shared only if the store is.** A Redis or Memcached store is shared between instances; a Memory store is still per process.
- **The problem it solves.** Without it each process counts alone, so N instances allow N times the limit.

## Quick start

```csharp
builder.Services.AddNaravelCache(builder.Configuration);
builder.Services.AddNaravelRedisCache(builder.Configuration);   // from Naravel.Cache.Redis
builder.Services.AddNaravelRouting();
builder.Services.AddNaravelSharedThrottle("redis");
```

## Learn more

- Routing: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/routing.md)
- Cache: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/cache.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
