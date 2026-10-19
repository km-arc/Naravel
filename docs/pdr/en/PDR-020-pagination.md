# PDR-020 — Pagination (`Naravel.Pagination`)

**Native-first verdict:** partly native. LINQ and EF Core already do the work (`Skip`/`Take` for offset pages, `Where` on the last seen key for keyset pages), and EF Core ships **no** pagination type. What .NET lacks is the part Laravel is loved for: one typed result that carries the page numbers and links, serializes to a stable JSON shape that front ends can rely on, and a cursor that is safe to hand to a client. Proposed outcome: **keep, small**: a dependency-free core with three result types, plus an EF Core adapter. A hand-rolled `Skip`/`Take` is three lines; the metadata, the cursor and the safe defaults are what the package adds.

Status: **Approved 2026-10-19** with the recommended answer to each of the five questions (see *Approval*).

## Context

Laravel has three paginators (source: [Laravel 12 pagination](https://laravel.com/docs/12.x/pagination)):

| Laravel | Queries it runs | Gives you |
|---|---|---|
| `paginate` → `LengthAwarePaginator` | a `count`, then `limit`/`offset` | page numbers, total, last page |
| `simplePaginate` → `Paginator` | one `limit`/`offset` query, no count | next and previous only |
| `cursorPaginate` → `CursorPaginator` | `where` on the ordered column values, no offset | next and previous cursors; no page numbers |

Cursor pagination in Laravel needs an `orderBy`, at least one unique ordered column, and no `null` values in the ordered columns. Returned from a route, a paginator becomes JSON; an API resource collection wraps it as `data`, `links` (`first`, `last`, `prev`, `next`) and `meta` (`current_page`, `from`, `last_page`, `path`, `per_page`, `to`, `total`) ([Eloquent resources](https://laravel.com/docs/12.x/eloquent-resources)). That page shows no JSON for the simple or the cursor paginator, so their shape below is a Naravel decision.

What EF Core documents ([EF Core pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)): offset pagination has two weaknesses, the database still walks the skipped rows and a concurrent insert or delete shifts the pages (a row is skipped or shown twice); keyset ("seek") pagination filters on the last seen key, is fast with a matching index and is not disturbed by changes behind the cursor, but cannot jump to page 17. Ordering must be fully unique. EF Core cannot express the row-value form `(a, b) > (x, y)` in LINQ, so a multi-column keyset is written as `a > x or (a = x and b > y)`. EF Core has no built-in helper type.

## Proposed decision

Two packages, no others:

- **`Naravel.Pagination`**: no EF Core, no ASP.NET Core, no database dependency. The result types, the cursor codec, `PaginationOptions` and the JSON contract. Zero I/O, so it can also serve the query builder.
- **`Naravel.Pagination.EntityFrameworkCore`**: `PaginateAsync`, `SimplePaginateAsync` and `CursorPaginateAsync` on `IQueryable<T>`. This is the only project that references EF Core.

The query-builder methods (`Query.PaginateAsync` and friends, R26) will reuse the core types; they are **not** part of this PDR.

### Quickstart (the PDR states it before any code, as the roadmap requires)

```csharp
var page = await db.Posts.OrderBy(p => p.Id).PaginateAsync(pageNumber, perPage: 15, ct);   // two queries: count + page
return page.WithPath("/posts");                                                           // serializes to data / links / meta
```

### Types

- `Paginated<T>` (length-aware): `Items`, `CurrentPage`, `PerPage`, `Total`, `LastPage`, `From`, `To`, plus `HasMorePages`, `OnFirstPage`, `OnLastPage`, `Map<TResult>(...)` (Laravel's `through`) and `WithPath`/`WithQuery` for link building.
- `SimplePaginated<T>`: the same without `Total`/`LastPage`; it fetches `perPage + 1` rows to know whether a next page exists.
- `CursorPaginated<T>`: `Items`, `PerPage`, `NextCursor`, `PreviousCursor` (each `null` when there is none).

All three are immutable. `Items` is an `IReadOnlyList<T>`. There is no `ArrayAccess`, `Collection` or `Arrayable` equivalent (Laravel-isms with no .NET meaning).

### JSON contract (names are fixed with `[JsonPropertyName]`, so ASP.NET's camel-case policy cannot change them)

Length-aware, identical to Laravel's resource collection so an existing Laravel-targeted front end works unchanged:

```json
{ "data": [ ... ],
  "links": { "first": "/posts?page=1", "last": "/posts?page=7", "prev": "/posts?page=1", "next": "/posts?page=3" },
  "meta":  { "current_page": 2, "from": 16, "last_page": 7, "path": "/posts", "per_page": 15, "to": 30, "total": 100 } }
```

Simple: the same, with `links.first`, `links.prev`, `links.next` and `meta` holding `current_page`, `from`, `to`, `per_page`, `path` (no `last`, no `total`, no `last_page`).
Cursor (Naravel's own, since Laravel documents none): `{ "data": [...], "links": { "prev": ..., "next": ... }, "meta": { "path": "/posts", "per_page": 15, "next_cursor": "...", "prev_cursor": null } }`.

Links are `null` until a path is supplied with `WithPath`; the package never reads `HttpContext`. `WithQuery(IEnumerable<KeyValuePair<string, string?>>)` carries other query-string values into every link (Laravel's `withQueryString`); one line converts `Request.Query`.

### Behaviour that is decided up front

- **Page edges.** `page < 1` is treated as 1. A page past the end returns an empty `Items` with correct `Total`/`LastPage` (as Laravel does), not an error. An empty set has `LastPage = 1`, `From`/`To = null`.
- **Per-page limit.** `perPage` is clamped to `PaginationOptions.MaxPerPage` (default 100) so a client cannot ask for a million rows. `perPage < 1` throws `ArgumentOutOfRangeException` at the call.
- **Counting.** `PaginateAsync` runs the count first and the page second; the two are separate statements, so a concurrent write can make `Total` disagree with the page by a row. This is documented, not hidden. For large tables the docs steer users to `SimplePaginateAsync` or the cursor.
- **Ordering is required.** `PaginateAsync`/`SimplePaginateAsync` throw `InvalidOperationException` if the query has no `OrderBy`, because an unordered page is not stable (EF Core's own warning). No default ordering is invented.

### Cursor pagination: the design that makes it safe

The caller declares the keys once, and the helper applies **both** the `OrderBy` and the `Where`, so the ordering and the filter cannot disagree:

```csharp
var page = await db.Posts.CursorPaginateAsync(k => k.By(p => p.CreatedAt).Then(p => p.Id), perPage: 15, cursor, ct);
```

- The keys must together be unique (the last one should be the primary key). The docs say so; the helper cannot verify it.
- A multi-column keyset is generated as `a > x or (a = x and b > y)` (and `<` for descending keys), the portable form EF Core can translate.
- A `null` key value is not supported, as in Laravel. It fails with an `InvalidOperationException` that names the key, instead of silently skipping rows.
- The cursor is an opaque base64url string holding the key values as JSON and a direction flag. **It is a position, not a secret and not a permission:** it is unsigned by default. Its values reach the database only as **bound parameters**, never as SQL text, and the column names come from the caller's key declaration, never from the cursor, so a forged cursor can only move the position. Each value is converted to the declared CLR type of its key; a wrong shape, a wrong count, an unknown direction or a cursor longer than 1 KiB throws a typed `InvalidCursorException`, which an app maps to `400`.
- Stable under inserts: new rows behind or ahead of the cursor do not shift the pages (the contract the tests must prove on SQLite).

## Contracts and quality

- **S2 (speed).** No reflection on the paging path beyond the expression trees the caller passes to EF Core; the cursor codec uses `System.Text.Json` source generation for its fixed envelope. T04 adds a benchmark that compares offset and keyset at depth and records the numbers; until it runs, no speed claim is made.
- **S3 deviation, stated openly.** The roadmap asks I/O modules for a `Meter` and a Fake. These types do no I/O and the queries they trigger are already timed by the database and EF Core telemetry, so a second meter would only duplicate it, and a "fake paginator" would be a plain `new Paginated<T>(...)`, which the constructor allows. The PDR asks the owner to accept this deviation (question 4).
- No logging of items, cursors or query strings.
- Tests (after approval): serialization snapshots for all three shapes; page edges (first, last, past the end, empty, `perPage` above the cap); the missing-`OrderBy` error; cursor round trip, tampered and truncated cursors, wrong key types, descending and multi-column order, stability under inserts, and a `null` key; all on SQLite (OD-01), and a core-only test run with no EF reference to prove the dependency boundary.

## Alternatives considered

1. **Document `Skip`/`Take` and add nothing.** Honest and tiny, but every team then reinvents the JSON shape, the link maths and, worst, an unsafe home-made cursor. Rejected as the main path; the docs still show the three-line version for readers who do not need more.
2. **Reuse a third-party keyset library** (EF Core's page names `MR.EntityFrameworkCore.KeysetPagination`). Rejected: a new dependency needs its own PDR (OD-01/OD-02), it does not give the Laravel JSON shape, and it does not serve the query builder.
3. **Camel-case JSON, the ASP.NET default.** Rejected as the default because it breaks Laravel-targeted front ends and the stage's goal of Laravel-like `data`/`links`/`meta`. Question 1 lets the owner choose.
4. **Infer the cursor keys from the query's `OrderBy`.** Rejected: it needs expression-tree inspection of arbitrary queries and lets the filter and the ordering drift apart. Declaring the keys once is smaller and safer.
5. **Sign every cursor.** Deferred: it needs a key ring (Data Protection) and gains little, since a cursor is a position and its values are bound parameters. It can be added as an option later without changing the format.

## Questions for the owner

1. **JSON names:** Laravel's snake_case `data`/`links`/`meta` (recommended) or ASP.NET's camel-case?
2. **Packages:** core `Naravel.Pagination` plus `Naravel.Pagination.EntityFrameworkCore`, as above (recommended), or one package?
3. **Cursor:** unsigned with strict validation (recommended), or signed through Data Protection from the start?
4. **S3 deviation:** accept "no Meter and no Fake because there is no I/O" (recommended)?
5. **Cap:** `MaxPerPage` default 100 (recommended)?

## Approval

The owner approved the proposal on 2026-10-19 and accepted the recommended answer to every question: (1) snake_case `data`/`links`/`meta`; (2) the core package plus `Naravel.Pagination.EntityFrameworkCore`; (3) unsigned cursors with strict validation; (4) no `Meter` and no Fake for these I/O-free types; (5) `MaxPerPage` 100.
