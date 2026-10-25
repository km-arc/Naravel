# PDR-010 — Shared Redis connections (`Naravel.Redis`)

**Native-first verdict:** for **one** Redis connection, plain .NET is enough: `services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost"))` is two lines, and `IConnectionMultiplexer` is already a thread-safe, long-lived, shareable object. A Naravel wrapper would only hide a good API. **The case for a module is narrower and comes from this repository's own code:** named connections, one owner for disposal, and no duplicate pools. **Proposed outcome: accepted as a thin module that hands out the real `IConnectionMultiplexer` and nothing else.** If the owner prefers the pure-native route, the alternative below is `DECLINED` with a recipe.

Status: **Approved 2026-10-25.**

## Context

What the code does today (verified in the repository):

- `Naravel.Cache.Redis` registers a store factory and a lock factory per store, and **each factory calls `ConnectionMultiplexer.Connect`**. A store and its lock therefore use two connections to the same server, and `RedisCacheStore` and `RedisLock` each dispose the one they created.
- `Naravel.Queue.Redis` calls `ConnectionMultiplexer.Connect` once per queue store, and the driver, the failed-job store and the batch repository receive that one multiplexer. `RedisQueueDriver` has no `Dispose`, so **the connection is never closed** when the manager retires or disposes the driver.
- Cache and Queue cannot share a connection to the same Redis server, and a future module that needs Redis (a session store, a SignalR backplane or a distributed lock) would add a third and fourth pool.
- Both modules read `ConnectionString` from their own store section, so the same server string is repeated in several places in configuration.

What .NET and StackExchange.Redis already give: a multiplexer is designed to be created once and shared; it handles reconnection; `ConfigurationOptions` (or the connection string) carries every option (TLS, passwords, `abortConnect`, `defaultDatabase`, timeouts). `Microsoft.Extensions.Caching.StackExchangeRedis` has its own cache-only connection. There is no Laravel-style named-connection manager in the ecosystem.

Laravel's side: `config/database.php` `redis.*` defines named connections (`default`, `cache`), and `Redis::connection('cache')` resolves them; the cache and queue drivers refer to a connection by name instead of repeating the server.

## Proposed quickstart (S1)

```json
{ "Redis": { "Default": "main", "Stores": { "main": { "ConnectionString": "localhost:6379" } } },
  "Cache": { "Stores": { "redis": { "Driver": "redis", "Connection": "main", "Prefix": "app" } } },
  "NaravelQueue": { "Stores": { "jobs": { "Driver": "redis", "Connection": "main" } } } }
```

```csharp
builder.Services.AddNaravelRedis(builder.Configuration);                            // one multiplexer per named connection
builder.Services.AddNaravelCache(builder.Configuration).AddNaravelRedisCache(builder.Configuration);
var multiplexer = redis.Connection("main").Multiplexer;                             // redis is an injected IRedisConnections; the real IConnectionMultiplexer
```

## Proposed decision

1. New package `Naravel.Redis` (project-referenced by `Naravel.Cache.Redis` and `Naravel.Queue.Redis`, which stop referencing StackExchange.Redis directly). It adds a manager on Foundation's `Manager<,>` (PDR-004, PDR-005): configuration is `Redis:Default` and `Redis:Stores:<name>`, and a store is just a `ConnectionString` (the StackExchange.Redis string carries every client option, so Naravel invents no option names).
2. **The manager exposes the real `IConnectionMultiplexer`** (and `GetDatabase`/subscriber through it). It does not wrap commands: the parity matrix says not to hide useful .NET client capabilities.
3. **One multiplexer per named connection, created on first use and owned by the manager.** The manager disposes all of them when the host shuts down. Consumers borrow: a cache store, lock, queue driver or any application code never disposes a multiplexer it got from the manager.
4. **Cache and Queue refer to a connection by name** with a new `Connection` key. **The old `ConnectionString` key keeps working:** the manager creates (and de-duplicates by a hash of the string, never storing or logging the string) an implicit connection, so two stores or a store and its lock that use the same string now share one multiplexer instead of opening several. This fixes the double connection in `Naravel.Cache.Redis` without a configuration change.
5. **Ownership flag, no breaking change:** the public constructors of `RedisCacheStore` and `RedisLock` keep their behaviour (they dispose the multiplexer they were given) and gain an optional `ownsConnection` parameter that the shared path sets to `false`. `RedisQueueDriver` gains an equivalent flag; when it owns its connection it now disposes it (this fixes the leak).
6. **Reload follows PDR-004:** a real change of a connection string retires the old multiplexer and disposes it when the manager is disposed (a request may still be using it); the fingerprint approach prevents rebuilding on unrelated edits.
7. **Failure is visible:** an unknown connection name throws with the name and the available names; a missing `ConnectionString` throws with the store name; connection errors are the client's own exceptions, not wrapped.
8. **S3:** an `ActivitySource`/`Meter` is not added (the client exposes its own counters); the test seam is a factory delegate (`Func<string, IConnectionMultiplexer>`) so tests count creations and substitute a connection without a server. No fake of the Redis protocol is written: it would fake behaviour (scripts, expiry) the real server defines. Provider tests stay env-gated (OD-01).
9. **Out of scope:** the StackExchange.Redis 3.x upgrade (already done on the owner request in the dependency refresh, AUDIT D-65, with CI green), Lua scripts (they stay where R01 put them), Redis Cluster/Sentinel features beyond what the connection string already allows.

## Alternatives considered

1. **Decline: register `IConnectionMultiplexer` once in DI and document it.** The honest native answer for one server. It does not name connections, does not de-duplicate the connections Cache and Queue open today, does not fix the Queue leak, and puts the same string in several configuration sections. Recipe and `DECLINED` if the owner prefers it; the two defects above would then be fixed inside each module.
2. **Wrap Redis commands (`Redis::get()` style).** Rejected: duplicates the client and hides capabilities.
3. **A connection pool per store, as now.** Rejected: it is the problem this PDR solves.

## Decisions for the owner

- **D1:** accept the thin `Naravel.Redis` (recommended), or `DECLINED` with the DI recipe (alternative 1).
- **D2:** the `Connection` key plus keeping `ConnectionString` working and de-duplicated (recommended), or `Connection` only (a breaking change listed in the changelog, allowed pre-1.0 by OD-09).
- **D3:** the manager owns and disposes, consumers borrow, with an `ownsConnection` flag so old constructors behave as before (recommended).
- **D4:** no fake of the Redis protocol; a factory seam for tests (recommended).

## Contracts and quality (if accepted)

Connection strings and passwords never appear in logs, exception messages, metric tags or names (implicit connections are named by a hash). All connection work is async where the client offers it, with `CancellationToken`. Tests: `Extend` and config-reload tests; a test counts multiplexer creations (one per name, one for an identical string used by a store and a lock); the queue driver disposes an owned connection; a borrowed connection is not disposed by the store; the R01 provider tests pass unchanged against a real Redis in the env-gated suite.

## Approval

The owner approved D1 to D4 as recommended on 2026-10-25, after the trade-offs were explained: D1 build the thin `Naravel.Redis` (it fixes the duplicate connections and the Queue leak that the plain DI recipe would leave to each module); D2 add the `Connection` key and keep `ConnectionString` working, de-duplicated (no breaking change); D3 the manager owns and disposes, consumers borrow, with an `ownsConnection` flag so old constructors behave as before; D4 no fake of the Redis protocol, a factory seam for tests. The stage now runs the module task, the migration task and the validation task.
