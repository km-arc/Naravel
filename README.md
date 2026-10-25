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

Async background jobs with named drivers, delayed and prioritized dispatch, retries and backoff, chaining, batches, failed-job handling, hosted workers, and telemetry. Jobs are **at-least-once** and should be idempotent. The Memory driver is process-local; Redis, File, and Database use visibility timeouts for recovery, while Kafka and RabbitMQ use broker redelivery. Kafka does not support delayed delivery (delayed dispatch is rejected; retries are immediate); see [docs/en/queue.md](docs/en/queue.md#kafka-no-delayed-delivery).

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

### Naravel.Mail

Typed mail without a network dependency in the core package: a `Mailable` class describes the message, a named transport (`log` by default, `array` for tests, `smtp` via the optional `Naravel.Mail.Smtp` package) delivers it, and `MailFake` makes it testable. A transport failure is reported to the caller and never retried silently.

```csharp
builder.Services.AddNaravelMail(builder.Configuration);   // Mail:Default defaults to "log"

public class WelcomeMail(User user) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.From("app@example.com").Subject("Welcome").Text($"Hi {user.Name}").Build();
}

await mailer.To(user.Email).SendAsync(new WelcomeMail(user), cancellationToken);

// optional Naravel.Mail.Queue: services.AddNaravelMailQueue(); then
await mailer.To(user.Email).QueueAsync(new WelcomeMail(user), cancellationToken);   // stores message data only, at-least-once
```

See [docs/en/mail.md](docs/en/mail.md) and [docs/fa/mail.md](docs/fa/mail.md).

### Naravel.Notifications

One typed notification sent to a recipient through channels you name, in the order you name them, with a fake for tests. The core package has no mail, queue or network dependency; Mail (`Naravel.Notifications.Mail`) and Queue (`Naravel.Notifications.Queue`) are opt-in packages, and any other channel is a class or delegate. A missing channel fails before anything is sent; a failing channel stops the dispatch and `NotificationDeliveryException` says which channel failed and which had already delivered.

```csharp
builder.Services.AddNaravelNotifications();
builder.Services.AddLogNotificationChannel<InvoiceReady>();          // a channel named "log"

await notifications.SendAsync(user, new InvoiceReady(7), ["log"], cancellationToken);   // notifications is an injected INotificationSender

// optional Naravel.Notifications.Mail: services.AddMailNotificationChannel<InvoiceReady>(c => c.GetRecipient<User>().Email, n => new InvoiceMail(n));
// optional Naravel.Notifications.Queue: services.AddNaravelNotificationQueue(); services.AddQueuedNotification<InvoicePayload>("invoice.ready", handler);
//   await notificationQueue.QueueAsync("invoice.ready", payload);   // stores an alias and plain data only, at-least-once
```

See [docs/en/notifications.md](docs/en/notifications.md) and [docs/fa/notifications.md](docs/fa/notifications.md).

### Naravel.Encryption

A thin `Encrypt` / `Decrypt` facade over ASP.NET Core Data Protection (no custom cryptography), typed values, `ForPurpose` isolation and `DecryptException`. `Naravel.Queue.Encryption` stores the payload of jobs marked `IShouldBeEncrypted` encrypted; the job type is still chosen by the alias registry only. **Producer and worker must share the Data Protection key ring and application name.** See [docs/en/encryption.md](docs/en/encryption.md) and [docs/fa/encryption.md](docs/fa/encryption.md).

```csharp
builder.Services.AddNaravelEncryption();
var token = encrypter.Encrypt("secret");
var text = encrypter.Decrypt(token);
// optional Naravel.Queue.Encryption: services.AddNaravelQueueEncryption(); class MyJob : Job, IShouldBeEncrypted
```

### Naravel.Scheduling

Named tasks on a schedule, run by a hosted service inside your host: no cron line and no command to start. Frequencies and filters read like Laravel's (`EveryFiveMinutes`, `DailyAt`, `Weekdays`, `Cron`), and two guards work across several instances over the cache locks: `WithoutOverlapping` and `OnOneServer` (startup fails when the lock driver is not shared). Each run gets a new DI scope; `SchedulerFake` runs a schedule at times you choose. The Artisan half is not a package: Commands are a documented `System.CommandLine` recipe (PDR-017).

```csharp
builder.Services.AddNaravelScheduling(schedule =>
{
    schedule.Call("prune-sessions", (sp, ct) => sp.GetRequiredService<SessionPruner>().RunAsync(ct)).EveryFiveMinutes().WithoutOverlapping();
    schedule.Call("daily-digest", (sp, ct) => sp.GetRequiredService<Digest>().SendAsync(ct)).DailyAt(8, 0).OnOneServer();
});
```

See [docs/en/scheduling.md](docs/en/scheduling.md) and [docs/fa/scheduling.md](docs/fa/scheduling.md).

### Naravel.Database

A Laravel-style query builder with named connections and parameter-bound SQL on SQLite, MySQL, PostgreSQL and SQL Server (Dapper maps rows; EF Core stays the tool for entities and migrations). **Today:** connections, the SQLite, MySQL, PostgreSQL and SQL Server drivers, the builder (select, the full where family: value, null, between, column, date part, any/all/none, pattern, full text, JSON paths, exists and subqueries; conditionals; joins; group by and having; ordering helpers; limit; aggregates, chunking and streaming; unions, subqueries and lateral joins, CTEs, raw expressions with bindings, row locks, `ToSql`/`ToRawSql`) that compiles for all four dialects and runs on all four (one shared contract suite in CI), and observability (a `Naravel.Database` meter and activity with no SQL in tags, `IQueryListener`, an opt-in query log) with a `DatabaseFake` for tests. Aggregates (`CountAsync`, `SumAsync`, `ExistsAsync`...), chunking and `IAsyncEnumerable` streaming are included, and so are writes: `InsertAsync`, `InsertGetIdAsync`, `UpdateAsync`, `IncrementAsync`, `UpsertAsync`, `DeleteAsync`, `TruncateAsync` and `*ReturningAsync`, with an explicit `where` required for update and delete; transactions (`TransactionAsync` with savepoints and deadlock retry, `BeginTransactionAsync`, joined by the injected `IDatabase`) and an opt-in EF Core adapter (`Naravel.Database.EntityFrameworkCore`: `dbContext.Query("table")` and `db.Using(dbContext)` share the context's connection and transaction); pagination integration follows in a later stage (see [roadmap/QUERY-BUILDER-COVERAGE.md](roadmap/QUERY-BUILDER-COVERAGE.md)).

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);
builder.Services.AddNaravelSqliteDatabase(builder.Configuration);   // Database:Connections:main = { Driver: sqlite, Path: app.db }

var adults = await db.Table("users").Where("active", true).Where("age", ">=", 18).OrderBy("name").GetAsync(cancellationToken);
var ada    = await db.Table("users").Where("email", email).FirstAsync<UserRow>(cancellationToken);   // email is bound, never concatenated

var authors = await db.Table("users")                       // joins, JSON paths, exists, grouping: the same Laravel names
    .Join("posts", "users.id", "posts.user_id")
    .WhereLike("users.name", "a%")
    .Where("users.settings->theme", "dark")                  // JSON path, bound as a parameter
    .WhereExists(s => s.Table("comments").WhereColumn("comments.post_id", "posts.id"))
    .When(onlyRecent, q => q.Latest("posts.created_at"))
    .Select("users.name").GroupBy("users.name")
    .GetAsync(cancellationToken);
```

Values are always bound as parameters, identifiers are validated, and raw SQL exists only in `*Raw` methods. See [docs/en/database.md](docs/en/database.md) and [docs/fa/database.md](docs/fa/database.md).

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
| `Naravel.Mail` | `builder.Services.AddNaravelMail(configuration);` |
| `Naravel.Mail.Queue` | `builder.Services.AddNaravelMailQueue();` |
| `Naravel.Mail.Smtp` | `builder.Services.AddNaravelMailSmtp(configuration);` |
| `Naravel.Notifications` | `builder.Services.AddNaravelNotifications();` |
| `Naravel.Notifications.Mail` | `builder.Services.AddMailNotificationChannel<TNotification>(address, mailable);` |
| `Naravel.Notifications.Queue` | `builder.Services.AddNaravelNotificationQueue();` |
| `Naravel.Encryption` | `builder.Services.AddNaravelEncryption();` |
| `Naravel.Queue.Encryption` | `builder.Services.AddNaravelQueueEncryption();` |
| `Naravel.Scheduling` | `builder.Services.AddNaravelScheduling(schedule => schedule.Call("name", handler).EveryFiveMinutes());` |
| `Naravel.Database` | `builder.Services.AddNaravelDatabase(configuration);` |
| `Naravel.Database.Sqlite` | `builder.Services.AddNaravelSqliteDatabase(configuration);` |
| `Naravel.Database.MySql` | `builder.Services.AddNaravelMySqlDatabase(configuration);` |
| `Naravel.Database.Postgres` | `builder.Services.AddNaravelPostgresDatabase(configuration);` |
| `Naravel.Database.SqlServer` | `builder.Services.AddNaravelSqlServerDatabase(configuration);` |

Queue registrations are extension methods in `Naravel.Queue.Extensions`. The Queue configuration uses `NaravelQueue:Stores`; Cache and Filesystem use `Cache:Stores` and `Filesystem:Stores` by default. Each supports a custom section name.

### Hashing (native, no package)

Password hashing is already in .NET: `PasswordHasher<TUser>` from `Microsoft.Extensions.Identity.Core` does `Hash::make`, `Hash::check` and `Hash::needsRehash` (PDR-019, `DECLINED`). The one thing to change is the work factor: the default of 100,000 iterations is below the OWASP figure for HMAC-SHA512, so set `PasswordHasherOptions.IterationCount` yourself. See [docs/en/hashing.md](docs/en/hashing.md) and [docs/fa/hashing.md](docs/fa/hashing.md).

### Pagination

`Naravel.Pagination` (no EF Core dependency) returns Laravel-shaped pages with `data`, `links` and `meta`; `Naravel.Pagination.EntityFrameworkCore` adds `PaginateAsync`, `SimplePaginateAsync` and `CursorPaginateAsync` to EF Core queries. Cursor paging declares its keys once and writes both the `ORDER BY` and the `WHERE`; a cursor is an unsigned position, read strictly (`InvalidCursorException`), and page sizes are capped at 100.

```csharp
var page = await db.Posts.OrderBy(p => p.Id).PaginateAsync(page: 2, perPage: 15);
var next = await db.Posts.CursorPaginateAsync(k => k.By(p => p.CreatedAt).Then(p => p.Id), 15, cursor);
```

See [docs/en/pagination.md](docs/en/pagination.md) and [docs/fa/pagination.md](docs/fa/pagination.md).

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
| `/database` | The query builder on in-memory SQLite: conditions added per request parameter, bound values, a rejected column name, `WhereYear`, `FindAsync` and the query log |

See [samples/Naravel.Sample/README.md](samples/Naravel.Sample/README.md) for provider switching and demo headers.

## Build and test

Requires the .NET 10 SDK. From the repository root:

```sh
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

Naravel is a monorepo: `Naravel.slnx` contains all modules, providers, tests, and samples. Central package versions are in `Directory.Packages.props`; shared build settings and the lock-step package version are in `Directory.Build.props`.
