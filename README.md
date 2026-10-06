# Naravel

Laravel's concise, discoverable APIs with .NET's speed and strengths: strong typing, `async/await`, dependency injection
and high-performance native libraries. Naravel adopts a Laravel feature only when it adds value on top of what .NET
already offers; every such decision is documented as a PDR.

**Status:** See [ROADMAP.md](ROADMAP.md) for the authoritative module and stage status.

> **Verification:** Build and test results are reported by [GitHub Actions](https://github.com/km-arc/Naravel/actions/workflows/ci.yml).

- Persian: [README.fa.md](README.fa.md)
- AI agents / contributors: [AGENTS.md](AGENTS.md)
- Roadmap and current priority: [ROADMAP.md](ROADMAP.md) · Persian overview: [ROADMAP.fa.md](ROADMAP.fa.md)
- Docs: [docs/en](docs/en) · Decisions: [docs/pdr/en](docs/pdr/en)

## Cache in 30 seconds
```csharp
using Naravel.Cache;
using Naravel.Cache.Abstractions;

builder.Services.AddNaravelCache(builder.Configuration)
    .AddNaravelRedisCache(builder.Configuration);

public sealed class SettingsService(ICacheStore cache)
{
    public Task<string> GetAsync(CancellationToken ct) =>
        cache.RememberAsync("site-name", TimeSpan.FromMinutes(10), _ => Task.FromResult("Naravel"), ct);
}

public sealed class Reports(CacheManager cache)
{
    public ICacheStore Redis => cache.Store("redis");
}
```
For Laravel-like convenience without a static facade, inject `ICacheStore` for the default or use `CacheManager` to select
a named store, scope keys or add tags. Full setup and provider limitations: [docs/en/cache.md](docs/en/cache.md) /
[docs/fa/cache.md](docs/fa/cache.md).

## Repository layout (monorepo)
One repository, one solution (`Naravel.slnx`), all modules. `src/Naravel.<Module>` holds a module, `src/Naravel.<Module>.<Provider>` a
driver with a heavy dependency, `tests/` the tests, `samples/` runnable samples. Versions live in `Directory.Packages.props`, shared build
settings and the single lock-step package version in `Directory.Build.props`. See `AGENTS.md` for the rules and current module status.

## Build & test
```
dotnet restore Naravel.slnx && dotnet build Naravel.slnx -c Release && dotnet test Naravel.slnx -c Release
```
Requires the .NET 10 SDK (latest LTS).


## Naravel.Queue (background jobs)

Driver-based job queue built on `Naravel.Foundation`: dispatch, delay, priority, retry with backoff,
chaining, batching, middleware, hosted worker, runtime `Extend`, config hot reload. Full docs with
examples: [`docs/en/queue.md`](docs/en/queue.md) / [`docs/fa/queue.md`](docs/fa/queue.md).

```csharp
builder.Services.AddQueue(builder.Configuration).AddRedisDriver(builder.Configuration);   // config section: "NaravelQueue:Stores"
builder.Services.AddQueueWorker(w => { w.Queues = new[] { "default" }; w.Concurrency = 4; });
await dispatcher.DispatchAsync(new SendWelcomeEmailJob("ali@example.com"));
```

**Delivery guarantee: at-least-once.** A job can run more than once (worker crash, visibility timeout, failed ack), so jobs must be
idempotent. Follow-up jobs of a chain are published *before* the job is acknowledged, so a chain is never silently lost.

**Crash recovery.** Redis, File and Database drivers return a job to the queue when its worker disappeared for longer than
`VisibilityTimeoutSeconds` (default 300; set it above your longest job). The Memory driver is in-process only. Kafka/RabbitMQ rely on
the broker's own redelivery.

**Known gaps.** Worker, Memory/File, Foundation integration, and provider contract tests are present. The provider suite lives in
`tests/Naravel.Queue.Providers.Tests`; service-backed Redis, RabbitMQ, Kafka, and Memcached cases run through CI when configured.
Database driver behavior on SQLite still needs review because EF Core's SQLite provider has limited `DateTimeOffset` translation.

## Naravel.Filesystem (named storage disks)

`Naravel.Filesystem` uses Foundation's driver manager for local and S3 storage, with config reload, runtime `Extend`, and manager-owned driver disposal. The local driver rejects paths outside its configured root. Configuration, upload, S3 URLs, and limitations: [docs/en/filesystem.md](docs/en/filesystem.md) / [docs/fa/filesystem.md](docs/fa/filesystem.md).

## Naravel.Cache (cache, tags, scopes and locks)

`Naravel.Cache` provides named Memory/Redis/Memcached stores, `RememberAsync`, per-user/tenant scoping, tag-version invalidation, and token-owned locks. Install only the provider packages you use. Configuration and provider limitations: [docs/en/cache.md](docs/en/cache.md) / [docs/fa/cache.md](docs/fa/cache.md).

## Naravel.Routing (Laravel-style routes and middleware)

A thin layer on ASP.NET Core: nested groups, named routes, `where`, route model binding, resource routes, and a middleware engine with aliases,
groups, parameters (`throttle:60,1`), priority, `withoutMiddleware`, controller attributes and terminable middleware. Details: [docs/en/routing.md](docs/en/routing.md).

```csharp
builder.Services.AddNaravelRouting(o => o.Middleware.Alias<EnsureAge>("age").Group("api", "bindings", "age:18"));
app.UseNaravelRouting();
app.MapNaravel(r => r.Prefix("admin").Name("admin.").Middleware("api").Group(g =>
    g.Get("users/{user}", (string user) => user).Name("users.show")));
```

**Status:** See [ROADMAP.md](ROADMAP.md) for the Stage 4a/4b status.
