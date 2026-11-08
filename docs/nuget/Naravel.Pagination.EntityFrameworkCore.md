# Naravel.Pagination.EntityFrameworkCore

PaginateAsync, SimplePaginateAsync and CursorPaginateAsync for EF Core queries, returning Naravel.Pagination results. Keyset (cursor) paging applies both the order and the filter from one key declaration.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Pagination.EntityFrameworkCore
```

Targets .NET 10. It depends on `Naravel.Pagination`, which NuGet installs with it.

## Use

```csharp
using Naravel.Pagination;   // PaginateAsync, SimplePaginateAsync and CursorPaginateAsync extend IQueryable<T>

// AppDb and its Posts set are your own DbContext and entity.
app.MapGet("/posts", async (AppDb db, int? page, CancellationToken ct) =>
{
    var posts = await db.Posts.OrderBy(p => p.Id).PaginateAsync(page ?? 1, 15, ct);
    return Results.Ok(posts.WithPath("/posts"));   // Results is ASP.NET Core; the page serializes as data, links and meta
});
```

The offset forms need an ordered query: one without `OrderBy` throws `InvalidOperationException`. A page below 1 is read as page 1, a page size below 1 throws `ArgumentOutOfRangeException`, and a size above `PaginationOptions.MaxPerPage` (100 unless you pass your own options) is reduced to it. `SimplePaginateAsync` takes the same arguments and runs no count.

For a feed, declare keys instead of ordering the query:

```csharp
app.MapGet("/feed", async (AppDb db, string? cursor, CancellationToken ct) =>
{
    try
    {
        var feed = await db.Posts.CursorPaginateAsync(k => k.By(p => p.CreatedAt, descending: true).Then(p => p.Id), 15, cursor, ct);
        return Results.Ok(feed.WithPath("/feed"));
    }
    catch (InvalidCursorException)
    {
        return Results.BadRequest("invalid cursor");
    }
});
```

The keys together must be unique, so end with the primary key: if two rows share every key, one of them can be skipped or repeated, and the helper cannot check that. A cursor is a position, not a secret and not a permission. It is not signed, so a client can edit it, and one that cannot be read throws `InvalidCursorException`.

## Documentation

- Pagination: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/pagination.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
