# Naravel.Database

Laravel-style query builder for Naravel on ADO.NET and Dapper: named connections on Naravel.Foundation, a connection and transaction seam that EF Core can share, and per-database driver packages.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Database
```

Targets .NET 10. It depends on `Naravel.Foundation` and `Naravel.Pagination`, which NuGet installs with it.

## Related packages

- [`Naravel.Database.EntityFrameworkCore`](https://www.nuget.org/packages/Naravel.Database.EntityFrameworkCore): EF Core interop for Naravel.Database: a DbContext and the query builder share one connection and one transaction.
- [`Naravel.Database.MySql`](https://www.nuget.org/packages/Naravel.Database.MySql): MySQL driver for Naravel.Database, built on MySqlConnector.
- [`Naravel.Database.Postgres`](https://www.nuget.org/packages/Naravel.Database.Postgres): PostgreSQL driver for Naravel.Database, built on Npgsql.
- [`Naravel.Database.SqlServer`](https://www.nuget.org/packages/Naravel.Database.SqlServer): SQL Server driver for Naravel.Database, built on Microsoft.Data.SqlClient.
- [`Naravel.Database.Sqlite`](https://www.nuget.org/packages/Naravel.Database.Sqlite): SQLite driver for Naravel.Database, built on Microsoft.Data.Sqlite.

## Documentation

- Database: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database.md) · [راهنمای فارسی](https://github.com/km-arc/Naravel/blob/main/docs/fa/database.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
