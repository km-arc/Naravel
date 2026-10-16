# PDR-022 — Database query builder (`Naravel.Database`)

**Native-first verdict:** .NET already has typed queries (LINQ with EF Core), a universal connection API (ADO.NET `DbConnection`) and a fast mapper (Dapper). What it lacks is a **dynamic, string-composed query builder** with Laravel's surface (`Table("users").Where("age", ">", 18).OrderBy("name")`) that compiles the same call chain to MySQL, PostgreSQL, SQL Server and SQLite. LINQ cannot compose column names at run time without expression trees, and raw SQL strings are error-prone and unsafe. Naravel therefore builds **only the SQL compiler and a thin executor**, and keeps EF Core for entities, change tracking and migrations.

Status: **Approved 2026-10-16.** Owner pre-decisions given in conversation on 2026-10-16: own compiler (not SqlKata), the packages listed below approved in principle, shared connection and transaction with EF Core, delivery after R09 in separate testable stages.

## Context

The owner asked for a query builder that supports **every method of the Laravel query builder page** on MySQL, SQL Server, PostgreSQL and SQLite, with EF Core as the foundation for entities and Dapper for fast queries. Repository rules that shape the design: monorepo (`Naravel.slnx`), Manager to Driver on `Naravel.Foundation`, central package versions, a PDR before any Laravel-equivalent feature, async plus `CancellationToken` on all I/O, no reflection on hot paths (S2), Meter and ActivitySource plus a Fake (S3), env-gated provider tests in CI service containers (OD-01), and honest verification: there is no .NET SDK in the agent sandbox, so CI is the only verification.

Laravel surface: the `Illuminate\Database\Query\Builder`, `Grammar` classes per dialect, `DB::` facade statements, `DB::transaction`, `DB::listen`. Native baseline: ADO.NET, EF Core `FromSql` and LINQ, Dapper.

## Decision

### 1. Quickstart (S1: at most 3 lines of user code)

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);      // Database:Default, Database:Connections:<name>
builder.Services.AddNaravelSqliteDatabase(builder.Configuration); // or MySql / SqlServer / Postgres package

var adults = await db.Table("users").Where("age", ">=", 18).OrderBy("name").Limit(10).GetAsync<UserRow>(ct);
```

`db` is an injected `IDatabase`. `GetAsync<T>()` maps with Dapper; `GetAsync()` returns rows as `IReadOnlyList<IDictionary<string, object?>>`.

### 2. Packages and boundaries

| Package | Contents | Dependencies |
|---|---|---|
| `Naravel.Database` | `IDatabase`, `DatabaseManager`, `Query` (builder), `Grammar` base plus the **four dialect grammars** (pure string generation, no driver), `Raw`, query log and listeners, `DatabaseFake`, telemetry | Foundation, Dapper, `Microsoft.Extensions.*` abstractions |
| `Naravel.Database.Sqlite` | connection factory, SQLite driver registration | Microsoft.Data.Sqlite |
| `Naravel.Database.MySql` | same for MySQL | MySqlConnector |
| `Naravel.Database.SqlServer` | same for SQL Server | Microsoft.Data.SqlClient |
| `Naravel.Database.Postgres` | same for PostgreSQL | Npgsql |
| `Naravel.Database.EntityFrameworkCore` | borrow the `DbContext` connection and transaction (R25) | Microsoft.EntityFrameworkCore.Relational |

Putting the grammars in the core means every dialect's SQL is unit-tested **without a database** on every OS; the driver packages only add `DbConnection` creation. Dapper sits in core because executing a built query and mapping rows is the builder's job; it is the only third-party runtime dependency of core.

New dependencies (owner approved in principle on 2026-10-16): **Dapper** (Apache-2.0), **MySqlConnector** (MIT), **Microsoft.Data.SqlClient** (MIT), **Npgsql** (PostgreSQL licence), **Microsoft.Data.Sqlite** (MIT). Exact current stable versions that support `net10.0` are verified from NuGet metadata at R19.T02 and R20.T01 before `Directory.Packages.props` is edited. `FluentAssertions` stays on 7.x.

### 3. Architecture

- `DatabaseManager : Manager<IDatabaseConnection, DatabaseOptions>` (rule 2: never re-implement driver resolution). `Database:Connections:<name>:Driver` is `sqlite`, `mysql`, `sqlserver` or `pgsql`; runtime `Extend` and configuration reload come from the base class.
- `IDatabaseConnection` is the driver: it knows its `Grammar` and opens a `DbConnection`. It never builds SQL itself.
- `Query` is a mutable fluent builder (as in Laravel) with `Clone()`. `ToSql()` and `ToRawSql()` call the grammar; execution methods (`GetAsync`, `FirstAsync`, `InsertAsync`, ...) compile once, open the connection (or reuse the ambient transaction), run through Dapper, and record telemetry. Compiled SQL is not cached by reflection; dynamic builders produce a string per call, so hot paths should reuse a built `Query` through `Clone()` or call `Compile()` once.
- **Safety is a hard rule.** Values are **always** bound as parameters, never concatenated. Identifiers (tables, columns, aliases) are validated against a conservative pattern and quoted per dialect. Anything that is SQL text must be wrapped in `Raw` (or use a `*Raw` method) so the unsafe path is explicit and greppable. Raw SQL accepts bindings.
- **Dialect differences are explicit.** A call a dialect cannot express correctly (for example `WhereFullText` on SQLite, lateral joins on SQLite) throws `NotSupportedException` at compile time with the dialect and method named. Nothing is silently emulated wrongly. The supported matrix is part of the docs and is asserted by tests.
- Results: `GetAsync<T>()` maps through Dapper (`DefaultTypeMap.MatchNamesWithUnderscores` is opt-in via `DatabaseOptions`); `GetAsync()` returns dictionaries. No reflection of our own on the hot path.
- Everything that touches I/O is `async` with `CancellationToken`; streaming (`Lazy`, `Cursor`, `Chunk`) uses `IAsyncEnumerable`.

### 4. Stages (each independently buildable, testable and releasable)

| Stage | Scope | Always-on proof | Env-gated proof |
|---|---|---|---|
| R19 | Core, manager, SQLite, `Table`, select, basic where, order, limit/offset, raw statements, query log, Fake, telemetry | compile tests (4 grammars for the covered methods), SQLite execution | - |
| R20 | MySQL, PostgreSQL, SQL Server grammars verified against real servers, driver packages, CI service containers | grammar compile tests | `NARAVEL_TEST_MYSQL`, `NARAVEL_TEST_POSTGRES`, `NARAVEL_TEST_SQLSERVER` contract suite |
| R21 | Full where family, joins, grouping, having, ordering helpers, `When`/`Unless` | compile tests per dialect, SQLite execution | same contract suite extended |
| R22 | Aggregates, `Exists`, chunking and streaming | SQLite execution | same |
| R23 | Insert, update, upsert, delete, truncate, increment, JSON update, returning | compile tests, SQLite execution | same |
| R24 | Union, subqueries, CTE, locks, raw expressions, debugging helpers | compile tests, SQLite execution | same |
| R25 | Transactions (with deadlock retry and savepoints) and EF Core interop | SQLite execution | same |
| R26 | Pagination integration with the R16 types | SQLite execution | same |

Every stage must add its methods to **all four** grammars (or document the `NotSupportedException`) and extend the shared contract suite, so no dialect lags behind. The method ownership ledger is [roadmap/QUERY-BUILDER-COVERAGE.md](../../../roadmap/QUERY-BUILDER-COVERAGE.md).

### 5. CI

The `services` job gains MySQL, PostgreSQL and SQL Server service containers (OD-01: no Testcontainers; Linux job only). SQLite and all compile tests run in the three-OS `fast` job. SQL Server needs `ACCEPT_EULA` and a strong password and a health check; versions are chosen at R20.T02 from currently supported images.

### 6. EF Core relationship (R25)

`Naravel.Database.EntityFrameworkCore` lets a `DbContext` and the query builder share **one connection and one transaction**: `db.Using(dbContext)` or `dbContext.Query("table")` borrows `Database.GetDbConnection()` and the current `IDbContextTransaction`. Neither side commits the other's transaction. Entities, migrations and change tracking stay in EF Core.

### 7. Observability and fake (S3)

`Meter` and `ActivitySource` named `Naravel.Database` with counters for queries, failures and a duration histogram; tags are the connection name and operation kind only, never SQL text or values. `DatabaseFake` (namespace `Naravel.Database.Testing`) records compiled queries and returns canned results for tests that do not need a database. `DB::listen` becomes `IQueryListener` plus an opt-in in-memory query log.

## Out of scope

Eloquent models and relationships, the schema builder, migrations (use EF Core), seeders, a connection pool of our own, query result caching, and the PHP debug helpers `dd`/`dump` (use `ToSql`, `ToRawSql` and the query log).

## Alternatives considered

1. **Wrap SqlKata.** Faster start, but it is an external dependency that does not cover the whole Laravel surface (locks, JSON where, lateral joins, upsert variants) and would put our SQL-safety guarantees in someone else's hands. Rejected by the owner.
2. **Build the builder on EF Core `IQueryable`.** LINQ already does typed queries; dynamic column composition needs expression-tree work and ties the builder to entity models. Rejected.
3. **Dapper only, no builder.** Leaves SQL composition to string concatenation, the exact problem the module solves.
4. **Single package with every driver.** Forces SQL Server, Postgres and MySQL clients on every user. Rejected; drivers are separate packages.
5. **Emulate unsupported dialect features.** Silent emulation risks wrong results (for example fake full-text search). Rejected; throw `NotSupportedException`.

## Verification required after approval

Per stage: regression tests first; compile tests for every new method on all four grammars (expected SQL and bindings as strings); SQLite execution tests; env-gated contract tests per server; injection tests (hostile identifiers rejected, hostile values only ever parameters); cancellation tests; Fake and telemetry tests; EN/FA docs; PublicAPI entries; benchmark of compile-and-execute against hand-written Dapper in R19 and R22; `roadmap/check.py`. Anything not run is reported as NOT RUN.

## Approval

The owner approved on 2026-10-16: (a) Dapper stays in `Naravel.Database` core (the proposed option); (b) the stage boundaries R19 to R26 are accepted; (c) the owner asked for the best option, and the decision is **all four databases from the first release**. The three server containers start in parallel in the existing `services` job; if that job's duration becomes a problem, the database contract tests move to their own CI job (a separate job started from the same workflow) instead of dropping a dialect. Packages (Dapper, MySqlConnector, Microsoft.Data.SqlClient, Npgsql, Microsoft.Data.Sqlite) are approved; exact versions are verified from NuGet metadata at R19.T02 and R20.T01.
