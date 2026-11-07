# Naravel.Cache

Named cache stores, tagging, scoping and locks for Naravel, built on Naravel.Foundation.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Cache
```

Targets .NET 10. It depends on `Naravel.Foundation`, which NuGet installs with it.

## Use

```csharp
using Naravel.Cache;
using Naravel.Cache.Abstractions;

builder.Services.AddNaravelCache(builder.Configuration);   // no configuration needed: the default store is "memory"

app.MapGet("/time", (ICacheStore cache) =>
    cache.RememberAsync("server-time", TimeSpan.FromSeconds(30),
        () => Task.FromResult(DateTime.UtcNow.ToString("T"))));
```

The value stays the same for 30 seconds, then the factory runs again. Add a provider package from the list below to share one cache between several instances of the application.

## Related packages

- [`Naravel.Cache.Redis`](https://www.nuget.org/packages/Naravel.Cache.Redis): Redis cache and distributed-lock providers for Naravel.Cache.
- [`Naravel.Cache.Memcached`](https://www.nuget.org/packages/Naravel.Cache.Memcached): Memcached cache and lock providers for Naravel.Cache.

## Documentation

- Cache: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/cache.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
