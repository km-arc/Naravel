# Naravel.Cache.Memcached

Memcached cache and lock providers for Naravel.Cache.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Cache.Memcached
```

Targets .NET 10. It depends on `Naravel.Cache`, which NuGet installs with it.

## Use

```csharp
builder.Services.AddNaravelCache(builder.Configuration).AddNaravelMemcachedCache(builder.Configuration);
```

```json
{ "Cache": { "Default": "memcached", "Stores": { "memcached": { "Driver": "memcached", "Servers": { "primary": { "Address": "localhost", "Port": "11211" } } } } } }
```

Inject `ICacheStore` as usual; it now reads and writes Memcached. Memcached has no delete by prefix, and the Cache guide lists what that means for flushing.

## Documentation

- Cache: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/cache.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
