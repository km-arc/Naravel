# Naravel.Queue.Database

EF Core-backed queue driver for Naravel.Queue - works with any EF Core provider (SQL Server, PostgreSQL, SQLite, MySQL).

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Queue.Database
```

Targets .NET 10. It depends on `Naravel.Queue`, which NuGet installs with it.

## Use

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

// AppDbContext is your own context, on any EF Core provider (SQLite here)
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

`ConfigureQueueJobs()` maps the jobs, failed-jobs and batches tables. Nothing creates them on its own: add an EF Core migration and apply it, which is the production path for every provider. `VisibilityTimeoutSeconds` (default 300) is how long a job may stay reserved by a worker that has disappeared before it goes back to the queue. Every store that uses the `database` driver on the same context type shares one table. Failed jobs are kept in memory unless you add `builder.Services.AddDatabaseFailedJobStore<AppDbContext>()`.

## Documentation

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
