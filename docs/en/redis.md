# Redis connections

`Naravel.Redis` hands out **named, shared** Redis connections. It returns the real StackExchange.Redis `IConnectionMultiplexer` and never wraps commands (PDR-010). `Naravel.Cache.Redis` and `Naravel.Queue.Redis` borrow their connection from it, so one server is one connection, and one owner closes it.

## Quickstart

A minimal API that counts visits in Redis. You need a Redis server on `localhost:6379`; Naravel does not start one.

```sh
dotnet new web -n RedisDemo
cd RedisDemo
dotnet add package Naravel.Redis
```

Add the connection to `appsettings.json`:

```json
{ "Redis": { "Default": "main", "Stores": { "main": { "ConnectionString": "localhost:6379" } } } }
```

Replace `Program.cs`:

```csharp
using Naravel.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelRedis(builder.Configuration);   // also called for you by AddNaravelRedisCache and AddRedisDriver
var app = builder.Build();

app.MapPost("/visits", async (RedisManager redis) =>
{
    var db = redis.Connection().Multiplexer.GetDatabase();   // the real StackExchange.Redis IDatabase
    return new { count = await db.StringIncrementAsync("visits") };
});

app.Run();
```

Try it, with `$URL` set to the address `dotnet run` prints:

```sh
curl -X POST "$URL/visits"      # {"count":1}
curl -X POST "$URL/visits"      # {"count":2}
```

The cache and the queue borrow the same connection: name it in their stores (`"Connection": "main"`, see [Cache and Queue](#cache-and-queue)) and one server is one multiplexer.

## Using it

### Named connections

A store under `Redis:Stores:<name>` is only a `ConnectionString`: the StackExchange.Redis string already carries every client option (TLS, password, `defaultDatabase`, timeouts), so Naravel invents no option names. `redis.Connection()` returns the default (`Redis:Default`), and `IRedisConnection` injects it. An unknown name throws and lists the available names; a store without `ConnectionString` throws naming the store.

### Cache and Queue

A cache or queue store names a connection with `Connection`. The old `ConnectionString` key keeps working: equal strings are compared by a hash and share one multiplexer, so a store and its lock, or two stores on one server, no longer open several connections. No configuration change is needed to get this.

### Ownership

The manager creates a multiplexer on first use and disposes every one when the host shuts down. Consumers borrow it and must not dispose it. `RedisCacheStore`, `RedisLock` and `RedisQueueDriver` have an optional `ownsConnection` parameter: `true` (the default for the cache store and lock, as before) disposes the connection they were given, `false` (the default for the queue driver, as before, and what the registrations pass) leaves it to its owner. Because the registrations borrow, the connection the queue driver used to open and never close is now closed with the manager.

### Reload and run-time connections

Changing a connection string in configuration makes the next `Connection(name)` open a new multiplexer; the old one stays open for requests still using it and is disposed with the manager. Unrelated configuration edits change nothing. Add a connection later with `redis.Extend("name", provider => ...)`.

## Errors

The messages below are the ones in the source. .NET adds ` (Parameter 'name')` to the message of an argument exception, which the table leaves out. `Naravel.Redis` writes no log lines, and none of its messages contains a connection string, a password or a host: an error names the connection, never what it connects to.

| When | Exception and message |
|---|---|
| `AddNaravelRedis` with a null `services` or `configuration` | `ArgumentNullException`, parameter `services` or `configuration` |
| `AddNaravelRedis` with a null, empty or blank `sectionName` | `ArgumentNullException` for null, `ArgumentException` for the others; parameter `sectionName` |
| `Connection(name)` with an empty or blank name | `ArgumentException`: `Driver name must not be blank.` |
| `Connection()` when `Redis:Default` is empty or missing | `InvalidOperationException`: `No default driver is configured for IRedisConnection. Set the 'Default' option or pass a driver name explicitly.` The option meant is `Redis:Default`. |
| `Connection("nope")` for a name that is not a store, an `Extend`ed connection or an implicit one | `DriverNotRegisteredException`, an `InvalidOperationException`: `Driver 'nope' is not registered for IRedisConnection. Available drivers: main, other.` The names are sorted, and the implicit ones (`implicit-` plus a hash) are listed too. |
| The first `Connection(name)` of a store that has no `ConnectionString` | `InvalidOperationException`: `Redis connection 'bad' requires ConnectionString.` Registering does not fail; the check runs when the name is first asked for, which for the default connection is the first time `IRedisConnection` is resolved. |
| `FromConnectionString` with a null, empty or blank string | `ArgumentNullException` for null, `ArgumentException` for the others; parameter `connectionString` |
| Any call on the manager after the host disposed it | `ObjectDisposedException`, object name `RedisManager` |
| The client cannot connect | What `ConnectionMultiplexer.Connect` throws, unwrapped. A connection that failed to open is not remembered: the next `Connection(name)` tries again. |

**No error at all.** `Connection("MAIN")` for a store named `main` (names are not case sensitive, and it is the same connection); `Connection("main")` or `FromConnectionString` with the same string twice (one multiplexer); a configuration change that does not touch `Redis` (nothing is reopened); and a second `AddNaravelRedis`, which returns at once, so **its `configuration` and `sectionName` are ignored**. That is what lets `AddNaravelRedisCache` call it for you, but it means the first call decides the stores.

## Testing

There is no fake of the Redis protocol: it would fake behaviour (scripts, expiry) the server defines. The seam is a `Func<string, IConnectionMultiplexer>` registered in DI before `AddNaravelRedis`: it receives the connection string and returns the multiplexer, so a test can count the connections, see which string each one got, or return a stand-in. The stand-in below answers every member with its default and counts `Dispose`; use your own if your code calls the database.

```csharp
public class CountingMultiplexer : DispatchProxy
{
    public int Disposed { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IDisposable.Dispose)) Disposed++;
        var returnType = targetMethod?.ReturnType;
        return returnType is { IsValueType: true } && returnType != typeof(void) ? Activator.CreateInstance(returnType) : null;
    }
}

static ServiceProvider Build(List<string> opened)
{
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Redis:Default"] = "main",
        ["Redis:Stores:main:ConnectionString"] = "main-host:6379",
        ["Redis:Stores:other:ConnectionString"] = "other-host:6379"
    }).Build();
    var services = new ServiceCollection();
    services.AddSingleton<Func<string, IConnectionMultiplexer>>(connectionString =>
    {
        opened.Add(connectionString);
        return DispatchProxy.Create<IConnectionMultiplexer, CountingMultiplexer>();
    });
    services.AddNaravelRedis(configuration);
    return services.BuildServiceProvider();
}

[Fact]
public void A_name_opens_one_connection_with_the_string_of_its_store()
{
    var opened = new List<string>();
    using var provider = Build(opened);
    var redis = provider.GetRequiredService<RedisManager>();

    redis.Connection("main");
    redis.Connection("main");
    redis.Connection("other");

    opened.Should().Equal("main-host:6379", "other-host:6379");
}

[Fact]
public void The_multiplexer_is_disposed_once_with_the_host_and_not_by_the_borrower()
{
    using var provider = Build(new List<string>());
    var multiplexer = provider.GetRequiredService<IRedisConnection>().Multiplexer;   // the default connection, borrowed
    var counter = (CountingMultiplexer)(object)multiplexer;

    counter.Disposed.Should().Be(0);
    provider.Dispose();

    counter.Disposed.Should().Be(1);
}
```

Tests against a real server stay env-gated (`NARAVEL_TEST_REDIS`).

## Complete example

One shared connection used by your own code, a second named connection, and a run-time connection. The cache and the queue keep borrowing the same `main` connection.

```csharp
// Program.cs: one Redis server, borrowed by the cache, the queue and your own code.
builder.Services.AddNaravelRedis(builder.Configuration);        // reads Redis:Default and Redis:Stores (see the quickstart JSON)

app.MapPost("/visits", async (RedisManager redis, CancellationToken ct) =>
{
    var db = redis.Connection().Multiplexer.GetDatabase();      // the real StackExchange.Redis IDatabase
    var count = await db.StringIncrementAsync("visits");
    return Results.Ok(new { count });
});

// A second, named connection (for example another database or server), declared in configuration:
//   "Redis": { "Default": "main", "Stores": { "main": { "ConnectionString": "localhost:6379" },
//                                              "sessions": { "ConnectionString": "localhost:6379,defaultDatabase=2" } } }
app.MapGet("/sessions/{id}", async (string id, RedisManager redis) =>
{
    var db = redis.Connection("sessions").Multiplexer.GetDatabase();
    var value = await db.StringGetAsync($"session:{id}");
    return value.HasValue ? Results.Ok(value.ToString()) : Results.NotFound();
});

// A connection added at run time (Laravel: Redis::extend). Do not dispose the multiplexer yourself: the manager owns it.
var manager = app.Services.GetRequiredService<RedisManager>();
manager.Extend("reports", provider => new ReportsRedisConnection(provider));   // your IRedisConnection implementation
```

The counting stand-in and the tests that use it are in [Testing](#testing).

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
