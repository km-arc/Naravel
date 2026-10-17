# Database (query builder)

`Naravel.Database` is the Laravel-style query builder for .NET: named connections, a fluent builder, and parameter-bound SQL on SQLite, MySQL, PostgreSQL and SQL Server. EF Core stays the tool for entities and migrations; this package is for the "just run a query" path, with Dapper for fast execution and **no SqlKata**.

Decisions: [PDR-022](../pdr/en/PDR-022-database-query-builder.md). Persian version: [docs/fa/database.md](../fa/database.md).

> **What exists today.** Connections, the manager, the SQLite driver, the connection seam, and the query builder that **compiles** SQL for all four dialects (`ToSql`, `ToRawSql`, `GetBindings`). Running the query (`GetAsync`, `FirstAsync`, ...) arrives in the next task of the same stage; joins, grouping and the extended where family follow. The method-by-method plan is in [roadmap/QUERY-BUILDER-COVERAGE.md](../../roadmap/QUERY-BUILDER-COVERAGE.md). Nothing here pretends otherwise.

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

## The query builder

```csharp
var query = Query.For(DatabaseDialect.Postgres)
    .Table("users")
    .Select("id", "name")
    .Where("active", true)
    .Where(g => g.Where("age", ">=", 18).OrWhere("vip", true))
    .OrderByDesc("created_at")
    .Limit(10).Offset(20);

query.ToSql();       // select "id", "name" from "users" where "active" = @p0 and ("age" >= @p1 or "vip" = @p2) order by "created_at" desc limit 10 offset 20
query.GetBindings(); // [true, 18, true]
```

Available now: `Table`, `Select`, `AddSelect`, `SelectRaw`, `Distinct`, `Where`/`OrWhere`/`WhereNot`/`OrWhereNot` (column and value, with an operator, an array of pairs, or a closure group), `WhereRaw`/`OrWhereRaw`, `WhereIn`/`WhereNotIn` and their `or` forms, `OrderBy`/`OrderByDesc`/`OrderByRaw`, `Limit`/`Take`, `Offset`/`Skip`, `Clone`, `Compile`, `ToSql`, `ToRawSql`, `GetBindings`.

**Safety rules, enforced at the call:**
- Values are **always** parameters (`@p0`, `@p1`, ...), on every dialect. Nothing a caller passes as a value can become SQL. `ToRawSql()` writes values in for logging only; never execute it.
- Identifiers (tables, columns, aliases) must match `[letter or _][letter, digit, _ or $]*`, at most 128 characters, in the forms `col`, `table.col`, `schema.table.col`, `*`, `table.*` and `source as alias`. Persian names work. Anything else throws `ArgumentException` immediately. The grammar is frozen by PDR-022 A3; loosening it needs a PDR amendment.
- Operators come from an allow-list. An operator another dialect has (for example `ilike` on SQLite) throws `NotSupportedException` instead of generating wrong SQL.
- SQL text is only possible through the `*Raw` methods (`?` is a bound parameter, `??` a literal question mark), so unsafe code is easy to grep for.
- Each statement is checked against the dialect's parameter limit (SQL Server 2098, SQLite 32766, MySQL and PostgreSQL 65535). Over the limit throws `InvalidOperationException` with the dialect, limit and count.

**Dialect differences handled for you:** quoting (`"x"`, `` `x` ``, `[x]`), `limit`/`offset` versus SQL Server `top (n)` and `offset ... fetch` (SQL Server requires `OrderBy` when `Offset` is used and says so), and offset without limit (`limit -1` on SQLite, a maximum `limit` on MySQL).

## How it works and why

**Why one `@pN` marker style everywhere.** SQLite, MySQL, PostgreSQL and SQL Server drivers all accept named parameters, and Dapper binds by name, so one style keeps every grammar identical and every test a plain string comparison. A positional `?` style would need a different marker per driver.

**Why a large `IN` list is not split.** The obvious fix (several `IN` groups joined by `OR`) does not reduce the parameter count, so the statement would still fail. The builder throws a clear error instead; a one-parameter strategy is proposed in PDR-022 A7.

**Why `Raw` is a method suffix, not a type, for now.** Every raw escape hatch is a `*Raw` method. A raw *value* type (Laravel's `DB::raw` inside `update`) is only needed for writes and arrives with them.

## How it works and why

**Connection seam (PDR-022 A1).** Queries never open connections themselves; they ask an `IConnectionScope` for a lease. Today the scope is a plain pool (a fresh pooled connection per lease, no transaction, closed on dispose). Transactions (ambient scope and EF Core sharing) are added later by registering another scope, **without changing any signature**. This is why the seam is built first: a builder that opens its own connections cannot join a transaction afterwards without a breaking change.

**Why `Database:Connections` and not `Stores`.** It reads like Laravel's `config/database.php`. Internally it is the same dictionary the shared `Manager` uses, so `Extend`, reload and error messages behave exactly like Cache, Queue and Mail.

**In-memory SQLite.** SQLite deletes an in-memory database when its last connection closes, and the builder opens one connection per operation. The driver therefore switches `:memory:` to a uniquely named shared-cache database and keeps one connection open until the driver is disposed. Without this, a test that creates a table and then queries it would see an empty database.

**Security of the driver package.** `Microsoft.Data.Sqlite` brings `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, which has advisory CVE-2025-6965. The repository pins the patched 2.1.13 directly, because the build treats warnings as errors.

## Not ported on purpose

- `DB::__call` forwarding to the connection: inject the scope (and, from the execution task, `IDatabase`) instead.
- Reading and writing split connections, `purge()` and `reconnect()`: connections are pooled by the provider and rebuilt on configuration change.
