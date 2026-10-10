# Naravel

Laravel-inspired, discoverable APIs with .NET's strengths: strong typing, `async/await`, dependency injection, and native performance. Naravel adopts a Laravel idea only when it adds practical value beyond what .NET already provides; the rationale is recorded in a PDR.

**Module and roadmap status:** [ROADMAP.md](ROADMAP.md) is the source of truth. Build and test results are reported by [GitHub Actions](https://github.com/km-arc/Naravel/actions/workflows/ci.yml).

- Persian: [README.fa.md](README.fa.md)
- Contributor and agent guidance: [AGENTS.md](AGENTS.md)
- English docs: [docs/en](docs/en) · PDRs: [docs/pdr/en](docs/pdr/en)
- Persian docs: [docs/fa](docs/fa) · PDRs: [docs/pdr/fa](docs/pdr/fa)

## Modules

### Naravel.Foundation

Shared driver-manager infrastructure used by driver-based modules. It resolves and caches named drivers, supports runtime `Extend`, reacts to configuration changes, and owns driver disposal. Applications usually consume it through Cache, Queue, or Filesystem rather than registering Foundation directly. For example, a Filesystem manager can register a local disk at runtime:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Naravel.Filesystem;
using Naravel.Filesystem.Drivers;

var storage = app.Services.GetRequiredService<StorageManager>();
storage.Extend("scratch", _ => new LocalStorageDriver("storage/scratch", "/scratch"));
var scratch = storage.Disk("scratch");
```

### Naravel.Cache

Named Memory, Redis, and Memcached stores; `RememberAsync`; explicit user/tenant scopes; tag-version invalidation; token-owned locks; and a Cache-backed fixed-window rate limiter. Redis and Memcached are optional provider packages.

```csharp
using Naravel.Cache.Abstractions;

builder.Services.AddNaravelCache(builder.Configuration)
    .AddNaravelRedisCache(builder.Configuration);

public sealed class SettingsService(ICacheStore cache)
{
    public Task<string> GetAsync(CancellationToken ct) =>
        cache.RememberAsync("site-name", TimeSpan.FromMinutes(10), _ => Task.FromResult("Naravel"), ct);
}
```

Use `ICacheStore` for the default store and `CacheManager` to choose a named store or create a scope. Provider behavior and limitations, including Memcached's cluster-wide `FlushAsync`, are documented in [docs/en/cache.md](docs/en/cache.md) and [docs/fa/cache.md](docs/fa/cache.md).

### Naravel.Queue

Async background jobs with named drivers, delayed and prioritized dispatch, retries and backoff, chaining, batches, failed-job handling, hosted workers, and telemetry. Jobs are **at-least-once** and should be idempotent. The Memory driver is process-local; Redis, File, and Database use visibility timeouts for recovery, while Kafka and RabbitMQ use broker redelivery.

```csharp
using Naravel.Queue.Extensions;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Jobs;

builder.Services.AddQueue(builder.Configuration)
    .AddRedisDriver(builder.Configuration);
builder.Services.AddJob<SendWelcomeEmailJob>("mail.welcome");
builder.Services.AddQueueWorker(w => { w.Queues = new[] { "default" }; w.Concurrency = 4; });

var app = builder.Build();
var dispatcher = app.Services.GetRequiredService<IJobDispatcher>();
await dispatcher.DispatchAsync(new SendWelcomeEmailJob("ali@example.com"));
```

See [docs/en/queue.md](docs/en/queue.md) and [docs/fa/queue.md](docs/fa/queue.md) for job definitions, configuration, failed jobs, persistent batches, worker controls, and provider-specific setup.

### Naravel.Events

Typed, asynchronous, in-process events. DI-registered `IEventListener<TEvent>` listeners run in registration order, followed by request-scoped `Listen` callbacks (each returns an `IDisposable` subscription). A listener can call `context.Stop()` to stop later listeners; the first listener exception propagates to the caller and stops the dispatch. Listeners are matched by the exact event type passed to `DispatchAsync<TEvent>`, with no base-type or interface fan-out. The dispatcher is scoped, emits a `Naravel.Events` `Meter` and `ActivitySource`, and `Naravel.Events.Testing.EventFake` records dispatched events for tests.

```csharp
using Naravel.Events;

builder.Services.AddNaravelEvents();
builder.Services.AddScoped<IEventListener<OrderPaid>, UpdateOrderReadModel>();

public sealed record OrderPaid(string OrderId);

public sealed class UpdateOrderReadModel : IEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken ct = default) =>
        Task.CompletedTask;
}

public sealed class CheckoutService(IEventDispatcher events)
{
    public Task PayAsync(string orderId, CancellationToken ct) =>
        events.DispatchAsync(new OrderPaid(orderId), ct);
}
```

The optional `Naravel.Events.Queue` adapter runs a listener through `Naravel.Queue` instead. Implement `IQueuedEventListener<TEvent>` and register it under an explicit alias; the alias, not a CLR type name, is what is stored in the queue. Configure Queue and a driver as usual. The adapter dispatches on the default connection and the `default` queue and serializes the event with `System.Text.Json`, so keep events small and serializable. Queued execution is at-least-once, like any Queue job.

```csharp
builder.Services.AddNaravelEventsQueue();
builder.Services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");
```

See [docs/en/events.md](docs/en/events.md) and [docs/fa/events.md](docs/fa/events.md), and run the sample's `/events` page for a working example.

### Naravel.Filesystem

Named local and S3/S3-compatible storage disks. The local driver rejects paths outside its configured root. Naravel does not create a public HTTP endpoint; serve or authorize stored files through ASP.NET Core separately.

```csharp
using Naravel.Filesystem;

builder.Services.AddNaravelFilesystem(builder.Configuration);

public sealed class ArchiveService(IStorageDriver storage)
{
    public Task SaveAsync(string key, Stream contents, CancellationToken ct) =>
        storage.PutAsync(key, contents, ct);
}
```

See [docs/en/filesystem.md](docs/en/filesystem.md) and [docs/fa/filesystem.md](docs/fa/filesystem.md) for disk configuration, uploads, S3 URLs, and security notes.

### Naravel.Routing

A thin layer over ASP.NET Core routing: nested route groups, names and URL generation, constraints, resource routes, model binding, and route middleware aliases/groups/parameters.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelRouting();
var app = builder.Build();
app.UseNaravelRouting();
app.MapNaravel(routes => routes
    .Prefix("admin").Name("admin.")
    .Group(group => group.Get("users/{id}", (int id) => Results.Ok(new { id }))
        .Name("users.show")));
```

Ready-made route middleware aliases are `throttle` (process-local fixed window), `signed`, `maintenance`, `cache.headers`, `trim`, `convert.empty`, and `guest`; custom aliases, groups, and parameters are supported too. Throttling and maintenance state are process-local, and signed URLs need a shared, persisted Data Protection key ring across instances. See [docs/en/routing.md](docs/en/routing.md) and [docs/fa/routing.md](docs/fa/routing.md).

## Provider packages

Register the core package and only the providers your configured stores use. The snippets below are alternatives; the Database driver also requires an EF Core context and `ConfigureQueueJobs()` model mapping. Provider configuration and requirements are in the linked module docs.

| Package | Example registration |
|---|---|
| `Naravel.Cache` (Memory) | `builder.Services.AddNaravelCache(configuration);` |
| `Naravel.Cache.Redis` | `builder.Services.AddNaravelRedisCache(configuration);` |
| `Naravel.Cache.Memcached` | `builder.Services.AddNaravelMemcachedCache(configuration);` |
| `Naravel.Queue.Memory` | `builder.Services.AddMemoryDriver(configuration);` |
| `Naravel.Queue.File` | `builder.Services.AddFileDriver(configuration);` |
| `Naravel.Queue.Redis` | `builder.Services.AddRedisDriver(configuration);` |
| `Naravel.Queue.Database` | `builder.Services.AddDatabaseDriver<AppDbContext>(configuration);` |
| `Naravel.Queue.RabbitMQ` | `builder.Services.AddRabbitMqDriver(configuration);` |
| `Naravel.Queue.Kafka` | `builder.Services.AddKafkaDriver(configuration);` |
| `Naravel.Events.Queue` (adapter) | `builder.Services.AddNaravelEventsQueue();` |

Queue registrations are extension methods in `Naravel.Queue.Extensions`. The Queue configuration uses `NaravelQueue:Stores`; Cache and Filesystem use `Cache:Stores` and `Filesystem:Stores` by default. Each supports a custom section name.

## Sample app

`samples/Naravel.Sample` is a runnable web app that exercises the modules. It is never packed.

```sh
dotnet run --project samples/Naravel.Sample/Naravel.Sample.csproj
```

| URL | What it shows |
|---|---|
| `/queue` | Queue dispatch, delay, priority, retry/backoff, chains and batches (file-backed by default) |
| `/events` | `OrderPlaced` dispatch: DI listeners, stop propagation, a `Listen` callback, and a queued listener |
| `/routing/` | Route groups, constraints, resources, model binding and route middleware |

See [samples/Naravel.Sample/README.md](samples/Naravel.Sample/README.md) for provider switching and demo headers.

## Build and test

Requires the .NET 10 SDK. From the repository root:

```sh
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

Naravel is a monorepo: `Naravel.slnx` contains all modules, providers, tests, and samples. Central package versions are in `Directory.Packages.props`; shared build settings and the lock-step package version are in `Directory.Build.props`.
