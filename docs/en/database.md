# Database (query builder)

`Naravel.Database` is the Laravel-style query builder for .NET: named connections, a fluent builder, and parameter-bound SQL on SQLite, MySQL, PostgreSQL and SQL Server. EF Core stays the tool for entities and migrations; this package is for the "just run a query" path, with Dapper for fast execution and **no SqlKata**.

Decisions: [PDR-022](../pdr/en/PDR-022-database-query-builder.md). Persian version: [docs/fa/database.md](../fa/database.md).

> **What exists today.** Connections, the manager, the SQLite driver, the connection seam, the query builder that **compiles** SQL for all four dialects (`ToSql`, `ToRawSql`, `GetBindings`), and execution. **Running** it on SQLite (`GetAsync`, `FirstAsync`, `SoleAsync`, `ValueAsync`, `FindAsync`, `PluckAsync` and raw SQL through `IDatabase`) works too; observability (meter, activity, `IQueryListener`, opt-in query log) and the test fake `DatabaseFake` work too. Joins, grouping and the extended where family follow. The method-by-method plan is in [roadmap/QUERY-BUILDER-COVERAGE.md](../../roadmap/QUERY-BUILDER-COVERAGE.md). Nothing here pretends otherwise.

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

## Running queries

```csharp
public class UserService(IDatabase db)
{
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ActiveAsync(CancellationToken ct) =>
        db.Table("users").Where("active", true).OrderBy("name").GetAsync(ct);

    public Task<UserRow?> ByEmailAsync(string email, CancellationToken ct) =>
        db.Table("users").Where("email", email).FirstAsync<UserRow?>(ct);
}
```

`IDatabase` is registered by `AddNaravelDatabase` for the default connection; `db.Connection("reports")` gives one bound to another name. `db.Table(...)` returns a `Query` that is already connected. A query created with `Query.For(dialect)` only compiles SQL and throws a clear `InvalidOperationException` if you try to run it.

| Method | Returns | Laravel |
|---|---|---|
| `GetAsync()` / `GetAsync<T>()` | all rows, as dictionaries or as `T` | `get` |
| `FirstAsync()` / `FirstAsync<T>()` | the first row, or `null` / default | `first` |
| `SoleAsync()` / `SoleAsync<T>()` | exactly one row; `RecordsNotFoundException` for none, `MultipleRecordsFoundException` for more | `sole` |
| `ValueAsync<T>(column)` | one column of the first row, or default | `value` |
| `FindAsync(id, keyColumn = "id")` / `FindAsync<T>(...)` | the row with that key, or `null` / default | `find` |
| `PluckAsync<T>(column)` | one column of every row | `pluck` (no key argument yet) |

Raw SQL goes through `IDatabase` with `?` placeholders (each becomes a bound parameter; write `??` for a literal question mark; a wrong number of values throws `ArgumentException` before anything runs):

```csharp
var rows  = await db.SelectAsync("select * from users where votes > ?", [100], ct);        // Laravel: DB::select
var count = await db.ScalarAsync<long>("select count(*) from users", cancellationToken: ct);               // DB::scalar
await db.StatementAsync("update users set votes = ? where id = ?", [0, 7], ct);             // DB::statement
var rowsChanged = await db.AffectingStatementAsync("delete from users where votes = ?", [0], ct); // DB::affectingStatement
await db.UnpreparedAsync("create table t (id integer primary key)", ct);                    // DB::unprepared (no placeholders)
```

Things to know:
- **Two ways to read rows.** Without a type argument the rows are read straight from the `DbDataReader` into case-insensitive dictionaries (fast, trim and AOT safe; values keep the provider's raw type, for example SQLite returns integers as `long` and dates as text). With `<T>` Dapper maps the row, which is **not trim or AOT safe**. Naravel registers no global Dapper type handler; Dapper 2.1.86 and newer maps `DateOnly` and `TimeOnly` itself on .NET 8 and 10.
- **Parameter types.** `bool`, `decimal`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` and `byte[]` are bound with an explicit `DbType`; an enum is bound as its underlying integer; `null` is bound as `DBNull`.
- **Connections and transactions.** Each call takes a lease from the `IConnectionScope`, sets `command.Transaction` from it, and disposes it. The default scope opens and closes a pooled connection per call; a scope that lends a connection with a transaction (arriving with R25) is joined, never closed or committed. A test proves both behaviours today.
- `FirstAsync`, `SoleAsync`, `ValueAsync` and `FindAsync` work on a clone and add their own `limit`, so the original query is unchanged.
- `SoleAsync` fetches at most two rows. On SQL Server `limit` is written as `top (n)`, so no `OrderBy` is needed for these methods.

## Observability and testing

**Telemetry.** Every command is counted and timed. The meter and the activity source are both named `Naravel.Database`: counters `naravel.database.queries` and `naravel.database.failures`, histogram `naravel.database.query.duration` (ms), and one activity `naravel.database.query` per command. Tags are the **connection name and the operation kind** (`select`, `scalar`, `statement`) only. SQL text and values never go into telemetry, so it is safe to ship to a shared collector.

```csharp
builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter("Naravel.Database")).WithTracing(t => t.AddSource("Naravel.Database"));
```

**Listeners (`DB::listen`).** Implement `IQueryListener` and register it in DI; it receives a `QueryExecuted` (connection, operation, SQL with `@p0` markers, bindings, duration, exception) after every command, including failed ones. Listeners run on the calling thread, so keep them fast. An exception thrown by a listener is swallowed, so a broken logger can never fail a query. Listeners are resolved once, when `IDatabase` is first resolved.

**Query log (`enableQueryLog`).** Opt in with `services.AddNaravelDatabaseQueryLog()` and inject `QueryLog`: `Entries`, `Clear()`, `Enabled`. It keeps the last 1000 entries (configurable) and holds bound values, so use it in development and tests, not production.

**`DatabaseFake`.** For tests that do not need a database, inject `new DatabaseFake()` where `IDatabase` is expected. `Table(...)` compiles real SQL for the dialect you pass (SQLite by default), so you can assert on the SQL and the bindings:

```csharp
var fake = new DatabaseFake().ReturnRows([new Dictionary<string, object?> { ["id"] = 1 }], sqlContains: "from \"users\"");
var service = new UserService(fake);
await service.ActiveUsersAsync();
fake.AssertExecuted(q => q.Sql.Contains("votes") && (int)q.Bindings[0]! == 10);
```

Set results with `ReturnRows`, `ReturnObjects<T>` (for `GetAsync<T>`), `ReturnScalar` and `ReturnAffected`; the latest matching set-up wins, and anything not set up returns no rows, the default value or 0 affected rows. Assert with `AssertExecuted`, `AssertExecutedCount` and `AssertNothingExecuted`. The fake does not execute SQL, so behaviour that depends on a real engine needs a SQLite or server test.

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

## How the connection seam and drivers work

**Connection seam (PDR-022 A1).** Queries never open connections themselves; they ask an `IConnectionScope` for a lease. Today the scope is a plain pool (a fresh pooled connection per lease, no transaction, closed on dispose). Transactions (ambient scope and EF Core sharing) are added later by registering another scope, **without changing any signature**. This is why the seam is built first: a builder that opens its own connections cannot join a transaction afterwards without a breaking change.

**Why `Database:Connections` and not `Stores`.** It reads like Laravel's `config/database.php`. Internally it is the same dictionary the shared `Manager` uses, so `Extend`, reload and error messages behave exactly like Cache, Queue and Mail.

**In-memory SQLite.** SQLite deletes an in-memory database when its last connection closes, and the builder opens one connection per operation. The driver therefore switches `:memory:` to a uniquely named shared-cache database and keeps one connection open until the driver is disposed. Without this, a test that creates a table and then queries it would see an empty database.

**Security of the driver package.** `Microsoft.Data.Sqlite` brings `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, which has advisory CVE-2025-6965. The repository pins the patched 2.1.13 directly, because the build treats warnings as errors.

## Server drivers

| Package | `Driver` value | Provider | Registration |
|---|---|---|---|
| `Naravel.Database.Sqlite` | `sqlite` | Microsoft.Data.Sqlite | `AddNaravelSqliteDatabase` |
| `Naravel.Database.MySql` | `mysql` | MySqlConnector | `AddNaravelMySqlDatabase` |
| `Naravel.Database.Postgres` | `pgsql` | Npgsql | `AddNaravelPostgresDatabase` |
| `Naravel.Database.SqlServer` | `sqlserver` | Microsoft.Data.SqlClient | `AddNaravelSqlServerDatabase` |

```json
{ "Database": { "Default": "main", "Connections": {
  "main": { "Driver": "pgsql", "ConnectionString": "Host=localhost;Database=app;Username=app;Password=..." },
  "reports": { "Driver": "mysql", "ConnectionString": "Server=localhost;Database=reports;User ID=app;Password=..." } } } }
```

Install only the packages you use, register each after `AddNaravelDatabase`, and name connections freely: one application can use several databases at once (`db.Table("t", "reports")`). Keep passwords out of source: set `Database__Connections__main__ConnectionString` in the environment. Connection pooling is the provider's; a disposed lease returns its connection to the pool.

**Status:** these drivers are written and unit-tested for registration, reload and `Extend`. Running real queries against each server is proven by the shared contract suite of the next task and by the CI `database` job, which do not exist yet, so treat MySQL, PostgreSQL and SQL Server execution as **not yet verified** until then. Known per-server facts: SQL Server allows 2100 parameters per statement (the grammar stops at 2098) and requires `OrderBy` with `Offset`; PostgreSQL has `ilike` and regex operators, MySQL has `<=>`, `regexp` and `rlike`, and using one on another dialect throws `NotSupportedException`.

## Not ported on purpose

- `DB::__call` forwarding to the connection: inject `IDatabase` (or the scope) instead.
- `DB::select` returning `stdClass` objects: rows are dictionaries or Dapper-mapped types. `pretend()` is replaced by `DatabaseFake`, and `listen()` by `IQueryListener`.
- Reading and writing split connections, `purge()` and `reconnect()`: connections are pooled by the provider and rebuilt on configuration change.
