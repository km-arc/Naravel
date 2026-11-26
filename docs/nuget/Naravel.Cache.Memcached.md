# Naravel.Cache.Memcached

[![NuGet](https://img.shields.io/nuget/v/Naravel.Cache.Memcached.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Cache.Memcached)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Cache.Memcached.svg)](https://www.nuget.org/packages/Naravel.Cache.Memcached)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Memcached cache-store provider for Naravel.Cache. This package does not provide a distributed lock.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Cache.Memcached` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Cache`, which NuGet installs with it |
| **Client** | `EnyimMemcachedCore`, one shared client for every Memcached store |

## What you get

- **Memcached behind the same cache.** Name `"Driver": "memcached"` in a store, and the cache calls you already use read and write Memcached.
- **Counters with a TTL.** `IncrementAsync` and `DecrementAsync`, plus an overload that sets the expiry when it creates the counter.
- **One client for every store.** All Memcached stores share one server pool, so they must list the same servers; registration throws otherwise, and changing the list needs a restart.

## Quick start

```csharp
builder.Services.AddNaravelCache(builder.Configuration).AddNaravelMemcachedCache(builder.Configuration);
```

```json
{ "Cache": { "Default": "memcached", "Stores": { "memcached": { "Driver": "memcached", "Servers": { "primary": { "Address": "localhost", "Port": "11211" } } } } } }
```

Inject `ICacheStore` as usual; it now reads and writes Memcached. Memcached cannot delete by prefix, so `FlushAsync` on a scope or on `Store()` throws `NotSupportedException` instead of clearing the cluster; invalidate a subset with tags. `MemcachedCacheStore.FlushAsync` called on the raw driver throws as well; nothing in this package clears the cluster. Memcached is not registered as a lock provider; use `Naravel.Cache.Redis` for distributed locks.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Cache`](https://www.nuget.org/packages/Naravel.Cache) | Named stores, tags, scopes and the cache abstractions |
| [`Naravel.Cache.Redis`](https://www.nuget.org/packages/Naravel.Cache.Redis) | The Redis provider: atomic lock release, and a flush that stays inside the store's prefix |

## Learn more

- Cache: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/cache.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
