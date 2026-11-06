# Naravel.Routing.Cache

A shared store for the Naravel.Routing throttle middleware, counting in a Naravel.Cache store (for example Redis) so every application instance uses one quota.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Routing.Cache
```

Targets .NET 10. It depends on `Naravel.Routing` and `Naravel.Cache`, which NuGet installs with it. Add a cache provider such as `Naravel.Cache.Redis` for a store that is shared between instances.

## Use

```csharp
builder.Services.AddNaravelCache(builder.Configuration);
builder.Services.AddNaravelRedisCache(builder.Configuration);
builder.Services.AddNaravelRouting();
builder.Services.AddNaravelSharedThrottle("redis");
```

Policies and `throttle:login` stay as they are. Without this package the throttle counts in the memory of each process, so N instances allow N times the limit.

## Documentation

- Routing: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/routing.md) · [راهنمای فارسی](https://github.com/km-arc/Naravel/blob/main/docs/fa/routing.md)
- Cache: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/cache.md) · [راهنمای فارسی](https://github.com/km-arc/Naravel/blob/main/docs/fa/cache.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
