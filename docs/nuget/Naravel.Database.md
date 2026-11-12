# Naravel.Database

[![NuGet](https://img.shields.io/nuget/v/Naravel.Database.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Database)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Database.svg)](https://www.nuget.org/packages/Naravel.Database)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Laravel-style query builder for Naravel on ADO.NET and Dapper: named connections on Naravel.Foundation, a connection and transaction seam that EF Core can share, and per-database driver packages.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Database` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Foundation` and `Naravel.Pagination`, which NuGet installs with it |
| **Drivers** | SQLite, MySQL, PostgreSQL and SQL Server, one package each (listed below) |

## What you get

- **A fluent query builder** on ADO.NET and Dapper: `db.Table("users").Where("active", true).OrderBy("name")`, with the values bound as parameters.
- **Named connections.** `Default` names the connection `IDatabase` uses and `db.Connection("reports")` gives another, so one application can use several databases.
- **Four databases.** The `Driver` of a connection is `sqlite`, `mysql`, `pgsql` or `sqlserver`, and each value has its own driver package.
- **One transaction with EF Core.** With `Naravel.Database.EntityFrameworkCore`, a `DbContext` write and a query-builder write commit or roll back together.
- **Raw SQL and a query log.** `?` placeholders become bound parameters, and the opt-in `AddNaravelDatabaseQueryLog()` keeps the last queries for development and tests.

## Quick start

The connection `Driver` picks a driver package, which you install next to this one; the sample uses SQLite:

```bash
dotnet add package Naravel.Database.Sqlite
```

```json
{ "Database": { "Default": "main", "Connections": { "main": { "Driver": "sqlite", "Path": "app.db" } } } }
```

```csharp
using Naravel.Database;

builder.Services.AddNaravelDatabase(builder.Configuration);         // reads the "Database" section and registers IDatabase for the default connection
builder.Services.AddNaravelSqliteDatabase(builder.Configuration);   // the driver package that the Driver of the connection names; this package has none

public sealed class UserService(IDatabase db)
{
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ActiveAsync(CancellationToken ct) =>
        db.Table("users").Where("active", true).OrderBy("name").GetAsync(ct);
}
```

Rows come back as dictionaries, or as your own type with `GetAsync<T>()`, which maps through Dapper and is not trim or AOT safe. Raw SQL goes through `db.SelectAsync(sql, bindings, ct)` with `?` placeholders, each one bound as a parameter.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Database.EntityFrameworkCore`](https://www.nuget.org/packages/Naravel.Database.EntityFrameworkCore) | A DbContext and the query builder share one connection and one transaction |
| [`Naravel.Database.MySql`](https://www.nuget.org/packages/Naravel.Database.MySql) | MySQL driver on MySqlConnector |
| [`Naravel.Database.Postgres`](https://www.nuget.org/packages/Naravel.Database.Postgres) | PostgreSQL driver on Npgsql |
| [`Naravel.Database.SqlServer`](https://www.nuget.org/packages/Naravel.Database.SqlServer) | SQL Server driver on Microsoft.Data.SqlClient |
| [`Naravel.Database.Sqlite`](https://www.nuget.org/packages/Naravel.Database.Sqlite) | SQLite driver on Microsoft.Data.Sqlite |

## Learn more

- Database: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
