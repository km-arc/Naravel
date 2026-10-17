# Database (query builder)

`Naravel.Database` is the Laravel-style query builder for .NET: named connections, a fluent builder, and parameter-bound SQL on SQLite, MySQL, PostgreSQL and SQL Server. EF Core stays the tool for entities and migrations; this package is for the "just run a query" path, with Dapper for fast execution and **no SqlKata**.

Decisions: [PDR-022](../pdr/en/PDR-022-database-query-builder.md). Persian version: [docs/fa/database.md](../fa/database.md).

> **What exists today.** Only the foundation is in place: connections, the manager, the SQLite driver and the connection seam. The fluent `Query` builder (`Table("users").Where(...).GetAsync()`) arrives in the next tasks of the same stage; the method-by-method plan is in [roadmap/QUERY-BUILDER-COVERAGE.md](../../roadmap/QUERY-BUILDER-COVERAGE.md). Nothing here pretends otherwise.

## Quickstart

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);
builder.Services.AddNaravelSqliteDatabase(builder.Configuration);   // call after AddNaravelDatabase or before, order does not matter
```

```json
{ "Database": { "Default": "main", "Connections": { "main": { "Driver": "sqlite", "Path": "app.db" } } } }
```

A connection takes either `Path` (a file, or `:memory:`) or a full `ConnectionString`. There is deliberately **no built-in default**: with nothing configured, asking for the default connection throws instead of silently creating a database.

## Using it

- `DatabaseManager.Connection(name)` returns the named connection definition (`IDatabaseConnection`: `Name`, `Dialect`, `OpenConnectionAsync`). Laravel: `DB::connection($name)`.
- `DatabaseManager.Extend(name, factory)` adds a connection at run time; changing `Database:Connections:<name>` in configuration rebuilds only that connection; unrelated keys change nothing.
- `IConnectionScope.AcquireAsync(name)` returns an `IConnectionLease` (`Connection`, `Transaction`, `OwnsConnection`) that every query goes through. Dispose the lease when done.
- `:memory:` databases keep their data while the application runs, and two connection names never share one.

## How it works and why

**Connection seam (PDR-022 A1).** Queries never open connections themselves; they ask an `IConnectionScope` for a lease. Today the scope is a plain pool (a fresh pooled connection per lease, no transaction, closed on dispose). Transactions (ambient scope and EF Core sharing) are added later by registering another scope, **without changing any signature**. This is why the seam is built first: a builder that opens its own connections cannot join a transaction afterwards without a breaking change.

**Why `Database:Connections` and not `Stores`.** It reads like Laravel's `config/database.php`. Internally it is the same dictionary the shared `Manager` uses, so `Extend`, reload and error messages behave exactly like Cache, Queue and Mail.

**In-memory SQLite.** SQLite deletes an in-memory database when its last connection closes, and the builder opens one connection per operation. The driver therefore switches `:memory:` to a uniquely named shared-cache database and keeps one connection open until the driver is disposed. Without this, a test that creates a table and then queries it would see an empty database.

**Security of the driver package.** `Microsoft.Data.Sqlite` brings `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, which has advisory CVE-2025-6965. The repository pins the patched 2.1.13 directly, because the build treats warnings as errors.

## Not ported on purpose

- `DB::__call` forwarding to the connection: inject the scope (and, from the execution task, `IDatabase`) instead.
- Reading and writing split connections, `purge()` and `reconnect()`: connections are pooled by the provider and rebuilt on configuration change.
