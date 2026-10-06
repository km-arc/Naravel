# Cache

`Naravel.Cache` provides named cache stores, per-user/tenant scoping, tag-version invalidation and token-owned locks. It builds on `Naravel.Foundation`; Redis and Memcached providers are opt-in packages.

## Quickstart

```csharp
builder.Services.AddNaravelCache(configuration).AddNaravelRedisCache(configuration);
builder.Services.AddNaravelRateLimiter("redis");
var decision = await limiter.AttemptAsync("login", subjectKey, 5, TimeSpan.FromMinutes(1), cancellationToken);
```

## Register providers

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddNaravelCache(builder.Configuration)
    .AddNaravelRedisCache(builder.Configuration)
    .AddNaravelMemcachedCache(builder.Configuration);

var app = builder.Build();
app.Run();
```

Register the core package before backend providers. Add only providers used by the application. `AddNaravelCache` binds the `Cache` section by default; pass a section name as the second argument to use another section.

## Configuration

```json
{
  "Cache": {
    "Default": "memory",
    "AppPrefix": "orders-api",
    "Stores": {
      "memory": {
        "Driver": "memory",
        "Prefix": "orders-api:memory"
      },
      "redis": {
        "Driver": "redis",
        "ConnectionString": "localhost:6379",
        "Database": "0",
        "Prefix": "orders-api:redis"
      },
      "memcached": {
        "Driver": "memcached",
        "Prefix": "orders-api:memcached",
        "Servers": {
          "primary": { "Address": "localhost", "Port": "11211" }
        }
      }
    }
  }
}
```

Use environment variables or a secret provider for credentials. Store names are case-insensitive. `Default` defaults to `memory`; `AppPrefix` defaults to `app`. A provider registers the configured store names at startup. Changes to values in an existing store are reloaded by Foundation; adding a new store after startup requires registering/restarting the host.

The `Prefix` is applied to raw keys before they reach the backend. Redis flush is restricted to that prefix. Memcached has no prefix-scoped delete operation; see limitations below.

Named Memory stores use their store name as the default prefix when `Prefix` is omitted, so they remain isolated despite sharing the process memory cache. For connection-pool efficiency, all configured Memcached stores use one client and must declare the same server list. Changing that server list requires a host restart; other store settings continue to follow Foundation's reload behavior.

## Basic operations

Inject `ICacheStore` for the default application-scoped store:

```csharp
using Naravel.Cache.Abstractions;

public sealed class SettingsService(ICacheStore cache)
{
    public Task SetAsync(string key, string value, CancellationToken cancellationToken) =>
        cache.SetAsync(key, value, TimeSpan.FromMinutes(10), cancellationToken);

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var (found, value) = await cache.TryGetAsync<string>(key, cancellationToken);
        return found ? value : null;
    }
}
```

`RememberAsync` loads on a miss and stores the result:

```csharp
var settings = await cache.RememberAsync(
    "site-settings",
    TimeSpan.FromMinutes(10),
    ct => settingsRepository.LoadAsync(ct),
    cancellationToken);
```

Other operations include `ExistsAsync`, `RemoveAsync`, `PullAsync`, `IncrementAsync`, `DecrementAsync`, and `FlushAsync`. `FlushAsync` has backend-specific scope; see limitations.

## Select a store

Use `CacheManager` when choosing a store at runtime:

```csharp
using Naravel.Cache;

public sealed class ReportCache(CacheManager cache)
{
    public ICacheStore RedisStore() => cache.Store("redis");
}
```

The manager inherits Foundation's runtime `Extend`, config reload, store caching, case-insensitive names and disposal behavior. Factories should return instances the manager may own and dispose.

A custom cache store can be registered after startup:

```csharp
cacheManager.Extend("custom", _ => new MyCacheStore("custom"));
var custom = cacheManager.Store("custom");
```

A custom store that supports locks must be registered separately with `LockManager.Extend`; the cache and lock contracts are intentionally independent.

## Scopes and tags

Scope keys explicitly to a user or tenant; no ambient HTTP identity is read:

```csharp
var userCache = cacheManager.ForUser(userId);
await userCache.SetAsync("cart-count", 3, TimeSpan.FromMinutes(5), cancellationToken);

var tenantCache = cacheManager.Scope($"tenant:{tenantId}");
await tenantCache.Tags("orders", "recent").SetAsync("last-order", order, ttl, cancellationToken);
await tenantCache.Tags("orders").FlushAsync(cancellationToken);
```

Tagging uses version counters stored in the underlying cache. Flushing a tag increments its version, making previous values unreachable without scanning keys. Old values may remain allocated until their TTL expires or the backend evicts them. `CacheManager.FlushTagAsync(tag, storeName)` flushes the tag in the application scope.

## Locks

Inject `LockManager` to select a lock provider independently from the cache store:

```csharp
using Naravel.Cache;

public sealed class OrderProcessor(LockManager locks)
{
    public Task<bool> ProcessAsync(string orderId, CancellationToken cancellationToken)
    {
        var cacheLock = locks.Driver("redis");
        return cacheLock.BlockAsync(
            $"process-order:{orderId}",
            ttl: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            callback: async ct => await ProcessOrderAsync(orderId, ct),
            cancellationToken);
    }
}
```

Every successful acquisition returns a unique token; only that token can release the lock. Redis uses atomic `SET NX` acquisition and compare-and-delete release. The memory lock is process-local. Memcached acquisition is atomic, but release has a small read/delete race; use Redis for critical distributed sections.

## Provider behavior and limitations
## Rate limiting

Register a fixed-window limiter against a named cache store:

```csharp
builder.Services.AddNaravelRateLimiter("redis");
var decision = await limiter.AttemptAsync("login", subjectKey, 5, TimeSpan.FromMinutes(1), cancellationToken);
```

Inject `IRateLimiter` where it is used. Each policy and subject has an isolated bucket; subject keys
are hashed before being placed in cache keys. The first permitted attempt starts the fixed window.
Rejected attempts do not increment the counter or extend the window. `RateLimitDecision` exposes
`Allowed`, `Remaining`, and `RetryAfter`; `ClearAsync` resets one policy/subject bucket. Use
`System.Threading.RateLimiting` for process-local policies and Cache-backed limiting only when all
instances share a backend whose lock implementation provides the required coordination. Memory locks
are process-local; Memcached lock release has a read/delete race, so Redis is the safer choice for
critical shared quotas.

`ICacheStore.IncrementAsync(key, by, ttl)` atomically increments a counter and only applies TTL when
the key is created. Redis performs increment and first-expiry assignment in one Lua operation; Memory
coordinates with its per-key lock; Memcached uses atomic add followed by atomic increment. Later
increments never slide the fixed-window boundary.

The limiter emits `naravel.cache.ratelimiter.allowed`, `naravel.cache.ratelimiter.rejected`, and a
duration histogram through the `Naravel.Cache` meter, plus an `ActivitySource` span. Metric tags use
only limiter name and outcome, never the subject key.

## Provider behavior and limitations

| Provider | Value handling | Lock behavior | Flush behavior |
|---|---|---|---|
| Memory | Typed in-process values | Process-local token locks | Removes keys tracked by that store instance |
| Redis | JSON via `System.Text.Json` | Distributed; token release is atomic | Scans/deletes only keys under the configured prefix |
| Memcached | Typed values via the configured Enyim transcoder; `ttl: null` means no expiry | Distributed acquisition; release is not fully atomic | **Flushes the entire shared Memcached cluster**, not only this store prefix |

Use tag invalidation instead of `FlushAsync` when using Memcached or when only a subset of keys should be invalidated. The existing Memcached increment/decrement operations still require existing numeric values; the TTL increment overload uses atomic add for first creation. The current Enyim client exposes counter commands synchronously; the provider schedules them on the thread pool, and cancellation cannot interrupt a command after it starts.

Memory state is not shared across app instances. Redis is the distributed option for multi-server cache and locks.

`tests/Naravel.Cache.Tests` includes a shared store contract for Memory, Redis and Memcached, covering atomic increments with first-write-only TTL. Rate-limiter tests cover fixed-window boundaries, clearing, named-store isolation, cancellation, Fake behavior and Meter/Activity output. Set
`NARAVEL_TEST_REDIS=host:port` or `NARAVEL_TEST_MEMCACHED=host:port` to run the corresponding live
provider test; without the variable, xUnit skips it. CI runs both against service containers.
