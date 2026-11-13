# Naravel.Database.Postgres

[![NuGet](https://img.shields.io/nuget/v/Naravel.Database.Postgres.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Database.Postgres)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Database.Postgres.svg)](https://www.nuget.org/packages/Naravel.Database.Postgres)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

PostgreSQL driver for Naravel.Database, built on Npgsql.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Database.Postgres` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Database`, which NuGet installs with it |
| **Driver** | `pgsql`, not `postgres` |

## What you get

- **A named connection per server.** Every connection whose `Driver` is `pgsql` takes its own `ConnectionString`, so one application can use several PostgreSQL databases.
- **Npgsql's own pooling.** Each connection holds one `NpgsqlDataSource`, Npgsql's recommended entry point, created without touching the network and disposed with the driver.
- **The whole query builder.** Fluent queries, raw SQL and transactions from `Naravel.Database`, written in PostgreSQL's dialect.
- **PostgreSQL's own SQL.** `UpsertAsync` writes `on conflict (uniqueBy)`, which needs a unique index on those columns, and `*ReturningAsync` writes `returning`.

## Quick start

```json
{ "Database": { "Default": "main", "Connections": {
  "main": { "Driver": "pgsql", "ConnectionString": "Host=localhost;Port=5432;Database=app;Username=app" } } } }
```

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);         // from Naravel.Database; the order of the two calls does not matter
builder.Services.AddNaravelPostgresDatabase(builder.Configuration);
```

`ConnectionString` is required: without it the first use fails with an `InvalidOperationException` that names the key. Keep the password out of the file, for example in user secrets or the variable `Database__Connections__main__ConnectionString`. PostgreSQL refuses a `Having` on a select alias.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Database`](https://www.nuget.org/packages/Naravel.Database) | The query builder and named connections that this driver plugs into |
| [`Naravel.Database.EntityFrameworkCore`](https://www.nuget.org/packages/Naravel.Database.EntityFrameworkCore) | A DbContext and the query builder share one connection and one transaction |

## Learn more

- Database: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database.md)
- Database dialect differences: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database-dialects.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
