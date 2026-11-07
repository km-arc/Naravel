# Naravel.Cache.Redis

Redis cache and distributed-lock providers for Naravel.Cache.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Cache.Redis
```

Targets .NET 10. It depends on `Naravel.Cache` and `Naravel.Redis`, which NuGet installs with it.

## Use

```csharp
builder.Services.AddNaravelCache(builder.Configuration).AddNaravelRedisCache(builder.Configuration);
```

```json
{ "Cache": { "Default": "redis", "Stores": { "redis": { "Driver": "redis", "ConnectionString": "localhost:6379" } } } }
```

Inject `ICacheStore` as usual; it now reads and writes Redis. When other Naravel modules use the same Redis server, name a shared connection with `Connection` instead of `ConnectionString` (see `Naravel.Redis`).

## Documentation

- Cache: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/cache.md)
- Redis: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/redis.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
