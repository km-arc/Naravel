# Redis connections

`Naravel.Redis` hands out **named, shared** Redis connections. It returns the real StackExchange.Redis `IConnectionMultiplexer` and never wraps commands (PDR-010). `Naravel.Cache.Redis` and `Naravel.Queue.Redis` borrow their connection from it, so one server is one connection, and one owner closes it.

## Quickstart

```json
{ "Redis": { "Default": "main", "Stores": { "main": { "ConnectionString": "localhost:6379" } } },
  "Cache": { "Stores": { "redis": { "Driver": "redis", "Connection": "main", "Prefix": "app" } } },
  "NaravelQueue": { "Stores": { "jobs": { "Driver": "redis", "Connection": "main" } } } }
```

```csharp
builder.Services.AddNaravelRedis(builder.Configuration);   // also called for you by AddNaravelRedisCache and AddRedisDriver
var multiplexer = redis.Connection("main").Multiplexer;    // redis is an injected RedisManager; the real IConnectionMultiplexer
```

## Using it

### Named connections

A store under `Redis:Stores:<name>` is only a `ConnectionString`: the StackExchange.Redis string already carries every client option (TLS, password, `defaultDatabase`, timeouts), so Naravel invents no option names. `redis.Connection()` returns the default (`Redis:Default`), and `IRedisConnection` injects it. An unknown name throws and lists the available names; a store without `ConnectionString` throws naming the store.

### Cache and Queue

A cache or queue store names a connection with `Connection`. The old `ConnectionString` key keeps working: equal strings are compared by a hash and share one multiplexer, so a store and its lock, or two stores on one server, no longer open several connections. No configuration change is needed to get this.

### Ownership

The manager creates a multiplexer on first use and disposes every one when the host shuts down. Consumers borrow it and must not dispose it. `RedisCacheStore`, `RedisLock` and `RedisQueueDriver` have an optional `ownsConnection` parameter: `true` (the default for the cache store and lock, as before) disposes the connection they were given, `false` (the default for the queue driver, as before, and what the registrations pass) leaves it to its owner. Because the registrations borrow, the connection the queue driver used to open and never close is now closed with the manager.

### Reload and run-time connections

Changing a connection string in configuration makes the next `Connection(name)` open a new multiplexer; the old one stays open for requests still using it and is disposed with the manager. Unrelated configuration edits change nothing. Add a connection later with `redis.Extend("name", provider => ...)`.

### Testing

There is no fake of the Redis protocol: it would fake behaviour (scripts, expiry) the server defines. The seam is a `Func<string, IConnectionMultiplexer>` registered in DI before `AddNaravelRedis`; tests use it to count connections or return a stand-in. Tests against a real server stay env-gated (`NARAVEL_TEST_REDIS`).

## Observability

None of its own: the client exposes its own counters. Connection strings and passwords are never logged, named or put in exception messages by Naravel; an implicit connection is named `implicit-` plus a hash.

## How it is built

- `RedisManager` is a Foundation `Manager<IRedisConnection, RedisOptions>`; each connection is a driver, so `Extend`, reload and disposal come from the base class (PDR-004, PDR-005).
- `FromConnectionString` registers a driver named by the hash on first use. The string is kept only inside the factory that opens the connection.
- The multiplexer is opened with `ConnectionMultiplexer.Connect` (synchronous), because Cache and Queue resolve their stores synchronously.

## Not included (by decision)

Command forwarding (`Redis::get()`), a fake of the Redis protocol, Redis Cluster or Sentinel features beyond what the connection string allows, and Lua scripts (they stay in the Cache and Queue packages).

## Laravel mapping

| Laravel | Naravel |
|---|---|
| `config/database.php` `redis.*` | `Redis:Stores:<name>:ConnectionString` |
| `Redis::connection('cache')` | `redisManager.Connection("cache")` |
| a cache store's `connection => 'cache'` | `"Connection": "cache"` on the store |
| `Redis::extend()` | `redisManager.Extend(name, factory)` |
