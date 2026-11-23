# Cache

`Naravel.Cache` provides named cache stores, per-user/tenant scoping, tag-version invalidation and token-owned locks. It builds on `Naravel.Foundation`; Redis and Memcached providers are opt-in packages.

## Quickstart

Add the package, register it and inject `ICacheStore`. With no configuration at all you get an in-memory store, which is enough to try everything on this page.

```sh
dotnet add package Naravel.Cache
```

```csharp
using Naravel.Cache;
using Naravel.Cache.Abstractions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelCache(builder.Configuration);

var app = builder.Build();

app.MapGet("/time", (ICacheStore cache) =>
    cache.RememberAsync("server-time", TimeSpan.FromSeconds(30),
        () => Task.FromResult(DateTime.UtcNow.ToString("T"))));

app.Run();
```

Reload `/time`: the value stays the same for 30 seconds, then the factory runs again.

To share one cache between several instances of the application, add the Redis provider and make it the default store (`"Default": "redis"` in the [configuration](#configuration)):

```csharp
builder.Services.AddNaravelCache(builder.Configuration).AddNaravelRedisCache(builder.Configuration);
builder.Services.AddNaravelRateLimiter("redis");   // optional: the login throttle further down, counted in that store
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

The `Prefix` is applied to raw keys before they reach the backend. Redis flush is restricted to that prefix and leaves the keys of locks (`<Prefix>:lock:<name>`) alone. Memcached has no prefix-scoped delete operation; see limitations below.

Named Memory stores use their store name as the default prefix when `Prefix` is omitted, so they remain isolated despite sharing the process memory cache. For connection-pool efficiency, all configured Memcached stores use one client and must declare the same server list. Changing that server list requires a host restart; other store settings continue to follow Foundation's reload behavior.

## Basic operations

Inject `ICacheStore` for the default application-scoped store. This service is a `ScopedCacheStore` view over the manager-owned default driver, not the driver instance itself; `CacheManager` owns and disposes the underlying driver. Direct `ICacheLock` injection resolves the actual default lock driver through Foundation's ownership-aware registration, so DI disposes that instance and `LockManager` skips it:

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

Every operation, and what it returns:

| Method | Returns | What it does |
|---|---|---|
| `TryGetAsync<T>(key, ct)` | `(bool Found, T? Value)` | Reads a value. `Found` tells a missing key apart from a stored `default`. |
| `SetAsync<T>(key, value, ttl, ct)` | `Task` | Stores a value. `ttl: null` means no expiry. A `ttl` of zero or less means the value is already expired: nothing is stored and any value under the key is removed. |
| `ExistsAsync(key, ct)` | `bool` | Whether a live value is stored. |
| `RemoveAsync(key, ct)` | `bool` | Deletes a key; `true` when it existed. |
| `PullAsync<T>(key, ct)` | `T?` | Reads and deletes in one call; `default` when the key is missing. |
| `RememberAsync<T>(key, ttl, factory, ct)` | `T` | Returns the stored value, or runs `factory`, stores its result and returns it. |
| `RememberForeverAsync<T>(key, factory, ct)` | `T` | The same, with no expiry. |
| `IncrementAsync(key, by, ct)` and `IncrementAsync(key, by, ttl, ct)` | `long` | Adds `by` to a counter and returns the new value. The `ttl` overload sets the expiry only when it creates the key. |
| `DecrementAsync(key, by, ct)` | `long` | Subtracts `by` and returns the new value. |
| `FlushAsync(ct)` | `Task` | Removes the keys of this view: everything under the application prefix on `Store()`, and only that scope and the scopes nested under it on `ForUser(...)` and `Scope(...)`. See [Flushing a scope](#flushing-a-scope) and [limitations](#provider-behavior-and-limitations). |

`PullAsync`, `RememberAsync` and `RememberForeverAsync` are extension methods in the `Naravel.Cache` namespace, so add `using Naravel.Cache;` next to `using Naravel.Cache.Abstractions;`. `RememberAsync` takes a factory with or without a `CancellationToken`: `() => ...` or `ct => ...`.

```csharp
await cache.SetAsync("greeting", "hello", TimeSpan.FromMinutes(10), ct);

var (found, value) = await cache.TryGetAsync<string>("greeting", ct);   // found == true, value == "hello"
var removed = await cache.RemoveAsync("greeting", ct);                  // true: it existed
var removedAgain = await cache.RemoveAsync("greeting", ct);             // false: it is gone

var visits = await cache.IncrementAsync("visits:home", 1, TimeSpan.FromHours(1), ct);   // 1, then 2, then 3 ...
var stock = await cache.IncrementAsync("stock:42", 10, ct);                              // 10
stock = await cache.DecrementAsync("stock:42", 3, ct);                                   // 7

var rates = await cache.RememberForeverAsync("exchange-rates", () => LoadRatesAsync(ct), ct);   // loaded once; LoadRatesAsync stands for your own method

await cache.SetAsync("flash:message", "saved", TimeSpan.FromMinutes(1), ct);
var message = await cache.PullAsync<string>("flash:message", ct);       // "saved"; the key is now gone
```

Three things to know before you rely on these:

- **`RememberAsync` does not lock.** If two requests miss the same key at the same moment, both run the factory and the last write wins. For an expensive or side-effecting factory, wrap the call in a [lock](#locks).
- **Counters that should expire need the `ttl` overload.** On the Memory store the plain `IncrementAsync(key, by, ct)` keeps the counter with no expiry.
- **A TTL of zero or less removes the key; only `null` means no expiry.** `SetAsync` with such a TTL stores nothing and removes the value that was under the key, in every store and in `CacheFake`, so a lifetime worked out as "time until a deadline" ends in "not cached" once the deadline has passed. A configured TTL of zero switches caching off for that key without an error, so check configured values where you read them. The `IncrementAsync` overload with a `ttl` is different: it still throws for a `ttl` of zero or less.


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

A store registered with an asynchronous factory (a custom store that must connect before first use) cannot be created by `Store()`, which is synchronous. Create it once with `DriverAsync`, for example at startup; after that `Store` returns it:

```csharp
cacheManager.Extend("remote", async (sp, ct) =>
{
    var store = new MyRemoteCacheStore("remote");
    await store.ConnectAsync(ct);
    return store;
});

await cacheManager.DriverAsync("remote", cancellationToken);   // creates and caches the store
var remote = cacheManager.Store("remote");                     // now works; before DriverAsync it throws
```

Calling `Store("remote")` before `DriverAsync` throws `InvalidOperationException`: `Driver 'remote' is registered with an asynchronous factory and has not been created yet. Use DriverAsync to create it.` `DriverAsync` returns the raw store, without the application prefix; use `Store`, `ForUser` or `Scope` for the view your code should read and write. The built-in Memory, Redis and Memcached stores are synchronous, so they need none of this.

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

A key is joined to the prefix of its scope with a colon and nothing else, the way Laravel joins a cache prefix and a key. `cacheManager.Store().SetAsync("user:7:profile", ...)` and `cacheManager.ForUser(7).SetAsync("profile", ...)` therefore write the same entry. A view only writes keys that start with its own prefix, so it never reaches a sibling scope (`ForUser(7)` cannot reach `ForUser(70)`) or a key outside its prefix; the overlap is between a view and the scopes nested under it. Keep the keys of an outer view free of the names of the scopes nested under it.

### Flushing a scope

`FlushAsync` on a scoped view removes the keys of that scope and nothing else: `cacheManager.ForUser(7).FlushAsync()` leaves every other user, every tenant and every key outside the scope alone. A scope also covers the scopes nested under it (flushing `Scope("tenant:a")` also removes `Scope("tenant:a:sub")`), and it does not reach a sibling whose name only starts with the same characters (user `1` does not reach user `10`). `Store().FlushAsync()` removes everything under the application prefix, which includes every user and tenant scope; a key that was written through the raw driver outside that prefix survives. `FlushPrefixAsync(keyPrefix)` on a scoped view removes only the keys of that scope that start with the prefix, matched literally.

This needs a store that implements `IPrefixFlushableCacheStore`: Memory, Redis and `CacheFake` do. Memcached cannot delete by prefix, so `FlushAsync` through the manager, and `MemcachedCacheStore.FlushAsync` on the raw driver, throw `NotSupportedException` instead of clearing the whole cluster; invalidate with tags there. A store that you write opts in by implementing the interface, and a type that wraps a store has to forward it. An empty prefix is rejected with `ArgumentException`, because it would match every key.

The call is complete-or-fail but not atomic. If it throws, some keys may already be gone and calling it again is safe, and a key written while it runs may survive. On Redis it scans every primary, so its cost grows with the number of keys in the database, and it throws `InvalidOperationException`, without removing anything, when a primary is not connected. Use tags for invalidation on a hot path. `tests/Naravel.Cache.Tests/ScopedFlushTests.cs` runs these cases, against real Redis and Memcached servers in CI.

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

How `BlockAsync` behaves, so you can size the two durations:

- It tries to take the lock, and while it is held by someone else it retries about every 150 ms until `wait` has passed. It then returns `false` and the callback does not run. `TryRunAsync(name, ttl, callback)` is the same without waiting: one attempt, `false` when the lock is taken.
- When it returns `true`, the callback has run. The lock is released afterwards even if the callback throws; the exception then reaches your code.
- `ttl` is the lock's lifetime, and it is not renewed. If the callback runs longer than `ttl`, the lock expires and another caller can take it. Choose a `ttl` longer than the slowest run you expect.
- `ReleaseAsync(name, token)` returns `false` when the token is not the current owner's, for example after the lock expired and someone else took it. It does not throw.

### Extending a lease and clearing a stuck lock

A lock is a lease: when `ttl` ends, the lock ends, whether or not its holder is still working. Two optional capabilities exist for the two situations that follow. They are separate interfaces in `Naravel.Cache.Abstractions`, so `ICacheLock` and custom lock drivers are unchanged. Ask the driver whether it has one; the Memory and Redis locks have both, and the Memcached lock has neither, because its `touch` command does not check an owner.

```csharp
using Naravel.Cache.Abstractions;

var cacheLock = locks.Driver("redis");
if (cacheLock is not IRefreshableCacheLock refreshable)
{
    throw new NotSupportedException(
        "The selected lock provider does not support lease refresh.");
}

var token = await cacheLock.AcquireAsync("import:nightly", TimeSpan.FromMinutes(1));
if (token is null) return;

// Run the protected work, then refresh before the current lease expires.
if (!await refreshable.RefreshAsync("import:nightly", token, TimeSpan.FromMinutes(1)))
{
    throw new InvalidOperationException(
        "The lock lease was lost. Stop the protected operation.");
}
```

- `IRefreshableCacheLock.RefreshAsync(name, token, ttl)` (Laravel's `Lock::refresh()`) sets the lease to `ttl` from now. It does not add `ttl` to what was left. It returns `true` only while the token owns a lease that has not ended; it returns `false`, changing nothing, for a token that does not own the lock, for a lease that has already ended (even when nobody took the lock since, so a late refresh never revives it) and for a lock that does not exist. A `ttl` of zero or less throws `ArgumentOutOfRangeException`. Its cancellation token is checked before the Redis command is sent; once StackExchange.Redis has been given the command, later cancellation cannot stop the server-side operation, which may still complete.
- `IForceReleasableCacheLock.ForceReleaseAsync(name)` (Laravel's `Lock::forceRelease()`) deletes the lock whoever owns it and returns `true` when a lock that had not expired was deleted. The previous owner's token then neither releases nor refreshes anything. It is an operator tool for a lock whose holder crashed; calling it while a holder is still working lets another process in at once, so do not use it in normal release logic.
- There is no automatic renewal. A renewal that nobody has to ask for would turn a holder that hangs into a lock that never ends, so you decide when to extend and for how long. Even a refreshed lease does not stop a paused holder from outliving it: the lock keeps two workers apart only while each one finishes inside its lease. For a resource that must never see two writers, the protected resource has to check a fencing token; that is not part of this package yet.


## Rate limiting

Register a fixed-window limiter against a named cache store:

```csharp
builder.Services.AddNaravelRateLimiter("redis");
var decision = await limiter.AttemptAsync("login", subjectKey, 5, TimeSpan.FromMinutes(1), cancellationToken);
```

Inject `IRateLimiter` where it is used. Each policy and subject has an isolated bucket; subject keys
are hashed before being placed in cache keys. The first permitted attempt starts the fixed window.
Every attempt, rejected ones included, adds one to the counter, but none of them extends the window.
`RateLimitDecision` exposes `Allowed`, `Remaining`, and `RetryAfter`; `ClearAsync` resets one
policy/subject bucket. Use `System.Threading.RateLimiting` for process-local policies and Cache-backed
limiting only when all instances share a backend counter (Redis or Memcached); the Memory store counts
per process.

Complete example: throttle a login endpoint to 5 attempts per minute per IP and account. `IAuthService` and `LoginRequest` stand for your own code.

```csharp
using Naravel.Cache.RateLimiting;

// Program.cs: a shared Redis store, so every instance counts the same attempts.
builder.Services.AddNaravelCache(builder.Configuration).AddNaravelRedisCache(builder.Configuration);
builder.Services.AddNaravelRateLimiter("redis");

app.MapPost("/login", async (LoginRequest body, HttpContext http, IRateLimiter limiter, IAuthService auth, CancellationToken ct) =>
{
    var subject = $"{http.Connection.RemoteIpAddress}:{body.Email.ToLowerInvariant()}";   // one bucket per IP and account

    var decision = await limiter.AttemptAsync("login", subject, 5, TimeSpan.FromMinutes(1), ct);
    if (!decision.Allowed)
    {
        http.Response.Headers.RetryAfter = ((int)Math.Ceiling(decision.RetryAfter.TotalSeconds)).ToString();
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    if (!await auth.CheckAsync(body.Email, body.Password, ct))
        return Results.Unauthorized();                       // the failed attempt stays counted

    await limiter.ClearAsync("login", subject, ct);          // RateLimiter::clear after a successful sign-in
    return Results.Ok();
});
```

`Remaining` tells you how many attempts are left in the window, for example to send an `X-RateLimit-Remaining` header.

`ICacheStore.IncrementAsync(key, by, ttl)` atomically increments a counter and only applies TTL when
the key is created. Redis performs increment and first-expiry assignment in one Lua operation; Memory
coordinates with its per-key lock; Memcached uses atomic add followed by atomic increment (which carries the same TTL, so a key that expires between the two calls is recreated with an expiry and never becomes a permanent counter). Later
increments never slide the fixed-window boundary.

**Local versus distributed.** The limit is shared by exactly the processes that share the counter:

| Backend | Counter shared across replicas | What the limit means with N replicas |
|---|---|---|
| Redis | yes (atomic Lua increment) | one limit for all replicas |
| Memcached | yes (atomic add, then atomic increment) | one limit; an evicted key resets the window |
| Memory | **no** (per process) | **each replica admits the full limit**, so the effective limit is N times higher |
| route `throttle` middleware | **no** by default (`System.Threading.RateLimiting`, per process). `Naravel.Routing.Cache` counts in the store you name, with its own one-call counter (it does not use `IRateLimiter`) | default store: same as Memory; shared store: as the named store above |

Failure behaviour: the limiter takes no lock, so `AttemptAsync` cannot time out while waiting for one. A backend outage surfaces the backend's exception, and the call then neither admits nor rejects, so decide in the caller whether to fail open or closed. If the backend fails after the counter was incremented (in the second call, which records the start of the window), that attempt stays counted although the caller gets the exception. The counter is not rolled back if your own work after an allowed attempt fails.

A fixed window can admit up to **2x the configured limit** in a short interval across an adjacent-window
boundary (for example, the full limit just before expiry and another full limit just after reset). This is
not a rolling-window guarantee; use `System.Threading.RateLimiting` with a sliding window for local limits
that need that behavior.

The limiter takes no lock. Each attempt is one atomic `IncrementAsync(key, 1, window)` on the selected store, which also fixes the end of the window when it creates the counter. The count that call returns decides the outcome, so concurrent attempts, even from different instances, never receive the same count. A second key holds the time the window began and is used only to work out `RetryAfter`; when it is missing or stale (the first attempt has not written it yet, or the key was evicted), the attempt records the current time, which can only make `RetryAfter` longer than the true remainder and never changes whether an attempt is admitted. `ClearAsync` removes both keys. The `lockManager` argument of the `CacheRateLimiter` constructor is ignored and stays so that existing code keeps compiling.

The limiter emits `naravel.cache.ratelimiter.allowed`, `naravel.cache.ratelimiter.rejected`, and a
duration histogram through the `Naravel.Cache` meter, plus an `ActivitySource` span. Metric tags use
only limiter name and outcome, never the subject key.

The Memory throughput benchmark is `MemoryRateLimiterAttempt` in
`benchmarks/Naravel.Benchmarks/QueueAndCacheBenchmarks.cs`. Run it in Release mode with:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*MemoryRateLimiterAttempt*' --job short
```

## Errors

Calling a method wrongly fails fast with a standard .NET exception. The messages below are the ones the code raises.

| When | Exception and message |
|---|---|
| A blank `key`, lock `name` or scope | `ArgumentException` (`ArgumentNullException` for `null`) |
| `ForUser(null)` | `ArgumentNullException` |
| `Tags()` with no non-empty tag | `ArgumentException`: `At least one non-empty cache tag is required.` |
| `IncrementAsync(key, by, ttl)` with `by` or `ttl` of zero or less | `ArgumentOutOfRangeException` |
| `BlockAsync` with a `ttl` of zero or less or a negative `wait` (the Memory lock's `AcquireAsync` rejects a `ttl` of zero or less too) | `ArgumentOutOfRangeException` |
| `RefreshAsync` with a `ttl` of zero or less | `ArgumentOutOfRangeException` |
| `AttemptAsync` with `maxAttempts` or `window` of zero or less | `ArgumentOutOfRangeException` |
| A store name that is not registered | `DriverNotRegisteredException`: `Driver 'x' is not registered for ICacheStore. Available drivers: …` |
| `Store()` on an async-only store not yet created | `InvalidOperationException`; call `DriverAsync` first (see [Select a store](#select-a-store)). |
| The token is cancelled | `OperationCanceledException` |
| The backend is down | The backend client's own exception, unchanged (for Redis, the StackExchange.Redis exception). |

Foundation's own errors (no default store, a disposed manager) are listed in the [Foundation guide](foundation.md#errors).

`CacheFake` assertions throw `InvalidOperationException` with these messages:

| Assertion | Message |
|---|---|
| `AssertHas("k")` on a missing or expired key | `Expected cache key 'k' to exist, but it does not.` |
| `AssertHas<int>("k")` when the value is a string | `Cache key 'k' holds String, not Int32.` |
| `AssertMissing("k")` on a stored key | `Expected cache key 'k' to be missing, but it exists.` |
| `AssertEmpty()` with two keys left | `Expected an empty cache, but found 2 key(s): a, b.` |

## Provider behavior and limitations

| Provider | Value handling | Lock behavior | Flush behavior |
|---|---|---|---|
| Memory | Typed in-process values | Process-local token locks; can refresh and force release | Removes keys tracked by that store instance, or only those under a prefix |
| Redis | JSON via `System.Text.Json` | Distributed; token release and refresh are atomic; can force release | Scans and deletes only keys under the configured prefix, or under the scope's prefix; never the keys of locks; throws when a primary is not connected |
| Memcached | Typed values via the configured Enyim transcoder; `ttl: null` means no expiry | Distributed acquisition; release is not fully atomic; cannot refresh or force release | A scope or `Store()` flush through `CacheManager` throws `NotSupportedException`. `MemcachedCacheStore.FlushAsync` called on the raw driver throws `NotSupportedException` too, because the only flush Memcached has clears every key on every node, including other applications |

Use tag invalidation instead of `FlushAsync` when using Memcached or when only a subset of keys should be invalidated. The existing Memcached increment/decrement operations still require existing numeric values; the TTL increment overload uses atomic add for first creation. The current Enyim client exposes counter commands synchronously; the provider schedules them on the thread pool, and cancellation cannot interrupt a command after it starts.

Memory state is not shared across app instances. Redis is the distributed option for multi-server cache and locks.

`tests/Naravel.Cache.Tests` includes a shared store contract for Memory, Redis and Memcached, covering atomic increments with first-write-only TTL. Rate-limiter tests cover fixed-window boundaries, clearing, named-store isolation, cancellation, Fake behavior and Meter/Activity output. Set
`NARAVEL_TEST_REDIS=host:port` or `NARAVEL_TEST_MEMCACHED=host:port` to run the corresponding live
provider test; without the variable, xUnit skips it. CI runs both against service containers.

## Test fake

`Naravel.Cache.Testing.CacheFake` is an in-memory `ICacheStore` for application tests. Inject it where `ICacheStore` is expected and assert on what is cached; no Redis, no Memcached and no `IMemoryCache`.

```csharp
var clock = new ManualClock();                 // your TimeProvider subclass
var cache = new CacheFake(timeProvider: clock);
await sut.RememberProfileAsync(42);

cache.AssertHas<Profile>("profile:42");
clock.Advance(TimeSpan.FromMinutes(11));       // expiry follows the clock, so no sleeping
cache.AssertMissing("profile:42");
```

It follows the Memory store (typed values, first-write-only TTL on `IncrementAsync(key, by, ttl)`), keeps no tags or locks, and passes the same shared store contract. `Keys`, `AssertHas`, `AssertMissing`, `AssertEmpty` and `Reset` are its inspection API. Closes AUDIT D-23 for the cache half.

The snippets in this guide (basic operations, `RememberAsync`, scopes and tags, locks, the login throttle) are run against the Memory store by `tests/Naravel.Cache.Tests/DocsExampleTests.cs`. Change one, change the other. The Redis and Memcached variants are covered by the provider tests.
