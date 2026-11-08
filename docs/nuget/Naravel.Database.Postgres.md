# Naravel.Database.Postgres

PostgreSQL driver for Naravel.Database, built on Npgsql.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Database.Postgres
```

Targets .NET 10. It depends on `Naravel.Database`, which NuGet installs with it.

## Use

```json
{ "Database": { "Default": "main", "Connections": {
  "main": { "Driver": "pgsql", "ConnectionString": "Host=localhost;Port=5432;Database=app;Username=app" } } } }
```

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);         // from Naravel.Database; the order of the two calls does not matter
builder.Services.AddNaravelPostgresDatabase(builder.Configuration);
```

The connection `Driver` is `pgsql` and `ConnectionString` is required: without it the first use fails with an `InvalidOperationException` that names the key. The driver is built on Npgsql, which pools connections. Keep the password out of the file, for example in user secrets or the variable `Database__Connections__main__ConnectionString`. Note that the `Driver` value is `pgsql`, not `postgres`.

## Documentation

- Database: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database.md)
- Database dialect differences: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database-dialects.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
