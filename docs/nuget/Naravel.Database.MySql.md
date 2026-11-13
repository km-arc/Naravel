# Naravel.Database.MySql

[![NuGet](https://img.shields.io/nuget/v/Naravel.Database.MySql.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Database.MySql)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Database.MySql.svg)](https://www.nuget.org/packages/Naravel.Database.MySql)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

MySQL driver for Naravel.Database, built on MySqlConnector.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Database.MySql` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Database`, which NuGet installs with it |
| **Driver** | `mysql` |

## What you get

- **A named connection per server.** Every connection whose `Driver` is `mysql` takes its own `ConnectionString`, so one application can use several MySQL databases.
- **Pooling you do not manage.** MySqlConnector pools connections, and the driver keeps none open: a finished call returns its connection to the pool.
- **The whole query builder.** Fluent queries, raw SQL and transactions from `Naravel.Database`, written in MySQL's dialect.
- **Differences written down.** The dialect guide lists, engine by engine, what the same call does on MySQL, SQLite, PostgreSQL and SQL Server.

## Quick start

```json
{ "Database": { "Default": "main", "Connections": {
  "main": { "Driver": "mysql", "ConnectionString": "Server=localhost;Port=3306;Database=app;User ID=app" } } } }
```

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);         // from Naravel.Database; the order of the two calls does not matter
builder.Services.AddNaravelMySqlDatabase(builder.Configuration);
```

`ConnectionString` is required: without it the first use fails with an `InvalidOperationException` that names the key. Keep the password out of the file, for example in user secrets or the variable `Database__Connections__main__ConnectionString`. With MySQL 8 and no TLS, `AllowPublicKeyRetrieval=true` in the connection string is needed; use it for local and CI servers only. `*ReturningAsync` throws `NotSupportedException` on MySQL, and `UpsertAsync` ignores `uniqueBy`: any unique key of the table can trigger the update.

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
