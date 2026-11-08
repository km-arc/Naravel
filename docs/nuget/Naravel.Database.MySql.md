# Naravel.Database.MySql

MySQL driver for Naravel.Database, built on MySqlConnector.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Database.MySql
```

Targets .NET 10. It depends on `Naravel.Database`, which NuGet installs with it.

## Use

```json
{ "Database": { "Default": "main", "Connections": {
  "main": { "Driver": "mysql", "ConnectionString": "Server=localhost;Port=3306;Database=app;User ID=app" } } } }
```

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);         // from Naravel.Database; the order of the two calls does not matter
builder.Services.AddNaravelMySqlDatabase(builder.Configuration);
```

The connection `Driver` is `mysql` and `ConnectionString` is required: without it the first use fails with an `InvalidOperationException` that names the key. The driver is built on MySqlConnector, which pools connections. Keep the password out of the file, for example in user secrets or the variable `Database__Connections__main__ConnectionString`. With MySQL 8 and no TLS, `AllowPublicKeyRetrieval=true` in the connection string is needed; use it for local and CI servers only.

## Documentation

- Database: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database.md)
- Database dialect differences: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database-dialects.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
