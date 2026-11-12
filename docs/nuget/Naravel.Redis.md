# Naravel.Redis

[![NuGet](https://img.shields.io/nuget/v/Naravel.Redis.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Redis)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Redis.svg)](https://www.nuget.org/packages/Naravel.Redis)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Named, shared Redis connections for Naravel modules: one StackExchange.Redis multiplexer per name, owned and disposed by one manager.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Redis` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Foundation`, which NuGet installs with it (StackExchange.Redis comes with the package) |
| **Used by** | [`Naravel.Cache.Redis`](https://www.nuget.org/packages/Naravel.Cache.Redis) and [`Naravel.Queue.Redis`](https://www.nuget.org/packages/Naravel.Queue.Redis) borrow their connection from it |

## What you get

- **Named connections.** Each store under `Redis:Stores:<name>` is a `ConnectionString`; `Connection()` returns the default and `Connection("name")` another.
- **The real StackExchange.Redis.** You get the `IConnectionMultiplexer`; commands are never wrapped.
- **One owner.** One multiplexer per name, owned and disposed by one manager.

## Quick start

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

## Learn more

- Redis: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/redis.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
