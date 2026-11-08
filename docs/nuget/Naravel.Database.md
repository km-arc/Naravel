# Naravel.Database

Laravel-style query builder for Naravel on ADO.NET and Dapper: named connections on Naravel.Foundation, a connection and transaction seam that EF Core can share, and per-database driver packages.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Database
```

Targets .NET 10. It depends on `Naravel.Foundation` and `Naravel.Pagination`, which NuGet installs with it.

## Use

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

`Default` names the connection that `IDatabase` uses, and `db.Connection("reports")` gives one bound to another connection, so one application can use several databases. The `Driver` of a connection is `sqlite`, `mysql`, `pgsql` or `sqlserver`, and each value needs its own driver package (listed below). Rows come back as dictionaries, or as your own type with `GetAsync<T>()`, which maps through Dapper and is not trim or AOT safe. Raw SQL goes through `db.SelectAsync(sql, bindings, ct)` with `?` placeholders, each one bound as a parameter.

## Related packages

- [`Naravel.Database.EntityFrameworkCore`](https://www.nuget.org/packages/Naravel.Database.EntityFrameworkCore): EF Core interop for Naravel.Database: a DbContext and the query builder share one connection and one transaction.
- [`Naravel.Database.MySql`](https://www.nuget.org/packages/Naravel.Database.MySql): MySQL driver for Naravel.Database, built on MySqlConnector.
- [`Naravel.Database.Postgres`](https://www.nuget.org/packages/Naravel.Database.Postgres): PostgreSQL driver for Naravel.Database, built on Npgsql.
- [`Naravel.Database.SqlServer`](https://www.nuget.org/packages/Naravel.Database.SqlServer): SQL Server driver for Naravel.Database, built on Microsoft.Data.SqlClient.
- [`Naravel.Database.Sqlite`](https://www.nuget.org/packages/Naravel.Database.Sqlite): SQLite driver for Naravel.Database, built on Microsoft.Data.Sqlite.

## Documentation

- Database: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
