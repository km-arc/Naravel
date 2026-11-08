# Naravel.Database.EntityFrameworkCore

EF Core interop for Naravel.Database: a DbContext and the query builder share one connection and one transaction.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Database.EntityFrameworkCore
```

Targets .NET 10. It depends on `Naravel.Database`, which NuGet installs with it.

## Use

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

For `dbContext.Query(...)` only EF Core needs registering. The query builder only joins the transaction that is current when each command runs, so begin and finish it on the context; it never commits or rolls back one it did not start. The builder bypasses EF change tracking: an entity you already loaded does not see a builder update until you reload it or read it with `AsNoTracking()`. Use the table and column names that EF maps. If the builder's queries should also reach your `IQueryListener`s and the query log, register `AddNaravelDatabase` as usual and use `naravelDatabase.Using(dbContext)`, which returns an `IDatabase` on the context's connection and transaction. SQLite, SQL Server, PostgreSQL (Npgsql) and MySQL are supported.

## Documentation

- Database: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/database.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
