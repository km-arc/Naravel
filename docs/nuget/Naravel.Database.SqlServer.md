# Naravel.Database.SqlServer

SQL Server driver for Naravel.Database, built on Microsoft.Data.SqlClient.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Database.SqlServer
```

Targets .NET 10. It depends on `Naravel.Database`, which NuGet installs with it.

## Use

```json
{ "Database": { "Default": "main", "Connections": {
  "main": { "Driver": "sqlserver", "ConnectionString": "Server=localhost,1433;Database=app;User Id=app" } } } }
```

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);         // from Naravel.Database; the order of the two calls does not matter
builder.Services.AddNaravelSqlServerDatabase(builder.Configuration);
```

The connection `Driver` is `sqlserver` and `ConnectionString` is required: without it the first use fails with an `InvalidOperationException` that names the key. The driver is built on Microsoft.Data.SqlClient, which pools connections. Keep the password out of the file, for example in user secrets or the variable `Database__Connections__main__ConnectionString`. `TrustServerCertificate=true` is for a local or test server with a self-signed certificate; do not use it against production.

## Documentation

- Database: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database.md)
- Database dialect differences: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database-dialects.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
