# Naravel.Pagination

Laravel-style pagination results for .NET: length-aware, simple and cursor pages with Laravel's data/links/meta JSON. No EF Core, ASP.NET Core or database dependency.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Pagination
```

Targets .NET 10.

## Use

```csharp
using Naravel.Pagination;

// Your own code reads one page of rows and the total number of rows; this package describes the page.
IReadOnlyList<string> rows = ["Ada", "Grace"];
var page = new Paginated<string>(rows, 25, 1, 2).WithPath("/people");   // items, total, current page, per page

// Results is ASP.NET Core, not part of this package. System.Text.Json writes the page as data, links and meta.
app.MapGet("/people", () => Results.Ok(page));
```

`Paginated<T>` exposes `Items`, `Total`, `CurrentPage`, `PerPage` and `LastPage`. `Map` converts the items and keeps the page information, and `WithQuery` adds the other query-string values to the links. `SimplePaginated<T>` has no total and `CursorPaginated<T>` pages by a cursor; the guide describes both.

## Related packages

- [`Naravel.Pagination.EntityFrameworkCore`](https://www.nuget.org/packages/Naravel.Pagination.EntityFrameworkCore): PaginateAsync, SimplePaginateAsync and CursorPaginateAsync for EF Core queries, returning Naravel.Pagination results. Keyset (cursor) paging applies both the order and the filter from one key declaration.

## Documentation

- Pagination: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/pagination.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
