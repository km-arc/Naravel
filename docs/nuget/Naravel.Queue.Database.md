# Naravel.Queue.Database

[![NuGet](https://img.shields.io/nuget/v/Naravel.Queue.Database.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Queue.Database)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Queue.Database.svg)](https://www.nuget.org/packages/Naravel.Queue.Database)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

EF Core-backed queue driver for Naravel.Queue - works with any EF Core provider (SQL Server, PostgreSQL, SQLite, MySQL).

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Queue.Database` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Queue`, `Microsoft.EntityFrameworkCore` and `Microsoft.EntityFrameworkCore.Relational`, which NuGet installs with it, and the EF Core provider of your own database |
| **Driver** | `database`, added with `AddDatabaseDriver<TContext>` |

## What you get

- **Your own database is the queue.** `ConfigureQueueJobs()` maps the jobs, failed-jobs and batches tables onto your `DbContext`.
- **No locking hints.** A worker claims a job with a compare-and-swap update, so two workers cannot take the same row on any provider.
- **Priorities and delays.** Jobs come out by priority, then by the time they become available.
- **Fenced reservations.** A worker that outlives its visibility timeout cannot acknowledge the copy that another worker now owns; the job simply runs a second time.
- **Dispatch inside your transaction.** `AddTransactionalEnqueue()` lets one `SaveChanges` commit an order and its job together, or neither.

## Quick start

```json
{
  "NaravelQueue": {
    "Default": "db",
    "Stores": { "db": { "Driver": "database" } }
  }
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Naravel.Queue.Database;
using Naravel.Queue.Extensions;

// AppDbContext is your own context. UseSqlite comes from the Microsoft.EntityFrameworkCore.Sqlite package, which
// Naravel.Queue.Database does not install: use the provider package of your own database instead
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=queue.db"));
builder.Services.AddQueue(builder.Configuration);
builder.Services.AddDatabaseDriver<AppDbContext>(builder.Configuration);
builder.Services.AddQueueWorker();

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<JobRecord> QueueJobs => Set<JobRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ConfigureQueueJobs();
}
```

Nothing creates the tables on its own: add an EF Core migration and apply it, which is the production path for every provider. `VisibilityTimeoutSeconds` (default 300) is how long a job may stay reserved by a worker that has disappeared before it goes back to the queue. Every store that uses the `database` driver on the same context type shares one table. Failed jobs are kept in memory unless you add `builder.Services.AddDatabaseFailedJobStore<AppDbContext>()`.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Queue`](https://www.nuget.org/packages/Naravel.Queue) | Jobs, retries with backoff, chaining, batching and the background worker |

## Learn more

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
