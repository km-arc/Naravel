# Review Report — Naravel.Pagination

## Review metadata

- Package ID: `Naravel.Pagination`.
- Source project: `src/Naravel.Pagination/Naravel.Pagination.csproj`.
- Source-review baseline: `main` at `198b36f9e2753ccfb68354a02480cdc25e7eabc7`; this audit branch contains that commit.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source and contract review; independent second review NOT RUN.
- Status: REVIEWED WITH OBSERVATION (1 OBS; source inspection only).
- Files inspected: `Paginated.cs`, `SimplePaginated.cs`, `CursorPaginated.cs`, `PageUrl.cs`, `PaginationOptions.cs`, `CursorCodec.cs`, `InvalidCursorException.cs`, pagination unit tests and NuGet/English guide..
- Dependencies: No EF Core, ASP.NET Core or database project reference..
- Verification performed: source-to-contract tracing and inspection of the listed tests and documentation.
- Verification NOT RUN: .NET restore/build/test, live provider or service execution unless explicitly stated below, full CI and mutation testing.

## Executive assessment

This package represents a page that an application has already fetched. It provides count-aware, simple and cursor result shapes, fixed JSON property names, URL helpers, a default page-size cap and strict cursor parsing. The page constructors retain the list supplied by the caller instead of making a snapshot, despite immutability wording.

## Package and dependency map

- Public results: `Paginated<T>`, `SimplePaginated<T>`, `CursorPaginated<T>`.
- Cursor encoding/decoding: `CursorCodec`; max encoded length 1024.
- Default page size cap: `PaginationOptions.MaxPerPage=100`.
- It performs no database I/O.

## Findings


### [OBS] PGN-001 — Page objects retain a mutable list supplied by the caller

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; runtime reproduction NOT RUN.
- Exact source evidence: The public constructors of `Paginated<T>`, `SimplePaginated<T>` and `CursorPaginated<T>` assign `Items = items` directly. The public `IReadOnlyList<T>` type does not stop the original list owner from changing it.
- Affected API/configuration: Constructors of the three page result types.
- User impact: The serialized data can change later while totals, page numbers, cursors and links still describe the old page.
- Trigger/example: Construct a page from a `List<T>`, then clear or replace items in that original list.
- Why this proves the concern: Both the page object and caller still refer to the same list object. Read-only access through one reference does not make the object immutable.
- Existing test gap: Existing tests cover page calculations and JSON but do not mutate the constructor input afterwards.
- Required safe outcome: Copy the list into a stable snapshot in each constructor, or remove the immutability claim and document caller ownership. A private snapshot is safer.
- Regression tests required: For all three page kinds, construct from a mutable list, mutate and clear the original list, and assert the page data and metadata remain unchanged.
- Owner decision: Not required to record this finding; required before changing runtime behavior.

#### Plain-language explanation

**In everyday words:** The page remembers the same list object the caller gave it. If the caller changes that list later, the page contents change but its page information does not.

**A realistic example:** A page is built with ten rows and total 100. The caller clears the original list. The page now serializes no rows but still says total 100 and page 1.

**What we checked:** The public constructors of `Paginated<T>`, `SimplePaginated<T>` and `CursorPaginated<T>` assign `Items = items` directly. The public `IReadOnlyList<T>` type does not stop the original list owner from changing it.

**Why that proves the concern:** Both the page object and caller still refer to the same list object. Read-only access through one reference does not make the object immutable.

**What is not yet proven:** the review inspected source and existing test code only. The behavior was not reproduced by running a test or a live service in this review.

**What should change:** Copy the list into a stable snapshot in each constructor, or remove the immutability claim and document caller ownership. A private snapshot is safer.

**How we will prove the fix:** For all three page kinds, construct from a mutable list, mutate and clear the original list, and assert the page data and metadata remain unchanged.


## Test assessment

- Existing tests inspected: `PaginatedTests`, `SimpleAndCursorPaginatedTests`, `CursorCodecTests`..
- Coverage seen in source: JSON shape, page math, empty and out-of-range pages, link/query handling, mapping, cursor directions, invalid cursor, length cap and null cursor values..
- Gaps: no tests mutate the original list after constructing a page object.
- Tests actually run for this review: NONE.
- Build/restore: NOT RUN.
- External service/provider integration: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Documentation and packaging assessment

README and guide correctly separate the result-only package from the EF adapter. The immutability wording does not match direct list retention.

## Risk summary and next action

- OBS: PGN-001.
- P0 / P1 / P2 / P3 / DOC: none recorded.

Recommended next task: decide whether result objects promise snapshot semantics; if so, copy input lists and add mutation tests. Do not change runtime code in this audit without owner approval.

## Plain-language explanation

This package describes rows and page links that the application has already fetched; it does not query a database. Source and tests were inspected; no build or test ran.
