# Database dialect differences (the contract)

One query-builder call is **not** guaranteed to mean the same thing on every engine. This page is the public contract for where it differs. If behaviour is not listed here or not covered by a test, do not assume it is portable.

Persian version: [docs/fa/database-dialects.md](../fa/database-dialects.md). Method ownership: [roadmap/QUERY-BUILDER-COVERAGE.md](../../roadmap/QUERY-BUILDER-COVERAGE.md).

## How to read the evidence

| Level | Meaning |
|---|---|
| **Executed** | A scenario in `tests/Naravel.Database.Tests/ContractScenarios.cs` runs against a real server (SQLite, MySQL, PostgreSQL, SQL Server) in CI. |
| **Compile-only** | The SQL is generated and asserted, never run on that engine. |
| **Refused** | The call throws a documented exception on that engine (executed as a test of the refusal). |

## Same call, different meaning

| Call | SQLite | MySQL | PostgreSQL | SQL Server |
|---|---|---|---|---|
| `UpsertAsync(rows, uniqueBy, update)` | `on conflict (uniqueBy)`; needs a unique index on those columns | `on duplicate key update` (8.0.19+ row alias); **`uniqueBy` is ignored: any unique key of the table triggers the update**; an updated row counts twice in the affected count | `on conflict (uniqueBy)`; needs a unique index | `merge ... with (holdlock)` on `uniqueBy`; a collision on another unique key throws instead of updating |
| `UpsertAsync` with an empty update list | plain insert | plain insert | plain insert | plain insert |
| `InsertOrIgnoreAsync` | supported | supported | supported | **`NotSupportedException`** |
| `*ReturningAsync` | `returning` | **`NotSupportedException`** | `returning` | `output inserted` |
| Returning across a split batch | `InvalidOperationException` | n/a | `InvalidOperationException` | `InvalidOperationException` |
| Update/delete with joins | inner joins only | supported | inner joins only; delete refuses left/right/cross | supported |
| `UpdateOrInsertAsync` | two statements, **not atomic** | same | same | same |
| Row locks (`LockForUpdate`, `SharedLock`) | **refused** | supported | supported | supported |
| `WhereFullText` | **refused** | executed | executed | **compile-only**, never run |
| JSON contains a whole array, object, `Guid`, `DateTime`, `DateTimeOffset`, `char` or enum value | **`NotSupportedException`** | supported (serialised to JSON) | supported (serialised to JSON) | **`NotSupportedException`** |
| JSON contains a scalar (string, number, bool) | element of the array (or an object's value) at the path, compared **typed** (`1` does not match `"1"`) | `json_contains`: element of the array at the path | `@>`: element of the array at the path | element of the array (or an object's value) at the path, compared **as text** (`"1"` can match `1`) |
| JSON contains with a `null` value | `ArgumentNullException` | same | same | same |
| JSON contains with no path (`WhereJsonContains("col", v)`) | searches the whole document | same | same | same |
| `WhereLike` case sensitivity | differs from the other engines (the case-sensitive scenario is not run on SQLite) | executed | executed | executed |
| `Having` on a select alias | supported | supported | **refused by the engine** | **refused by the engine** |
| `DateTimeOffset` value | stored | **not round-tripped** | stored | stored |
| JSON boolean value comparison | supported | **not supported** | supported | supported |

"Executed" status of each cell is the ledger below: a cell in the "NOT run on" list is one the suite skips on that engine.

## Error contract (portable, offline, every dialect)

| Input | Result |
|---|---|
| Unknown operator, or one that looks like SQL | `ArgumentException` at the call |
| Operator that exists only on another dialect | `NotSupportedException` |
| `OrderBy` direction other than asc/desc | `ArgumentException` |
| Hostile column or table name | `ArgumentException` before any statement is sent |
| Update or delete without `where` | `InvalidOperationException` |
| `WhereIn` given a string | `ArgumentException` |
| `WhereBetween` with a null bound | `ArgumentNullException` |
| Empty `WhereIn` / `WhereNotIn` | valid SQL (never `in ()`) |
| Any value | always a binding, never part of the SQL text |

These are asserted by `QueryHostileInputTests`, `IdentifierSafetyTests` and `DialectMatrixTests`.

## "NOT run on" ledger

Scenarios the shared suite skips on an engine (they are refused or unsupported there, or differ by design):

| Scenario | Not run on |
|---|---|
| `where_like_case_sensitive` | SQLite |
| `where_json_boolean_value` | MySQL |
| `where_json_contains_a_whole_array` | SQLite, SQL Server |
| `where_full_text_natural` | SQLite, SQL Server |
| `where_full_text_is_unsupported_on_sqlite` | MySQL, PostgreSQL, SQL Server |
| `group_by_with_having_on_a_select_alias` | PostgreSQL, SQL Server |
| `type_datetimeoffset` | MySQL |
| `write_insert_or_ignore` | SQL Server |
| `write_returning_insert_update_delete`, `write_returning_batch_insert` | MySQL |
| `write_returning_is_refused_on_mysql` | SQLite, PostgreSQL, SQL Server |
| `subquery_join_lateral_and_left_join_lateral` | SQLite |
| `subquery_lateral_is_refused_on_sqlite` | MySQL, PostgreSQL, SQL Server |
| `locks_read_the_same_rows` | SQLite |
| `locks_are_refused_on_sqlite` | MySQL, PostgreSQL, SQL Server |

Every other scenario runs on all four engines. `write_upsert_key_semantics_differ_by_dialect` runs everywhere and asserts the upsert row above.

## What "Laravel-compatible" means here

The claim is limited to the **tested subset**: the methods in the coverage ledger, with the behaviour the contract suite executes on the engines above, plus the deliberate deviations (rows are dictionaries or Dapper types, no static facade, `Async` suffix, explicit `where` for update/delete, hostile identifiers rejected). It is not a promise that SQL text or edge behaviour is identical to Laravel or identical across engines.
