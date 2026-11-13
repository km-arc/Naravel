# Naravel.Database.SqlServer

[![NuGet](https://img.shields.io/nuget/v/Naravel.Database.SqlServer.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Database.SqlServer)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Database.SqlServer.svg)](https://www.nuget.org/packages/Naravel.Database.SqlServer)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

SQL Server driver for Naravel.Database, built on Microsoft.Data.SqlClient.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Database.SqlServer` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Database`, which NuGet installs with it |
| **Driver** | `sqlserver` |

## What you get

- **A named connection per server.** Every connection whose `Driver` is `sqlserver` takes its own `ConnectionString`, so one application can use several SQL Server databases.
- **Pooling stays with the provider.** Each call leases a pooled `Microsoft.Data.SqlClient` connection.
- **The whole query builder.** Fluent queries, raw SQL and transactions from `Naravel.Database`, written in SQL Server's dialect.
- **Its limits are checked for you.** A statement over 2098 bound parameters throws an `InvalidOperationException` that names the limit and the count, and large inserts are split to stay under it.
- **SQL Server's own SQL.** `*ReturningAsync` writes `output inserted`, and `UpsertAsync` writes `merge ... with (holdlock)`.

## Quick start

```json
{ "Database": { "Default": "main", "Connections": {
  "main": { "Driver": "sqlserver", "ConnectionString": "Server=localhost,1433;Database=app;User Id=app" } } } }
```

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);         // from Naravel.Database; the order of the two calls does not matter
builder.Services.AddNaravelSqlServerDatabase(builder.Configuration);
```

`ConnectionString` is required: without it the first use fails with an `InvalidOperationException` that names the key. Keep the password out of the file, for example in user secrets or the variable `Database__Connections__main__ConnectionString`. `TrustServerCertificate=true` is for a local or test server with a self-signed certificate; do not use it against production. `InsertOrIgnoreAsync` throws `NotSupportedException` on SQL Server.

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
