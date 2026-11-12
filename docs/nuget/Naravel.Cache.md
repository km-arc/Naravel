# Naravel.Cache

[![NuGet](https://img.shields.io/nuget/v/Naravel.Cache.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Cache)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Cache.svg)](https://www.nuget.org/packages/Naravel.Cache)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Named cache stores, tagging, scoping and locks for Naravel, built on Naravel.Foundation.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Cache` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Foundation`, which NuGet installs with it |
| **Providers** | [`Naravel.Cache.Redis`](https://www.nuget.org/packages/Naravel.Cache.Redis) and [`Naravel.Cache.Memcached`](https://www.nuget.org/packages/Naravel.Cache.Memcached), for a cache every application instance shares |

## What you get

- **Named stores.** The default store is `memory`, so it runs with no configuration; `CacheManager` picks another store by name at run time.
- **The everyday calls.** `SetAsync` with a TTL, `TryGetAsync`, `PullAsync`, counters with `IncrementAsync`, and `RememberAsync`, which loads a value on a miss and stores it.
- **Scopes and tags.** `ForUser(userId)` and `Scope(...)` prefix the keys, and `Tags("orders").FlushAsync()` clears a tag without scanning keys.
- **Locks.** `LockManager` and `BlockAsync` run a callback while holding a lock; with the Redis provider the lock is shared between instances.
- **A fixed-window rate limiter and a test fake.** `AddNaravelRateLimiter` counts attempts in a named store; `CacheFake` is an in-memory `ICacheStore` for your tests.

## Quick start

```csharp
using Naravel.Cache;
using Naravel.Cache.Abstractions;

builder.Services.AddNaravelCache(builder.Configuration);   // no configuration needed: the default store is "memory"
builder.Services.AddSingleton(TimeProvider.System);        // the clock the endpoint reads; a test registers its own

app.MapGet("/time", (ICacheStore cache, TimeProvider clock) =>
    cache.RememberAsync("server-time", TimeSpan.FromSeconds(30),
        () => Task.FromResult(clock.GetUtcNow().ToString("T"))));
```

The value stays the same for 30 seconds, then the factory runs again. Add a provider package from the list below to share one cache between several instances of the application.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Cache.Redis`](https://www.nuget.org/packages/Naravel.Cache.Redis) | Redis cache and distributed-lock providers |
| [`Naravel.Cache.Memcached`](https://www.nuget.org/packages/Naravel.Cache.Memcached) | Memcached cache and lock providers |

## Learn more

- Cache: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/cache.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
