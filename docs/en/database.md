# Database (query builder)

`Naravel.Database` is the Laravel-style query builder for .NET: named connections, a fluent builder, and parameter-bound SQL on SQLite, MySQL, PostgreSQL and SQL Server. EF Core stays the tool for entities and migrations; this package is for the "just run a query" path, with Dapper for fast execution and **no SqlKata**.

Decisions: [PDR-022](../pdr/en/PDR-022-database-query-builder.md). Persian version: [docs/fa/database.md](../fa/database.md).

> **What exists today.** Connections, the manager, the SQLite, MySQL, PostgreSQL and SQL Server drivers, the connection seam, the query builder that **compiles** SQL for all four dialects (`ToSql`, `ToRawSql`, `GetBindings`), and execution. **Running** it (`GetAsync`, `FirstAsync`, `SoleAsync`, `ValueAsync`, `FindAsync`, `PluckAsync` and raw SQL through `IDatabase`) works on all four databases, proven by one shared contract suite in CI; observability (meter, activity, `IQueryListener`, opt-in query log) and the test fake `DatabaseFake` work too. The where family (value, null, between, column, date part, any/all/none, pattern, full text, JSON, exists and subqueries), conditionals, joins, grouping, having and the ordering helpers all compile for the four dialects and run on them in CI, as do aggregates and existence (`CountAsync`, `MinAsync`, `MaxAsync`, `SumAsync`, `AvgAsync`, `ExistsAsync`), chunking (`ChunkAsync`, `ChunkByIdAsync`, `EachAsync`) and streaming (`CursorAsync`, `LazyAsync`, `LazyByIdAsync`); writes and transactions follow. The method-by-method plan is in [roadmap/QUERY-BUILDER-COVERAGE.md](../../roadmap/QUERY-BUILDER-COVERAGE.md). Nothing here pretends otherwise.

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

## Try it

`samples/Naravel.Sample` has a runnable demo at `/database` on in-memory SQLite: conditions added per request parameter, bound values shown next to the SQL, a column name rejected at the call, `WhereYear`, `FindAsync` and the query log. See [the sample README](../../samples/Naravel.Sample/README.md).

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

Available now: `Table`, `Select`, `AddSelect`, `SelectRaw`, `Distinct`; the where family: `Where`/`OrWhere`/`WhereNot`/`OrWhereNot` (column and value, with an operator, an array of pairs, or a closure group), `WhereRaw`/`OrWhereRaw`, `WhereIn`/`WhereNotIn` (values or a subquery callback), `WhereBetween`/`WhereNotBetween`, `WhereBetweenColumns`/`WhereNotBetweenColumns`, `WhereNull`/`WhereNotNull`, `WhereColumn`, `WhereDate`/`WhereMonth`/`WhereDay`/`WhereYear`/`WhereTime`, `WhereAny`/`WhereAll`/`WhereNone`, `WhereLike`/`WhereNotLike`, `WhereFullText`, `WhereJsonContains`/`WhereJsonDoesntContain`/`WhereJsonContainsKey`/`WhereJsonDoesntContainKey`/`WhereJsonLength`, `WhereExists`/`WhereNotExists`, `WhereSubquery` (each with an `or` form; bounds are bound parameters and must not be null, because `between null` never matches); `When`/`Unless`; `Join`/`LeftJoin`/`RightJoin`/`CrossJoin` with `JoinClause`; `GroupBy`, `Having` and its forms; `OrderBy`/`OrderByDesc`/`OrderByRaw`, `Latest`/`Oldest`, `InRandomOrder`, `Reorder`/`ReorderDesc`; `Limit`/`Take`, `Offset`/`Skip`; `CountAsync`, `MinAsync`, `MaxAsync`, `SumAsync`, `AvgAsync`, `ExistsAsync`, `DoesntExistAsync`; `ChunkAsync`, `ChunkByIdAsync`, `ChunkMapAsync`, `EachAsync`, `EachByIdAsync`; `CursorAsync`, `LazyAsync`, `LazyByIdAsync`, `LazyByIdDescAsync`; `Clone`, `Compile`, `ToSql`, `ToRawSql`, `GetBindings`. Writes, raw group/having and transactions follow.

**Date parts and multi-column where.** `WhereDate`, `WhereMonth`, `WhereDay`, `WhereYear`, `WhereTime` (each with an operator form and an `or` form) compare one part of a date or time column. The four engines have no common function for this, so each grammar writes its own; the value is always a parameter:

| Method | SQLite | MySQL | PostgreSQL | SQL Server |
|---|---|---|---|---|
| `WhereDate` | `date(c)` | `date(c)` | `cast(c as date)` | `cast(c as date)` |
| `WhereMonth` / `Day` / `Year` | `cast(strftime('%m', c) as integer)` (`%d`, `%Y`) | `month(c)` / `day(c)` / `year(c)` | `extract(month from c)` | `datepart(month, c)` |
| `WhereTime` | `strftime('%H:%M:%S', c)` | `time(c)` | `cast(c as time)` | `cast(c as time)` |

Why the value is converted at the call: a `DateTime`, `DateTimeOffset` or `yyyy-MM-dd` string given to `WhereDate` becomes a `DateOnly`, and `WhereTime` takes a `TimeOnly`, `TimeSpan` (inside one day), `DateTime` or `HH:mm[:ss]` string, so every provider receives a native date or time parameter and PostgreSQL never has to compare a date with text. Month, day and year are integers and are range-checked (1-12, 1-31, 1-9999). Only `= < > <= >= <> !=` are accepted. On SQLite, dates are compared as the ISO text the provider writes, so a column that holds another text format will not match.

`WhereAny`, `WhereAll` and `WhereNone` take a list of columns, an optional operator and one value. Each column gets its own parameter (the same value repeated), so the parameter limit counts them. Any is `(a = @p0 or b = @p1)`, all joins with `and`, and none is `not (a = @p0 or b = @p1)`. An empty column list is an error at the call.

**Pattern, full text and JSON.** `WhereLike`, `WhereNotLike` (with `OrWhere...` forms) match a pattern; the pattern is always a parameter and `%` and `_` stay wildcards (they are not escaped). The default is case-insensitive; pass `caseSensitive: true` for an exact-case match. `WhereFullText` searches one or several plain columns with `FullTextMode.Natural` (plain words) or `FullTextMode.Boolean` (the engine's own boolean syntax, still bound as a value). Each grammar writes its own SQL:

| Method | SQLite | MySQL | PostgreSQL | SQL Server |
|---|---|---|---|---|
| `WhereLike` | `c like p` | `c like p` (`like binary` when case-sensitive) | `c ilike p` (`like` when case-sensitive) | `c like p` (`collate SQL_Latin1_General_CP1_CS_AS` when case-sensitive) |
| `WhereFullText` | throws `NotSupportedException` | `match(c) against (p in natural language mode)` or `in boolean mode` | `to_tsvector('english', c) @@ plainto_tsquery('english', p)` (`to_tsquery` for boolean) | `freetext(c, p)` or `contains(c, p)` |
| `WhereJsonContains` | `exists (select 1 from json_each(c, path) where value is p)` | `json_contains(c, p, path)` | `c::jsonb @> p::jsonb` | `p in (select value from openjson(c, path))` |
| `WhereJsonContainsKey` | `json_type(c, path) is not null` | `json_contains_path(c, 'one', path)` | `jsonb_path_exists(c::jsonb, path::jsonpath)` | `json_value(c, path)` or `json_query(c, path)` is not null |
| `WhereJsonLength` | `json_array_length` | `json_length` | `jsonb_array_length` | `(select count(*) from openjson(...))` |

`WhereJsonContains` and `WhereJsonDoesntContain` take any JSON-serialisable value on MySQL and PostgreSQL; SQLite and SQL Server only search for a **scalar** (string, number, boolean) inside a JSON array and throw `NotSupportedException` for an array or object value. `WhereJsonContainsKey` and `WhereJsonDoesntContainKey` need a path (`data->name`). `WhereJsonLength` counts the elements of an array (SQL Server and MySQL also count the keys of an object); a negative length is an error at the call.

**JSON path columns.** A column written `data->name`, `data->list[0]` or `data->a->b` is accepted in every where method that takes a column (`Where`, `WhereNull`, `WhereIn`, `WhereBetween`, `WhereLike`, the JSON methods). The path segments must match `[letters, digits, _]` with an optional `[n]`; the path is sent **as a bound parameter** (`$."a"."b"[0]`, or a text array for PostgreSQL), never written into the SQL. Paths are not accepted in `Select`, `OrderBy`, `Table` or date parts (`NotSupportedException`). The extracted value is a comparable scalar: SQLite `json_extract`, MySQL `json_unquote(json_extract(...))`, SQL Server `json_value`, PostgreSQL `#>>` cast to `numeric` or `boolean` when the value you compare with is a number or a boolean. Limits to know: a JSON `null` is an SQL `NULL` on SQLite, PostgreSQL and SQL Server but the text `null` on MySQL, and comparing a JSON boolean on MySQL does not work (the extracted text `true` is not the number `1`), so compare it as a string there. Full-text search needs a full-text index on MySQL and SQL Server (the SQL Server test image has no full-text component, so that path is proven by compile tests only); PostgreSQL uses the `english` text search configuration.

**Exists, subqueries and conditionals.** `WhereExists`, `WhereNotExists` (with `Or` forms) take a callback that receives a new query: call `Table(...)` on it and add columns and conditions, and it becomes `exists (select ...)`. `WhereSubquery(column, op, callback)` compares a column with a scalar subquery, and `WhereIn`, `WhereNotIn` (and their `Or` forms) accept a callback that selects one column. A subquery is compiled into the same statement with the same parameter counter, so values are numbered in text order across any depth of nesting and the dialect's parameter limit counts them all; a subquery with `Limit` becomes `top (n)` on SQL Server. The subquery must name a table (`InvalidOperationException` at the call otherwise). Correlate with `WhereColumn("posts.user_id", "users.id")`. `When(condition, callback, otherwise)` and `Unless(...)` apply a callback to the same query depending on a plain `bool`, so optional filters stay in one chain. Why `WhereSubquery` has its own name: Laravel overloads `where` with a closure, but here `Where("a", null)` means `is null`, and a second overload taking a callback would silently capture a literal `null`.

**Joins.** `Join` (inner), `LeftJoin`, `RightJoin` and `CrossJoin` add a joined table, optionally with an alias (`posts as p`). The short forms take two columns (`Join("posts", "users.id", "posts.user_id")`) or two columns and an operator; the callback form receives a `JoinClause` with `On` and `OrOn` (column against column) and `Where`, `OrWhere`, `WhereNull`, `WhereNotNull`, `WhereIn` (column against a **bound value**), so a value never becomes SQL. A join needs at least one condition (`InvalidOperationException` at the call otherwise); `CrossJoin` has none. Joins are written right after `from`, before `where`, in call order, so the values of a join condition are numbered before the values of the `where` clause whatever order you called the methods in, and every join parameter counts against the dialect limit. Conditions in one join combine like `where` (`and` by default, `or` with an `Or` form, and SQL's usual `and`-before-`or` precedence applies). Joins work inside subqueries and are copied by `Clone`. Dialect notes: `RIGHT JOIN` needs SQLite 3.39 or later (the bundled `e_sqlite3` is newer; a custom older SQLite will fail at run time); `FULL OUTER JOIN`, join subqueries and lateral joins are not part of R21.

**Grouping, having and ordering helpers.** `GroupBy` takes one or more columns (qualified names allowed) and writes `group by` after `where` and before `having`. `Having`, `OrHaving`, `HavingBetween`, `OrHavingBetween`, `HavingNull`, `HavingNotNull` and their `Or` forms work like their `Where` twins; every value is a parameter. PostgreSQL and SQL Server only allow **grouped columns** in `having`, not a select alias (`SelectRaw("count(*) as n")` then `Having("n", ...)` works on SQLite and MySQL only); aggregate conditions such as `count(*) > 1` arrive with `HavingRaw` and the aggregate methods in later stages. `Latest(column = "created_at")` and `Oldest(...)` add a descending or ascending `order by`. `InRandomOrder()` writes the engine's function (`random()`, `rand()`, `newid()`). `Reorder()` removes every ordering, `Reorder(column)` and `ReorderDesc(column)` replace it with one; a bad column leaves the existing ordering untouched.

**Aggregates and existence.** `CountAsync()`, `CountAsync(column)`, `MinAsync<T>(column)`, `MaxAsync<T>(column)`, `SumAsync(column)`, `AvgAsync(column)`, `ExistsAsync()` and `DoesntExistAsync()` run on the query as built, so every `where`, join and binding applies. `Count` returns `long`; `Count(column)` skips nulls and, after `Distinct()`, counts distinct values (also `Sum`, `Avg`, `Min`, `Max`). `Sum` of nothing is `0` and `Avg`, `Min` and `Max` of nothing are `null` (use a nullable `T` for `Min`/`Max` to tell "none" from a real zero), as in Laravel. Existing `OrderBy` is ignored, because PostgreSQL and SQL Server reject an unneeded `order by` in an aggregate. A query with `GroupBy`, `Having`, `Distinct` without a column, `Limit` or `Offset` is counted as a **derived table** (`select count(*) from (select ...) as naravel_aggregate`), so the number is the rows the query would return; for `Min`, `Max`, `Sum` and `Avg` on such a query the column is read by its last segment and must be selected. `ExistsAsync` runs `select case when exists (select 1 ...) then 1 else 0 end`, which all four engines accept (SQL Server has no `select exists(...)`); columns, ordering and `Distinct` are dropped. Two engine facts: SQL Server `avg` of an integer column is an integer (cast the column in `SelectRaw` first if you need a fraction), and `sum` of a `bigint` is `numeric` on PostgreSQL, which `SumAsync` converts to `decimal`. A column is checked by the identifier grammar, so `Sum("a as b")`, `Sum("*")` and JSON paths are rejected at the call.

**Chunking.** For tables too big to load at once, `ChunkAsync(count, callback)` reads pages with `limit`/`offset` and calls `callback` with each non-empty page (a `IReadOnlyList` of rows, or of `T` with `ChunkAsync<T>`); the callback returns `true` to continue and `false` to stop, and only the current page is in memory. `ChunkAsync` **requires an `OrderBy`** (pages of an unordered query are not stable) and replaces any `Limit`/`Offset` on the query. Because offsets shift, a row inserted or deleted during the loop can be skipped or seen twice; when the table changes while you iterate (the usual reason to chunk and update), use `ChunkByIdAsync(count, callback, column: "id")`, which orders by a unique column and asks for `where id > last`, so deep pages cost the same as the first and changes behind the cursor do not matter. The column must be in the selected columns and never null (a clear `InvalidOperationException` otherwise); `ChunkByIdAsync<T>` takes a function that reads the key from a `T`. `ChunkMapAsync(map, count = 1000)` collects what `map` returns for every row (memory grows with the result), `EachAsync(callback, count = 1000)` and `EachByIdAsync(callback, count = 1000, column: "id")` call back per row and stop on `false`. Laravel's callback also receives the page number; here the callback gets the page only, because a counter in the closure does the same.

**Streaming.** `CursorAsync()` (and `CursorAsync<T>()`) returns an `IAsyncEnumerable` over **one** command: the reader stays open and each row is handed out as it is read, so memory holds one row at a time. The connection is borrowed until the loop ends, you `break`, or the token is cancelled; each of those releases the reader and the connection. Do not run other commands on the same connection while you read (SQL Server needs MARS for that, and inside a transaction scope the commands share one connection). The observer reports one command for the whole stream when it ends, so its duration includes the time you spend between rows. `LazyAsync(count = 1000)` also hands out one row at a time but reads **pages** with `limit`/`offset`, so nothing is held between pages and you can run other commands inside the loop; it needs an `OrderBy` and has the same offset caveat as `ChunkAsync`. `LazyByIdAsync(count, column: "id")` and `LazyByIdDescAsync(...)` follow a unique column (`where id > last` / `where id < last`) and are stable when rows change behind the cursor; `LazyByIdAsync<T>` and `LazyByIdDescAsync<T>` take a function that reads the key from a `T`. Arguments are checked when you call the method, the commands run as you enumerate, and stopping early asks for no further page.

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

### Connection strings

| Driver | Example | Notes |
|---|---|---|
| `sqlite` | `Path` = `app.db` or `:memory:` | A file path, not a connection string. `:memory:` becomes a shared in-memory database that lives as long as the driver. |
| `mysql` | `Server=localhost;Port=3306;Database=app;User ID=app;Password=...` | MySqlConnector. With MySQL 8 and no TLS add `AllowPublicKeyRetrieval=true` (local and CI only). |
| `pgsql` | `Host=localhost;Port=5432;Database=app;Username=app;Password=...` | Npgsql. |
| `sqlserver` | `Server=localhost,1433;Database=app;User Id=app;Password=...;TrustServerCertificate=true` | Microsoft.Data.SqlClient. `TrustServerCertificate=true` is for a local or test server with a self-signed certificate; do not use it against production. |

### Dialect matrix

The same builder call compiles differently per database. Everything below is proven by the contract suite on all four.

| | SQLite | MySQL | PostgreSQL | SQL Server |
|---|---|---|---|---|
| Identifier quotes | `"name"` | `` `name` `` | `"name"` | `[name]` |
| Parameter marker | `@p0` | `@p0` | `@p0` | `@p0` |
| `Limit(n)` | `limit n` | `limit n` | `limit n` | `top (n)`, or `fetch next n rows only` with `Offset` |
| `Offset` without `Limit` | `limit -1 offset n` | `limit 18446744073709551615 offset n` | `offset n` | `offset n rows`, needs `OrderBy` |
| Booleans in `ToRawSql` | `1` / `0` | `1` / `0` | `true` / `false` | `1` / `0` |
| Most parameters per statement | 32766 | 65535 | 65535 | 2098 (limit 2100) |
| Extra operators | none | `<=>`, `regexp`, `rlike` and their `not` forms | `ilike`, `~`, `~*`, `!~`, `!~*`, `similar to` and `not` forms | none |

An operator that exists on one dialect only throws `NotSupportedException` on the others instead of generating SQL that fails at run time. `Offset` on SQL Server without `OrderBy` throws `InvalidOperationException`, because `OFFSET ... FETCH` requires `ORDER BY`.

### R21 dialect matrix

What the where family, joins, grouping and ordering helpers do on each database. `yes` means the same call compiles and runs everywhere; a cell that says `throws` raises `NotSupportedException` naming the dialect when the query is compiled. `tests/Naravel.Database.Tests/DialectMatrixTests.cs` asserts this table: change both together.

| Feature | SQLite | MySQL | PostgreSQL | SQL Server |
|---|---|---|---|---|
| `WhereLike` / `WhereNotLike` | yes (ASCII case-insensitive) | yes | yes (`ilike`) | yes |
| case-sensitive `WhereLike` | throws | `like binary` | `like` | `collate SQL_Latin1_General_CP1_CS_AS` |
| `WhereFullText` | throws | yes, needs a full-text index | yes, `english` configuration | yes, needs a full-text index |
| `WhereJsonContains` scalar in an array | yes | yes | yes | yes |
| `WhereJsonContains` array or object value | throws | yes | yes | throws |
| `WhereJsonContainsKey`, `WhereJsonLength` | yes | yes | yes (array length) | yes |
| JSON path column (`data->a->b[0]`) in where | yes | yes (JSON booleans do not compare) | yes (cast to numeric/boolean by the compared value) | yes |
| `WhereDate`, `WhereMonth`, `WhereDay`, `WhereYear`, `WhereTime` | yes | yes | yes | yes |
| `WhereAny`, `WhereAll`, `WhereNone`, `WhereBetween`, `WhereColumn` | yes | yes | yes | yes |
| `WhereExists`, subquery where, `WhereIn` with a callback | yes | yes | yes | yes (`top (n)` inside) |
| `Join`, `LeftJoin`, `CrossJoin` | yes | yes | yes | yes |
| `RightJoin` | yes, SQLite 3.39 or later | yes | yes | yes |
| `GroupBy`, `Having` on grouped columns | yes | yes | yes | yes |
| `Having` on a select alias | yes | yes | no (database rule) | no (database rule) |
| `Latest`, `Oldest`, `Reorder` | yes | yes | yes | yes |
| `InRandomOrder` | `random()` | `rand()` | `random()` | `newid()` |

### Types

Parameters are bound with a value and, where providers disagree, an explicit type. Single values read back (`ValueAsync<T>`, `ScalarAsync<T>`, `PluckAsync<T>`, `GetAsync<T>` for a simple `T`) are converted by Naravel, so the same call gives the same CLR value on every database:

| CLR type | Notes |
|---|---|
| `bool` | Read back from `bit`, `tinyint(1)`, `boolean` and SQLite integers. |
| `int`, `long`, `double`, `decimal`, `string` | Widths are converted, so `ScalarAsync<long>("select count(*) ...")` works on every database. |
| `Guid` | `char(36)` on MySQL, `uuid` on PostgreSQL, `uniqueidentifier` on SQL Server, text on SQLite. |
| `DateTime` | Bound as `datetime2` on SQL Server. PostgreSQL `timestamp` needs a non-UTC kind, `timestamptz` needs UTC. |
| `DateTimeOffset` | **Not available on MySQL** (no such column type; store a UTC `DateTime`). PostgreSQL accepts only offset 0. A `DateTime` read from a timezone-aware column is taken as UTC. |
| `DateOnly`, `TimeOnly` | Native on every server; text on SQLite. |
| `byte[]` | `blob`, `varbinary`, `bytea`. |
| enums | Written as the underlying integer; read back with `ValueAsync<MyEnum>` or as the integer. |
| `null` | Written as `NULL` and read back as `null` (the default for value types in `ValueAsync<T>`). |

Mapping to your own classes (`GetAsync<MyRow>()`) uses Dapper.

### Running the database tests

CI runs the contract suite in a separate `database` job: one parallel leg per server (a throwaway container with test-only tuning), each running only its own `Dialect` trait. It always runs on `main`, on version tags and nightly, and on a pull request only when database code, tests, shared test code, package versions, the compose file or the workflow changed. A leg fails if its tests were skipped, so a missing connection string cannot pass silently. The first run measured about 45 seconds per leg, far under the five-minute budget in PDR-022 A6.

Locally, `scripts/test-local.sh --db` (or `test-local.ps1 -Db`) starts the same three servers from `docker-compose.yml` (SQL Server needs about 2 GB of RAM) and exports `NARAVEL_TEST_MYSQL`, `NARAVEL_TEST_POSTGRES` and `NARAVEL_TEST_SQLSERVER`. Without those variables the server tests are skipped, which means **not run**, and only SQLite is checked. To run one dialect: `scripts/test-local.sh --db -- --filter "Dialect=Postgres"`.

## Not ported on purpose

- `DB::__call` forwarding to the connection: inject `IDatabase` (or the scope) instead.
- `DB::select` returning `stdClass` objects: rows are dictionaries or Dapper-mapped types. `pretend()` is replaced by `DatabaseFake`, and `listen()` by `IQueryListener`.
- Reading and writing split connections, `purge()` and `reconnect()`: connections are pooled by the provider and rebuilt on configuration change.
