# Naravel.Pagination

[![NuGet](https://img.shields.io/nuget/v/Naravel.Pagination.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Pagination)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Pagination.svg)](https://www.nuget.org/packages/Naravel.Pagination)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Laravel-style pagination results for .NET: length-aware, simple and cursor pages with Laravel's data/links/meta JSON. No EF Core, ASP.NET Core or database dependency.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Pagination` |
| **Targets** | .NET 10 |
| **Needs** | No other package: no EF Core, ASP.NET Core or database dependency |
| **Add-on** | [`Naravel.Pagination.EntityFrameworkCore`](https://www.nuget.org/packages/Naravel.Pagination.EntityFrameworkCore) fills a page from an EF Core query |

## What you get

- **Laravel-shaped pages.** System.Text.Json writes a page as `data`, `links` and `meta`.
- **Three kinds of page.** Length-aware `Paginated<T>`, `SimplePaginated<T>` and cursor `CursorPaginated<T>`.
- **Convert and decorate.** `Map` converts the items and keeps the page information; `WithPath` and `WithQuery` set the links, `WithQuery` adding the other query-string values.
- **Cursor paging without drift.** With the EF Core adapter you declare the sort keys once and the helper writes both the `ORDER BY` and the `WHERE`.
- **No dependencies.** Your own code reads the rows; this package only describes the page.

## Quick start

```csharp
using Naravel.Pagination;

// Your own code reads one page of rows and the total number of rows; this package describes the page.
IReadOnlyList<string> rows = ["Ada", "Grace"];
var page = new Paginated<string>(rows, 25, 1, 2).WithPath("/people");   // items, total, current page, per page

// Results is ASP.NET Core, not part of this package. System.Text.Json writes the page as data, links and meta.
app.MapGet("/people", () => Results.Ok(page));
```

`Paginated<T>` exposes `Items`, `Total`, `CurrentPage`, `PerPage` and `LastPage`. `SimplePaginated<T>` has no total and `CursorPaginated<T>` pages by a cursor; the guide describes both.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Pagination.EntityFrameworkCore`](https://www.nuget.org/packages/Naravel.Pagination.EntityFrameworkCore) | `PaginateAsync`, `SimplePaginateAsync` and `CursorPaginateAsync` for EF Core queries; the cursor keys are declared once and drive both the order and the filter |

## Learn more

- Pagination: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/pagination.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
