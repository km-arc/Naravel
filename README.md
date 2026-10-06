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

Route middleware is available, but ready-made throttle, signed-URL, and maintenance middleware are not yet implemented. See [docs/en/routing.md](docs/en/routing.md) and [docs/fa/routing.md](docs/fa/routing.md).

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

Queue registrations are extension methods in `Naravel.Queue.Extensions`. The Queue configuration uses `NaravelQueue:Stores`; Cache and Filesystem use `Cache:Stores` and `Filesystem:Stores` by default. Each supports a custom section name.

## Build and test

Requires the .NET 10 SDK. From the repository root:

```sh
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

Naravel is a monorepo: `Naravel.slnx` contains all modules, providers, tests, and samples. Central package versions are in `Directory.Packages.props`; shared build settings and the lock-step package version are in `Directory.Build.props`.
