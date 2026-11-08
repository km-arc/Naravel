# Naravel.Database.Sqlite

SQLite driver for Naravel.Database, built on Microsoft.Data.Sqlite.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Database.Sqlite
```

Targets .NET 10. It depends on `Naravel.Database`, which NuGet installs with it.

## Use

```json
{ "Database": { "Default": "main", "Connections": { "main": { "Driver": "sqlite", "Path": "app.db" } } } }
```

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);         // from Naravel.Database; the order of the two calls does not matter
builder.Services.AddNaravelSqliteDatabase(builder.Configuration);
```

A connection takes either `Path` (a file, or `:memory:`) or a full `ConnectionString`; with neither, the first use fails with an `InvalidOperationException` that names both keys. The connection `Driver` is `sqlite`.

## Documentation

- Database: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database.md)
- Database dialect differences: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database-dialects.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
