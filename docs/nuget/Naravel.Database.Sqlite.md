# Naravel.Database.Sqlite

[![NuGet](https://img.shields.io/nuget/v/Naravel.Database.Sqlite.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Database.Sqlite)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Database.Sqlite.svg)](https://www.nuget.org/packages/Naravel.Database.Sqlite)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

SQLite driver for Naravel.Database, built on Microsoft.Data.Sqlite.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Database.Sqlite` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Database`, which NuGet installs with it |
| **Driver** | `sqlite` |

## What you get

- **No server to run.** SQLite runs inside your process, from a file or in memory.
- **In-memory databases that last.** `:memory:` becomes a uniquely named shared-cache database that stays open until the driver is disposed, so every call sees the same data and two connection names never share one.
- **The whole query builder.** Fluent queries, raw SQL and transactions from `Naravel.Database`, written in SQLite's dialect.
- **Pooled connections.** Each call opens a pooled `Microsoft.Data.Sqlite` connection.

## Quick start

```json
{ "Database": { "Default": "main", "Connections": { "main": { "Driver": "sqlite", "Path": "app.db" } } } }
```

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);         // from Naravel.Database; the order of the two calls does not matter
builder.Services.AddNaravelSqliteDatabase(builder.Configuration);
```

A connection takes either `Path` (a file, or `:memory:`) or a full `ConnectionString`; with neither, the first use fails with an `InvalidOperationException` that names both keys. SQLite refuses `LockForUpdate`, `SharedLock` and `WhereFullText`.

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
