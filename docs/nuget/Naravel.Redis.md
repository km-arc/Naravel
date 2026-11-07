# Naravel.Redis

Named, shared Redis connections for Naravel modules: one StackExchange.Redis multiplexer per name, owned and disposed by one manager.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Redis
```

Targets .NET 10. It depends on `Naravel.Foundation`, which NuGet installs with it.

## Use

```json
{ "Redis": { "Default": "main", "Stores": { "main": { "ConnectionString": "localhost:6379" } } } }
```

```csharp
using Naravel.Redis;

builder.Services.AddNaravelRedis(builder.Configuration);

app.MapPost("/visits", async (RedisManager redis) =>
{
    var db = redis.Connection().Multiplexer.GetDatabase();   // the real StackExchange.Redis IDatabase
    return Results.Ok(new { count = await db.StringIncrementAsync("visits") });
});
```

`Connection()` returns the default connection; `Connection("name")` returns another one from `Redis:Stores`. The cache and queue providers borrow the same connections.

## Documentation

- Redis: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/redis.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
