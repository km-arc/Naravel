# Getting started

Which package to install, which call registers it, and how to run the sample app. The short version is in the [README](../../README.md); every module has its own guide next to this page.

## Install

Naravel targets **.NET 10** (`net10.0`). Each module is its own NuGet package, and every package has the same version. Add the modules you use; their dependencies, `Naravel.Foundation` included, come with them:

```sh
dotnet add package Naravel.Cache          # a core module
dotnet add package Naravel.Cache.Redis    # a provider, only if you use Redis
```

Keep all Naravel packages in a project on one version: they are built and tested together.

## Which packages do I need

| You want | Install | Then register |
|---|---|---|
| Cache | `Naravel.Cache`; add `Naravel.Cache.Redis` or `Naravel.Cache.Memcached` for those stores | `AddNaravelCache(configuration)` |
| Queue | `Naravel.Queue` and one driver: `Naravel.Queue.Memory`, `.File`, `.Redis`, `.Database`, `.RabbitMQ` or `.Kafka` | `AddQueue(configuration)` and the driver's `Add...Driver` |
| Queue coordination (no overlap, rate limits, unique jobs) | `Naravel.Queue.Coordination` | see [queue-coordination](queue-coordination.md) |
| Events | `Naravel.Events`; add `Naravel.Events.Queue` for queued listeners | `AddNaravelEvents()` |
| Mail | `Naravel.Mail`; add `Naravel.Mail.Smtp` to send over SMTP and `Naravel.Mail.Queue` for queued mail | `AddNaravelMail(configuration)` |
| Notifications | `Naravel.Notifications`; add `Naravel.Notifications.Mail` and `Naravel.Notifications.Queue` as needed | `AddNaravelNotifications()` |
| Query builder | `Naravel.Database` and one dialect: `Naravel.Database.Sqlite`, `.MySql`, `.Postgres` or `.SqlServer`; `Naravel.Database.EntityFrameworkCore` to share an EF Core context's connection | `AddNaravelDatabase(configuration)` and the dialect's `AddNaravel...Database` |
| Pagination | `Naravel.Pagination`; `Naravel.Pagination.EntityFrameworkCore` for EF Core queries | none |
| Files | `Naravel.Filesystem` | `AddNaravelFilesystem(configuration)` |
| Routing | `Naravel.Routing` (ASP.NET Core) | `AddNaravelRouting()` |
| Encryption | `Naravel.Encryption`; add `Naravel.Queue.Encryption` for encrypted jobs | `AddNaravelEncryption()` |
| Scheduling | `Naravel.Scheduling` | `AddNaravelScheduling(...)` |
| Broadcasting | `Naravel.Broadcasting` (ASP.NET Core, SignalR) | `AddNaravelBroadcasting(configuration)` |
| Redis connections | `Naravel.Redis` (`Naravel.Cache.Redis` and `Naravel.Queue.Redis` already depend on it) | `AddNaravelRedis(configuration)` |

The [Provider packages](#provider-packages) table below lists every registration call, and each module section in [Modules](modules.md) starts with a short example. Prefer to see it run first? Use the [sample app](#run-the-sample-app).

## Provider packages

Register the core package and only the providers your configured stores use. The snippets below are alternatives; the Database driver also requires an EF Core context and `ConfigureQueueJobs()` model mapping. Provider configuration and requirements are in the linked module docs.

| Package | Example registration |
|---|---|
| `Naravel.Cache` (Memory) | `builder.Services.AddNaravelCache(configuration);` |
| `Naravel.Redis` | `builder.Services.AddNaravelRedis(configuration);` |
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
| `Naravel.Broadcasting` | `builder.Services.AddNaravelBroadcasting(configuration); app.MapNaravelBroadcasting("/hubs/broadcast");` |
| `Naravel.Database` | `builder.Services.AddNaravelDatabase(configuration);` |
| `Naravel.Database.Sqlite` | `builder.Services.AddNaravelSqliteDatabase(configuration);` |
| `Naravel.Database.MySql` | `builder.Services.AddNaravelMySqlDatabase(configuration);` |
| `Naravel.Database.Postgres` | `builder.Services.AddNaravelPostgresDatabase(configuration);` |
| `Naravel.Database.SqlServer` | `builder.Services.AddNaravelSqlServerDatabase(configuration);` |

Queue registrations are extension methods in `Naravel.Queue.Extensions`. The Queue configuration uses `NaravelQueue:Stores`; Cache and Filesystem use `Cache:Stores` and `Filesystem:Stores` by default. Each supports a custom section name.

## Run the sample app

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

See [samples/Naravel.Sample/README.md](../../samples/Naravel.Sample/README.md) for provider switching and demo headers.

## Build from source

Requires the .NET 10 SDK. From the repository root:

```sh
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

Naravel is a monorepo: `Naravel.slnx` contains all modules, providers, tests, and samples. Central package versions are in `Directory.Packages.props`; shared build settings and the lock-step package version are in `Directory.Build.props`.
