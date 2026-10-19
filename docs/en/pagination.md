# Pagination

`Naravel.Pagination` returns Laravel-shaped pages (`data`, `links`, `meta`) and `Naravel.Pagination.EntityFrameworkCore` fills them from an EF Core query ([PDR-020](../pdr/en/PDR-020-pagination.md)). The core has no EF Core, ASP.NET Core or database dependency; only the adapter references EF Core.

> Build and tests are NOT RUN locally (no .NET SDK in the authoring environment); CI is the check.

## Quickstart

```sh
dotnet add package Naravel.Pagination.EntityFrameworkCore   # brings Naravel.Pagination
```

```csharp
using Naravel.Pagination;

// Length-aware (Laravel: paginate): two statements, a count and a page.
Paginated<Post> page = await db.Posts.OrderBy(p => p.Id).PaginateAsync(page: 2, perPage: 15);

// Simple (Laravel: simplePaginate): one statement, no count, knows only "is there a next page".
SimplePaginated<Post> simple = await db.Posts.OrderBy(p => p.Id).SimplePaginateAsync(2, 15);

// Cursor (Laravel: cursorPaginate): keyset paging, the same cost on every page.
CursorPaginated<Post> next = await db.Posts.CursorPaginateAsync(k => k.By(p => p.CreatedAt, descending: true).Then(p => p.Id), 15, cursor);

return page.WithPath("/posts").WithQuery(Request.Query.Where(q => q.Key != "page").Select(q => KeyValuePair.Create(q.Key, (string?)q.Value))); // serializes as Laravel's JSON
```

## What you get

| Laravel | Naravel | Cost |
|---|---|---|
| `paginate($n)` | `PaginateAsync(page, perPage)` → `Paginated<T>` | `count(*)` + `limit/offset` |
| `simplePaginate($n)` | `SimplePaginateAsync(page, perPage)` → `SimplePaginated<T>` | one statement, reads `perPage + 1` rows |
| `cursorPaginate($n)` | `CursorPaginateAsync(keys, perPage, cursor)` → `CursorPaginated<T>` | one statement, `where` on the keys, no offset |
| `through($fn)` | `Map(fn)` | none |
| `withPath`, `appends`, `getPageName` | `WithPath`, `WithQuery`, `WithPageName` / `WithCursorName` | none |

The page number comes from you (`Request.Query["page"]`); Naravel does not read the current request.

## JSON

Length-aware:

```json
{"data":[...],
 "links":{"first":"/posts?page=1","last":"/posts?page=3","prev":null,"next":"/posts?page=2"},
 "meta":{"current_page":1,"from":1,"last_page":3,"path":"/posts","per_page":10,"to":10,"total":25}}
```

Simple pages have no `last` and no `total`; cursor pages have `meta: {path, per_page, next_cursor, prev_cursor}` and `links: {prev, next}`. The names are fixed with attributes, so they do not depend on the application's JSON naming policy, and `null` is always written (not omitted).

## Page edges

| Case | Result |
|---|---|
| `page` below 1 | treated as 1 |
| Page past the end | empty `data`, correct `total` and `last_page` |
| Empty table | `last_page` 1, `from` and `to` `null` |
| `perPage` above the maximum (100) | reduced to the maximum; change it with `new PaginationOptions { MaxPerPage = 50 }` |
| `perPage` below 1 | `ArgumentOutOfRangeException` |
| Query with no `OrderBy` (`PaginateAsync`, `SimplePaginateAsync`) | `InvalidOperationException`: a page of an unordered query is not stable |

Why the maximum exists: the page size usually comes from a query string, and a client asking for a million rows is a denial of service. Laravel leaves this to the caller; Naravel does not.

`PaginateAsync` runs two statements, so a write between them can make the total differ from the page by a row. Offset paging also reads and throws away every skipped row, so deep pages get slower.

## Cursor paging

You declare the sort keys **once**. The helper then writes both the `ORDER BY` and the `WHERE` from that declaration, so they cannot disagree:

```csharp
k => k.By(p => p.CreatedAt, descending: true).Then(p => p.Id)
```

Rules, each enforced:

- **Together the keys must be unique.** End with the primary key. If two rows share all keys, one can be skipped or repeated; the helper cannot check this.
- A key is a plain member access (`p => p.Id`, `p => p.Author.Id`); anything else is an `ArgumentException`.
- Key types: numbers, `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly`, `string` (strings compare with the database's collation). `Guid`, enums and `bool` throw `NotSupportedException` when declared, not on the first request that has a cursor.
- A key value must not be `null`; a `null` on a boundary item is an `InvalidOperationException` naming the key. Use a column that is never null.
- An `OrderBy` already on the query is replaced: the declared keys are the primary order. Filters are kept.

Multi-key predicate: `a > x or (a = x and b > y)`, flipped for descending keys and for paging backwards. The cursor values go to the database as **parameters**; column names come only from your declaration, never from a cursor.

### The cursor

An opaque base64url string of `{"d":"n"|"p","k":[...]}` (direction and key values), at most 1024 characters. It is a **position, not a secret and not a permission**: it is not signed, so a client can edit it, and all that can do is move the position. Do not put anything in a cursor that the client may not see, and do not use it to enforce access (filter in the query instead). Reading is strict: bad base64, wrong JSON shape, wrong number of values, a value that does not fit the key type, a null, or too long all throw `InvalidCursorException` (the message never contains the cursor). Map it to HTTP 400:

```csharp
try { return Ok(await query.CursorPaginateAsync(keys, 15, cursor)); }
catch (InvalidCursorException) { return BadRequest(); }
```

`PreviousCursor` is `null` on the first page and `NextCursor` is `null` on the last; an exact fit (exactly `perPage` rows left) does not invent a next page, because one extra row is read to find out. Walking back with `PreviousCursor` returns the earlier page in its normal order.

### Why keyset

| | Offset (`PaginateAsync`) | Keyset (`CursorPaginateAsync`) |
|---|---|---|
| Cost of page N | grows with N (skipped rows are read) | the same for every page, with an index on the keys |
| A row inserted behind the reader | pushes one row onto the next page (seen twice) | no effect |
| Jump to page N | yes | no, only next and previous |
| Total count | yes | no |

R16.T04 records a measured comparison; until then no numbers are claimed.

## Not ported

- Reading the page from the current HTTP request, and `links()` HTML rendering: build the links with `WithPath`/`WithQuery`, render in your view.
- `paginate` on the Naravel query builder (`Naravel.Database`): planned in R26, after R22.
- No Meter or Fake: the types do no I/O, so there is nothing to meter or fake (a deviation from DoD S3, stated in PDR-020).
