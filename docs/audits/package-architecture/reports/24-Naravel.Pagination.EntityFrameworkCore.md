# Review Report — Naravel.Pagination.EntityFrameworkCore

## Review metadata

- Package ID: Naravel.Pagination.EntityFrameworkCore.
- Source project: src/Naravel.Pagination.EntityFrameworkCore/Naravel.Pagination.EntityFrameworkCore.csproj.
- Baseline: main at 282a507bead92ae9ca40a9cb060baa2ee1dd7287.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source, test and documentation review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; provider/runtime verification pending).
- Files inspected: PaginationQueryableExtensions, CursorKeys, project metadata/references, offset and cursor tests, package README and Pagination guide.
- Dependencies: Naravel.Pagination and Microsoft.EntityFrameworkCore.
- Verification performed: order detection, offset calculation, count/page queries, cursor expression building, parameter binding, sort directions, null key handling, page-size limits and SQLite test coverage inspected.
- Verification NOT RUN: restore, build, tests, live PostgreSQL/MySQL/SQL Server provider tests, large-table boundary reproduction, mutation testing and independent review.

## Executive assessment

The adapter keeps database work out of the core package and uses EF Core query operations. Offset paging requires an ordered query; cursor paging builds the filter and order from the declared keys and parameterizes cursor values. The tests cover forward/backward walks, mixed sort directions, filtering, inserts behind a cursor and invalid cursor inputs.

One boundary defect remains: when an offset is larger than Int32.MaxValue, the adapter returns an empty page even if the total row count proves that rows exist beyond that offset. EF Core's Skip API used here accepts an Int32, but returning a normal empty page hides the fact that the requested position could not be represented.

## Package and dependency map

- Public operations: PaginateAsync, SimplePaginateAsync and CursorPaginateAsync extension methods for IQueryable<T>.
- Key declaration: CursorKeys<T> with By/Then key expressions.
- Dependencies: Naravel.Pagination and EF Core; no queue/cache/database driver dependency.
- Supported query contract: the caller supplies filters and an order for offset paging; cursor paging replaces the prior order with the declared cursor keys.
- Test provider inspected: SQLite through PostDb. The audit does not claim that PostgreSQL, MySQL or SQL Server ran.

## Findings

### [P3] PAG-001 — An offset beyond Int32 range is returned as an ordinary empty page

- Remediation state: OPEN.
- Evidence status: SEEN IN SOURCE; large-offset provider reproduction NOT RUN.
- Evidence: src/Naravel.Pagination.EntityFrameworkCore/PaginationQueryableExtensions.cs. PaginateAsync calculates the requested offset as a long and returns an empty list when offset > int.MaxValue. SimplePaginateAsync uses the same boundary and returns an empty list with HasMorePages=false. The total count is available in PaginateAsync, but it is not used to distinguish a true past-the-end page from an offset that the implementation cannot express.
- In everyday words: The caller can ask for a page that exists in a very large table, but the adapter silently tells the caller that the page is empty because the offset is too large for the method it uses.
- Realistic example: A table has more than 2.2 billion rows. The caller asks for a page whose first row is within that table but whose offset is over 2.1 billion. PaginateAsync returns no rows and still reports a total large enough to contain them. SimplePaginateAsync also says there is no next page.
- Why the evidence proves the risk: the adapter deliberately converts the unrepresentable offset to an empty result without checking whether it is past the total. The public argument permits page values that can produce this offset.
- What is not proven: the control flow is visible in source; the audit did not create a multi-billion-row database or run a provider-specific large-offset test.
- Required safe outcome: do not present an unrepresentable offset as a successful empty page. Fail clearly with a documented exception or use a query path that can express the requested offset when the provider supports it. A page genuinely past the total should continue to return an empty page.
- Regression tests required: test the boundary just below, at and above Int32.MaxValue. Use a query/provider test double or an expression inspection seam to prove that a large valid total cannot be mistaken for a past-the-end page; retain the current true past-the-end tests.
- Public API / PDR / release impact: behavioral correction or an explicit boundary exception; public API may be unchanged if a standard exception is selected.

## Existing guarantees and coverage

- Offset and simple paging reject unordered queries and clamp page size through PaginationOptions.
- Cursor paging declares a set of keys and builds both the query order and the cursor comparison from those same keys.
- Cursor key expressions must be member access; unsupported key types are rejected when keys are declared.
- Cursor values are sent through captured-value expression nodes so EF Core treats them as parameters rather than embedding them as SQL literals.
- Tests cover forward/backward navigation, mixed direction keys, filters, nullable keys with no null data, and null boundary rejection.
- The test suite uses SQLite; it does not establish provider behavior for every supported relational database.

## Test assessment

- Tests inspected: OffsetPagingTests, CursorPagingTests and PostDb in Naravel.Pagination.EntityFrameworkCore.Tests.
- Missing tests: offsets beyond Int32.MaxValue when rows still exist; provider matrix for generated cursor comparisons.
- Tests actually run in this review: NONE.
- Build/CI, PostgreSQL/MySQL/SQL Server execution, mutation testing and independent review: NOT RUN.

## Documentation and packaging assessment

- The README explains that cursor keys must be unique together (typically ending with a primary key) and that nullable cursor key values are unsupported.
- The package page distinguishes offset paging from cursor paging and states that count/page queries are separate statements.
- No package example was compiled in this audit.

## Risk summary and next action

- P3: PAG-001.
- Recommended next task: define the unsupported-offset behavior and add boundary tests before changing the implementation. No runtime change is authorized by this audit.

## Plain-language explanation

The EF Core adapter runs the pagination queries. It correctly asks the caller to provide an order and builds cursor filters from the same keys used for sorting. At an extreme table size, however, it currently returns an empty page when the offset exceeds the integer range, even when the count says rows should exist. That boundary was found in source; it was not reproduced against a huge database.
