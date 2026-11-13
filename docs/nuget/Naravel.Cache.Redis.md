# Naravel.Cache.Redis

[![NuGet](https://img.shields.io/nuget/v/Naravel.Cache.Redis.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Cache.Redis)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Cache.Redis.svg)](https://www.nuget.org/packages/Naravel.Cache.Redis)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Redis cache and distributed-lock providers for Naravel.Cache.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Cache.Redis` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Cache` and `Naravel.Redis`, which NuGet installs with it |
| **Client** | StackExchange.Redis, through the connections that `Naravel.Redis` owns |

## What you get

- **One cache for every instance.** Name `"Driver": "redis"` in a store, and all instances read and write the same keys.
- **Distributed locks.** Acquisition is an atomic `SET NX`, and release compares the token before it deletes.
- **Flush stays inside the store.** `FlushAsync` removes only the keys under the store's `Prefix`.
- **Counters in one step.** `IncrementAsync` with a TTL sets the first expiry in the same Lua operation.
- **One connection per server.** The store and its lock borrow the connection that `Naravel.Redis` owns, and equal connection strings share one.

## Quick start

```csharp
builder.Services.AddNaravelCache(builder.Configuration).AddNaravelRedisCache(builder.Configuration);
```

```json
{ "Cache": { "Default": "redis", "Stores": { "redis": { "Driver": "redis", "ConnectionString": "localhost:6379" } } } }
```

Inject `ICacheStore` as usual; it now reads and writes Redis. When other Naravel modules use the same Redis server, name a shared connection with `Connection` instead of `ConnectionString` (see `Naravel.Redis`).

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Cache`](https://www.nuget.org/packages/Naravel.Cache) | Named stores, tags, scopes and locks that this provider backs |
| [`Naravel.Redis`](https://www.nuget.org/packages/Naravel.Redis) | Named, shared Redis connections that the store borrows |

## Learn more

- Cache: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/cache.md)
- Redis: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/redis.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
