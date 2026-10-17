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

> Refined by amendment A6 below: the server containers run in a separate parallel `database` job, not inside `services`.

The `services` job gains MySQL, PostgreSQL and SQL Server service containers (OD-01: no Testcontainers; Linux job only). SQLite and all compile tests run in the three-OS `fast` job. SQL Server needs `ACCEPT_EULA` and a strong password and a health check; versions are chosen at R20.T02 from currently supported images.

### 6. EF Core relationship (R25)

`Naravel.Database.EntityFrameworkCore` lets a `DbContext` and the query builder share **one connection and one transaction**: `db.Using(dbContext)` or `dbContext.Query("table")` borrows `Database.GetDbConnection()` and the current `IDbContextTransaction`. Neither side commits the other's transaction. Entities, migrations and change tracking stay in EF Core.

### 7. Observability and fake (S3)

`Meter` and `ActivitySource` named `Naravel.Database` with counters for queries, failures and a duration histogram; tags are the connection name and operation kind only, never SQL text or values. `DatabaseFake` (namespace `Naravel.Database.Testing`) records compiled queries and returns canned results for tests that do not need a database. `DB::listen` becomes `IQueryListener` plus an opt-in in-memory query log.

## Amendment 2026-10-17 (owner request, architecture review)

An architecture review of this PDR found six risks. The owner asked for them to be written into the design before R19 starts. They change **how** stages are built, not the approved scope. Each item names the stage that must prove it.

### A1. Connection and transaction seam in R19 (risk: late breaking change)

R25 adds transactions and EF Core interop, but if R19 builds the executor around "open a connection, run, close", R25 would have to change public signatures. R19 therefore defines the seam now and implements only the simple half:

- The executor never opens a connection itself. It asks an `IConnectionScope` for a **lease**: `IConnectionLease { DbConnection Connection; DbTransaction? Transaction; bool OwnsConnection }`.
- R19 supplies one scope: it leases a fresh pooled connection, `Transaction` is `null`, `OwnsConnection` is true, and disposing the lease closes the connection.
- R25 supplies two more scopes without touching `Query` or any execution signature: the ambient-transaction scope (all queries in a `TransactionAsync` callback share one lease) and the EF Core scope (borrowed connection and transaction, `OwnsConnection` false, never closed or committed by us).
- Every command the executor creates sets `command.Transaction` from the lease. A borrowed lease is never disposed.
- **Proof in R19.T05:** a test double scope shows the executor sets `command.Transaction` and does not close a connection it does not own. `IConnectionScope` and `IConnectionLease` are in PublicAPI from R19.

### A2. Dapper facts and limits (risk: global state, AOT, provider types)

Dapper stays in `Naravel.Database` (owner decision, unchanged). The design contains its known limits instead of hiding them:

- **Global state.** Dapper's `SqlMapper` type handlers and default type map are process-wide. Naravel never registers a global handler on its own. If a type needs one (checked against the pinned version at R19.T02, for example `DateOnly` and `TimeOnly`), it is offered as an opt-in `DatabaseOptions.RegisterDapperTypeHandlers`, documented as global.
- **Trim and AOT.** Dapper emits IL at run time, so `GetAsync<T>()` is **not** AOT or trim safe, and the package is not marked `IsAotCompatible`. The dictionary path `GetAsync()` does not use Dapper: it reads the `DbDataReader` directly, which is faster and trim safe. This keeps the "no reflection on hot paths" rule (S2) honest.
- **Parameter types.** Values are bound as `DbParameter` with an explicit `DbType` for the types that differ between providers (`bool`, `decimal`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `byte[]`, enums). Dapper is used for materializing rows, not for guessing parameter types.
- **Proof in R20.T04:** a type round-trip test (write then read: null, bool, long, decimal, Guid, `DateTime` kinds, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `byte[]`) on SQLite and on each server, asserting identical values.

### A3. Identifier and operator grammar, frozen in R19 (risk: security rule relaxed later)

The "conservative pattern" is now exact, so R21 cannot loosen it to fit joins or JSON:

- **Segment:** `^[\p{L}_][\p{L}\p{N}_$]*$`, at most 128 characters. Unicode letters are allowed, so Persian column names work.
- **Accepted forms:** `col`, `table.col`, `schema.table.col` (at most three segments); `*` and `table.*` only in a select list; `source as alias` or `source AS alias` where the alias is one segment; a JSON column path `col->key->sub` or `col->list[0]`, whose path segments match `^[\p{L}\p{N}_]+(\[\d+\])?$`.
- **JSON paths are bound as parameters** where the dialect allows it (`JSON_EXTRACT(col, ?)`, `JSON_VALUE(col, @p)`, `json_extract(col, ?)`, an array parameter for PostgreSQL `#>`), so a path never becomes an injection point.
- **Operators** come from a per-method allow-list (`=`, `<`, `>`, `<=`, `>=`, `<>`, `!=`, `like`, `not like` and the dialect-specific ones) and are never concatenated from caller text.
- **Quoting** per dialect doubles the closing quote character (`"`, backtick, `]`).
- Anything else throws `ArgumentException` **at the call**, not when the query runs. Hostile-input tables are written in R19.T04 for every dialect. A later stage may add new callers of the grammar, but **relaxing it needs a PDR amendment**.

### A4. Parameter limits (risk: failure only in production)

Each grammar exposes `MaxParameters`: SQL Server 2100 (use 2098), SQLite 32766, MySQL 65535, PostgreSQL 65535 (values are confirmed against the pinned drivers at R20.T01).

- `WhereIn` and `WhereNotIn` above the limit are **split** into `(col IN (...) OR col IN (...))` and `(col NOT IN (...) AND col NOT IN (...))`. This keeps the meaning; the split is shown in `ToSql()`.
- Any other statement above the limit throws `InvalidOperationException` naming the dialect, the limit and the count. R23 batch inserts split into several statements inside one transaction (own or ambient), so a batch stays atomic.
- **Proof in R19.T04:** grammar tests with `limit - 1`, `limit` and `limit + 1` values per dialect (pure strings, no database), plus a SQLite execution test with 40,000 values.

### A5. R16 before R26 (risk: ordering)

R26 needs the R16 pagination types. The **Default order** in `ROADMAP.md` already places R16 before R26 and R26 lists R16 as a dependency. R26 must not be started early even if its other dependencies are finished.

### A6. CI that is both tested and fast (risk: job length)

The goal is to cover all four dialects without making every push slow. Four measures:

1. **Tier 1, every push and PR, three operating systems (`fast` job):** all grammar compile tests, injection tests, `MaxParameters` tests, SQLite execution, Fake and telemetry tests. Compile tests are the bulk and they are plain string comparisons from `[Theory]` data, so they cost milliseconds and need no I/O.
2. **Tier 2, a new `database` job, Linux only, one matrix leg per server (`mysql`, `postgres`, `sqlserver`):** each leg has its own service container and runs only that dialect's contract suite (xUnit trait `Dialect`). The legs run in parallel, so the wall time is the slowest leg, not the sum, and the existing `services` job does not get longer. This refines section 5, which started the three containers inside `services`.
3. **Fast containers and fast tests:**
   - PostgreSQL uses an alpine image with `fsync=off`, `synchronous_commit=off` and `full_page_writes=off`; MySQL runs without the binary log and with relaxed flush settings; both keep their data on `tmpfs` (`options: --tmpfs ...`). SQL Server cannot be tuned much, so its leg is the one that sets the wall time.
   - Each dialect has **one shared xUnit collection fixture**: schema created once under a run-unique prefix and a small fixed data set seeded once. Read tests (select, where, joins, aggregates) share it with no per-test DDL. Write tests (R23) run **inside a transaction that is rolled back**, so they are isolated without re-creating anything. Tests that need DDL use uniquely named temporary tables. Test classes use separate tables so xUnit can run them in parallel.
   - The NuGet package cache is cached by `actions/cache`, keyed on `Directory.Packages.props`.
4. **Path filter plus a safety net:** on a PR, Tier 2 runs only when the PR touches `src/Naravel.Database*`, `tests/Naravel.Database*`, `Directory.Packages.props` or the workflow; on every push to `main` and on a nightly schedule it always runs. A final `ci-ok` job (`if: always()`) treats a skipped Tier 2 as success and any failure as failure, so branch protection requires only `ci-ok` and a path-filtered job can never block a merge by being absent. **Owner action when R20.T02 merges:** until then branch protection requires the existing checks (`Build and test` for the three operating systems and `Provider integration tests`); after the new job exists, replace those required checks with `ci-ok` alone, otherwise a skipped or renamed check can block every merge.

**Budget and escalation:** R20.T02 measures the real duration. If a leg takes more than five minutes, the contract suite is sharded by trait inside that leg. A dialect is **never dropped** to save time (owner decision). `scripts/test-local.sh` gains a `--db` option that starts the three servers from the root `docker-compose.yml` so Tier 2 can be run locally.

### A7. Correction to A4: splitting a large `IN` list does not help (found while implementing R19.T04)

A4 said an `IN` list above the limit is split into `(col IN (...) OR col IN (...))`. Implementing it showed this is wrong: the limit is on **parameters per statement**, and the split keeps every value as a parameter, so the statement is exactly as large and still fails. The split only changes the SQL text, not the count. The implemented behaviour is therefore:

- **R19.T04 (done):** any statement above `MaxParameters` throws `InvalidOperationException` that names the dialect, the limit and the count. `WhereIn`/`WhereNotIn` do **not** split. The boundary tests (limit - 1, limit, limit + 1) assert this.
- **Proposed for R20/R21, needs owner approval:** above a threshold, bind the whole list as **one** parameter instead of one per value: PostgreSQL `col = ANY(@p)` with an array parameter, SQL Server `col IN (SELECT value FROM OPENJSON(@p))`, SQLite `col IN (SELECT value FROM json_each(@p))`, MySQL 8 `col IN (SELECT v FROM JSON_TABLE(@p, '$[*]' COLUMNS (v ...)))`. The values stay bound (no injection risk) and the 40,000-value SQLite test from A4 becomes meaningful. Cost: JSON value typing per dialect and a minimum server version for MySQL; each needs its own round-trip test.
- A4's other rule is unchanged and correct: R23 batch inserts are split into several statements inside one transaction.

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

Amendment of 2026-10-17 (A1 to A6) was written at the owner's request after an architecture review; it keeps every approved decision and adds the constraints above. Its open items are decided by the owner at the stage that implements them.

The owner approved on 2026-10-16: (a) Dapper stays in `Naravel.Database` core (the proposed option); (b) the stage boundaries R19 to R26 are accepted; (c) the owner asked for the best option, and the decision is **all four databases from the first release**. The three server containers start in parallel in the existing `services` job; if that job's duration becomes a problem, the database contract tests move to their own CI job (a separate job started from the same workflow) instead of dropping a dialect. Packages (Dapper, MySqlConnector, Microsoft.Data.SqlClient, Npgsql, Microsoft.Data.Sqlite) are approved; exact versions are verified from NuGet metadata at R19.T02 and R20.T01.
