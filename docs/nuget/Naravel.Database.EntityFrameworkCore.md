# Naravel.Database.EntityFrameworkCore

[![NuGet](https://img.shields.io/nuget/v/Naravel.Database.EntityFrameworkCore.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Database.EntityFrameworkCore)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Database.EntityFrameworkCore.svg)](https://www.nuget.org/packages/Naravel.Database.EntityFrameworkCore)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

EF Core interop for Naravel.Database: a DbContext and the query builder share one connection and one transaction.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Database.EntityFrameworkCore` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Database` and `Microsoft.EntityFrameworkCore.Relational`, which NuGet installs with it, and the EF Core provider that your own `DbContext` uses |
| **Providers** | SQLite, SQL Server, PostgreSQL (Npgsql) and MySQL |

## What you get

- **A builder query on the context.** `dbContext.Query("table")` starts a query on the context's own connection and its current transaction.
- **One unit of work.** An EF `SaveChangesAsync` and a builder write commit or roll back together, in the transaction you begin on the context.
- **Atomic statements beside tracked entities.** Entities and change tracking stay in EF Core, while a guarded `DecrementAsync` or a bulk statement runs in the builder.
- **Listeners and the query log still see it.** `naravelDatabase.Using(dbContext)` returns an `IDatabase` on the context's connection and transaction.

## Quick start

```csharp
using Microsoft.EntityFrameworkCore;
using Naravel.Database.EntityFrameworkCore;   // adds dbContext.Query("table") and database.Using(dbContext)

// db is your own DbContext, "Accounts" is the table that EF maps for your Accounts DbSet, and fromId, toId and amount are yours
await using var transaction = await db.Database.BeginTransactionAsync(ct);   // EF Core owns the transaction: begin it before the first builder call

var debited = await db.Query("Accounts")
    .Where("Id", fromId)
    .Where("Balance", ">=", amount)
    .DecrementAsync("Balance", amount, cancellationToken: ct);               // one guarded, atomic UPDATE on the context's connection
if (debited == 0)
    throw new InvalidOperationException("Unknown account or insufficient funds.");   // no commit: disposing the transaction rolls back

await db.Query("Accounts").Where("Id", toId).IncrementAsync("Balance", amount, cancellationToken: ct);
await db.SaveChangesAsync(ct);                                               // EF Core changes are saved in the same transaction
await transaction.CommitAsync(ct);
```

For `dbContext.Query(...)` only EF Core needs registering. The query builder only joins the transaction that is current when each command runs, so begin and finish it on the context; it never commits or rolls back one it did not start. The builder bypasses EF change tracking: an entity you already loaded does not see a builder update until you reload it or read it with `AsNoTracking()`. Use the table and column names that EF maps. For `naravelDatabase.Using(dbContext)`, register `AddNaravelDatabase` as usual.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Database`](https://www.nuget.org/packages/Naravel.Database) | The query builder, named connections and raw SQL |

## Learn more

- Database: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
