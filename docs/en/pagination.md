# Pagination

`Naravel.Pagination` returns Laravel-shaped pages (`data`, `links`, `meta`) and `Naravel.Pagination.EntityFrameworkCore` fills them from an EF Core query ([PDR-020](../pdr/en/PDR-020-pagination.md)). The core has no EF Core, ASP.NET Core or database dependency; only the adapter references EF Core.

> Build and tests are NOT RUN locally (no .NET SDK in the authoring environment); CI is the check.

## Quickstart

A minimal API over EF Core and SQLite with the three kinds of page: length-aware, simple and cursor. It creates its own database file and 25 posts the first time it runs.

```sh
dotnet add package Naravel.Pagination.EntityFrameworkCore   # brings Naravel.Pagination
dotnet add package Microsoft.EntityFrameworkCore.Sqlite      # any EF Core provider works; SQLite needs no server
```

```csharp
using Microsoft.EntityFrameworkCore;
using Naravel.Pagination;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDb>(o => o.UseSqlite("Data Source=posts.db"));
var app = builder.Build();

// The first run creates posts.db with 25 posts. Delete the file to start again.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    if (db.Database.EnsureCreated())
    {
        for (var i = 1; i <= 25; i++)
        {
            db.Posts.Add(new Post { Title = $"post-{i:000}", CreatedAt = new DateTime(2026, 1, 1).AddDays(i) });
        }

        db.SaveChanges();
    }
}

// Length-aware (Laravel: paginate): GET /posts?page=2&perPage=10. Two statements, a count and a page.
// The page number comes from you; Naravel does not read the request.
app.MapGet("/posts", async (AppDb db, HttpRequest request, CancellationToken ct, int page = 1, int perPage = 10) =>
{
    var result = await db.Posts.OrderBy(p => p.Id).PaginateAsync(page, perPage, ct);
    return Results.Ok(result
        .WithPath("/posts")
        .WithQuery(request.Query.Where(q => q.Key != "page").Select(q => KeyValuePair.Create(q.Key, (string?)q.Value))));
});

// Simple (Laravel: simplePaginate): GET /posts/simple?page=2. One statement and no count: it knows only whether a next page exists.
app.MapGet("/posts/simple", async (AppDb db, CancellationToken ct, int page = 1) =>
    Results.Ok((await db.Posts.OrderBy(p => p.Id).SimplePaginateAsync(page, 10, ct)).WithPath("/posts/simple")));

// Cursor (Laravel: cursorPaginate): GET /posts/feed, then GET /posts/feed?cursor=<meta.next_cursor>. Keyset paging.
app.MapGet("/posts/feed", async (AppDb db, CancellationToken ct, string? cursor) =>
{
    try
    {
        var feed = await db.Posts.CursorPaginateAsync(k => k.By(p => p.CreatedAt, descending: true).Then(p => p.Id), 10, cursor, ct);
        return Results.Ok(feed.WithPath("/posts/feed"));
    }
    catch (InvalidCursorException)
    {
        return Results.BadRequest("invalid cursor");   // malformed: cut, not base64url, or not the expected shape
    }
});

app.Run();

public sealed class Post
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Post> Posts => Set<Post>();
}
```

`GET /posts?page=3` answers with the last page of the 25 posts (the `data` array is shortened here to its first item):

```text
{"data":[{"id":21,"title":"post-021","createdAt":"2026-01-22T00:00:00"}],
 "links":{"first":"/posts?page=1","last":"/posts?page=3","prev":"/posts?page=2","next":null},
 "meta":{"current_page":3,"from":21,"last_page":3,"path":"/posts","per_page":10,"to":25,"total":25}}
```

`GET /posts/feed` returns posts 25 down to 16 (newest first) with `meta.next_cursor` and `links.next`; sending that cursor back as `?cursor=` returns the next ten, and so on until `next_cursor` is `null`. A cursor that is malformed (cut, not base64url, or not the expected shape) answers `400`. A cursor that was edited into another valid position is accepted and moves the reader there: it is not signed (see [The cursor](#the-cursor)).

`WithQuery` repeats the other query-string values (for example `perPage`) in every link. `Map` shapes the rows without a second query, as Laravel's `through`: `result.Map(p => new PostDto(p.Id, p.Title))` (`PostDto` is your own type).

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

A measured comparison on in-memory SQLite is in [benchmarks.md](benchmarks.md); the numbers depend on the engine, the page depth and the indexes, so measure on your own data.

## Errors

The messages below are the ones in the source. .NET adds ` (Parameter 'name')` to the message of an argument exception, which the tables leave out. `PaginateAsync`, `SimplePaginateAsync` and `CursorPaginateAsync` are not `async` methods themselves: the errors for the page size, the ordering, the keys and the cursor a client sends are thrown by the call, before any statement runs, so `try { await ... }` catches them as well. A null key value is thrown from the returned task.

**Page size, ordering and the page types.**

| When | Exception and message |
|---|---|
| `perPage` below 1 in `PaginateAsync`, `SimplePaginateAsync`, `CursorPaginateAsync` or `PaginationOptions.ClampPerPage` | `ArgumentOutOfRangeException`, parameter `perPage` |
| `PaginationOptions.MaxPerPage` set below 1 | `ArgumentOutOfRangeException`, parameter `value` |
| `PaginateAsync` or `SimplePaginateAsync` on a query with no `OrderBy` | `InvalidOperationException`: `The query has no OrderBy. A page of an unordered query is not stable; order it, ending with a unique column.` |
| A page type built by hand with `currentPage` or `perPage` below 1, or (`Paginated`) a negative `total` | `ArgumentOutOfRangeException`, parameter `currentPage`, `perPage` or `total` |
| `SimplePaginated` or `CursorPaginated` built with more items than `perPage` | `ArgumentException`, parameter `items`: `A page cannot hold more items than the page size.` |
| `WithPath`, `WithPageName` or `WithCursorName` given an empty or whitespace text | `ArgumentException` |

**Cursor keys.** Checked when the keys are declared, not when the first cursor arrives.

| When | Exception and message |
|---|---|
| The callback of `CursorPaginateAsync` declares no key | `ArgumentException`, parameter `keys`: `Declare at least one key with By.` |
| `Then` before `By` | `InvalidOperationException`: `Declare the first key with By before Then.` |
| `By` called twice | `InvalidOperationException`: `By declares the first key; use Then for the others.` |
| A key that is not a member access, such as `p => p.Id + 1` | `ArgumentException`, parameter `selector`: `A cursor key must be a property or field access such as p => p.Id.` |
| A `Guid`, `bool` or enum key (`Token` is the name of the key in this example; the type name is the CLR one, so `Boolean` for `bool`) | `NotSupportedException`: `The cursor key 'Token' has type Guid, which has no ordering the databases agree on. Use a number, DateTime, DateTimeOffset, TimeSpan, DateOnly, TimeOnly or string key (or its underlying number).` A type that has no comparison the database can run gets the same sentence with `which has no comparison the database can run` in place of `which has no ordering the databases agree on`. |
| A `null` key value on the first or last item of a page, when the cursor for it is written | `InvalidOperationException`, thrown from the task: `The cursor key 'Score' is null on an item at the edge of the page; null key values are not supported. Use a key that is never null.` |

**The cursor a client sends.** Every rejection is an `InvalidCursorException`. It derives from `Exception`, not from `ArgumentException` or `InvalidOperationException`, so catch it by its own type. The message never contains the cursor.

| The cursor | Message |
|---|---|
| An empty string (`?cursor=` with no value arrives as one; `null` means the first page) | `The cursor is empty.` |
| Longer than 1024 characters | `The cursor is longer than 1024 characters.` |
| Not base64url | `The cursor is not valid base64url.` |
| Not the expected JSON, a direction other than `n` or `p`, a different number of values than there are keys, or a value that does not fit its key type | `The cursor does not hold the expected position.` |
| A `null` among the key values | `The cursor holds a null key value.` |

`tests/Naravel.Pagination.EntityFrameworkCore.Tests/DocsExampleTests.cs` runs the Quickstart's statements and every message above, apart from the `which has no comparison the database can run` variant, which comes from the source and is not run there. Change one, change the other.

## Testing

The page types do no I/O, so there is no fake to inject (see "Not ported"). Two ways to test code that returns a page:

**Without a database.** The constructors are public, so a test can build the page it wants:

```csharp
var page = new Paginated<int>([21, 22, 23, 24, 25], total: 25, currentPage: 3, perPage: 10).WithPath("/posts");
var simple = new SimplePaginated<int>([1, 2], currentPage: 1, perPage: 2, hasMorePages: true).WithPath("/posts");
var feed = new CursorPaginated<int>([1, 2], perPage: 2, nextCursor: "abc", previousCursor: null).WithPath("/posts/feed");

// page.LastPage is 3, page.HasMorePages is false and page.Links.Previous is "/posts?page=2".
// simple.Links.Next is "/posts?page=2" and feed.Links.Next is "/posts/feed?cursor=abc".
```

**Against SQLite in memory.** To test the query side (the `OrderBy`, the keys, the cursors) use a real database. SQLite in memory needs no server; keep the connection open, because the database lives only as long as it does:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
var options = new DbContextOptionsBuilder<AppDb>().UseSqlite(connection).Options;
using var db = new AppDb(options);
db.Database.EnsureCreated();
for (var i = 1; i <= 25; i++)
{
    db.Posts.Add(new Post { Title = $"post-{i:000}", CreatedAt = new DateTime(2026, 1, 1).AddDays(i) });
}

db.SaveChanges();

var page = await db.Posts.OrderBy(p => p.Id).PaginateAsync(page: 3, perPage: 10);
// page.Items holds post-021 to post-025, page.Total is 25 and page.HasMorePages is false.
```

The repository's own tests use the same approach (`PostDb.cs` in `tests/Naravel.Pagination.EntityFrameworkCore.Tests`). The cursor walk (forward, then back with `PreviousCursor`) and each documented message are in `DocsExampleTests.cs` next to it. How a SQL engine orders `NULL` or compares strings is the engine's rule, so a test on SQLite does not prove the order on SQL Server or PostgreSQL; the keys are meant to be non-null and the comparison of a string key uses the database's collation.

## On the Naravel query builder

`Naravel.Database` returns these same types: `PaginateAsync(page, perPage)`, `SimplePaginateAsync` and `CursorPaginateAsync(perPage, cursor, column)` on a query. The offset forms need an `OrderBy`; the cursor form follows one unique, never-null column that is among the selected columns. How each engine behaves is a documented contract: [database.md](database.md) and [database-dialects.md](database-dialects.md).

## Not ported

- Reading the page from the current HTTP request, and `links()` HTML rendering: build the links with `WithPath`/`WithQuery`, render in your view.
- No Meter or Fake: the types do no I/O, so there is nothing to meter or fake (a deviation from DoD S3, stated in PDR-020).
