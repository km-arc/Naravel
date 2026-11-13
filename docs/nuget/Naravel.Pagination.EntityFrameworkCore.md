# Naravel.Pagination.EntityFrameworkCore

[![NuGet](https://img.shields.io/nuget/v/Naravel.Pagination.EntityFrameworkCore.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Pagination.EntityFrameworkCore)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Pagination.EntityFrameworkCore.svg)](https://www.nuget.org/packages/Naravel.Pagination.EntityFrameworkCore)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

PaginateAsync, SimplePaginateAsync and CursorPaginateAsync for EF Core queries, returning Naravel.Pagination results. Keyset (cursor) paging applies both the order and the filter from one key declaration.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Pagination.EntityFrameworkCore` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Pagination` and `Microsoft.EntityFrameworkCore`, which NuGet installs with it, and the EF Core provider that your own `DbContext` uses |

## What you get

- **Three kinds of page.** `PaginateAsync` returns a `Paginated<T>` with a total, `SimplePaginateAsync` a `SimplePaginated<T>` with no count, and `CursorPaginateAsync` a `CursorPaginated<T>` read by keyset.
- **Laravel's page shape.** A page serializes as `data`, `links` and `meta`, and `WithPath` sets where the links point.
- **One key declaration.** `k.By(p => p.CreatedAt, descending: true).Then(p => p.Id)` gives the cursor query its order and its filter.
- **Back and forth.** `NextCursor` and `PreviousCursor` walk a feed in both directions, and an exact fit does not invent a next page.

## Quick start

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

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Pagination`](https://www.nuget.org/packages/Naravel.Pagination) | The page types and options that this package fills from EF Core queries |

## Learn more

- Pagination: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/pagination.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
