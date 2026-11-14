# Review Report — Naravel.Pagination.EntityFrameworkCore

## Review metadata

- Package ID: `Naravel.Pagination.EntityFrameworkCore`.
- Source project: `src/Naravel.Pagination.EntityFrameworkCore/Naravel.Pagination.EntityFrameworkCore.csproj`.
- Source-review baseline: `main` at `198b36f9e2753ccfb68354a02480cdc25e7eabc7`; this audit branch contains that commit.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source and contract review; independent second review NOT RUN.
- Status: REVIEWED WITH FINDINGS (1 P2; source inspection only).
- Files inspected: `CursorKeys.cs`, `PaginationQueryableExtensions.cs`, EF project references, `CursorPagingTests`, `OffsetPagingTests`, NuGet README and pagination guide..
- Dependencies: EF Core Relational and `Naravel.Pagination`..
- Verification performed: source-to-contract tracing and inspection of the listed tests and documentation.
- Verification NOT RUN: .NET restore/build/test, live provider or service execution unless explicitly stated below, full CI and mutation testing.

## Executive assessment

The adapter builds offset and keyset (cursor) pages from EF Core queries. It preserves filters, enforces ordering for offset pages, binds cursor values, validates cursor text and supports compound keys. A nullable cursor key is accepted, but the cursor predicate does not define how null values are ordered; rows with null keys can be skipped by SQL comparisons.

## Package and dependency map

- Extensions: `PaginateAsync`, `SimplePaginateAsync`, `CursorPaginateAsync`.
- Key declaration: `CursorKeys<T>.By/Then`.
- Result DTOs come from `Naravel.Pagination`.
- No provider-specific source dependency.

## Findings


### [P2] PGE-001 — Nullable cursor keys can make later pages skip rows

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; runtime reproduction NOT RUN.
- Exact source evidence: `CursorKey<T,TKey>` unwraps nullable types for its rejection check, so nullable numeric/date/string keys remain allowed. `PaginationQueryableExtensions.After` builds ordinary equality and greater/less comparisons and has no explicit null-rank rule. `CursorCodec.Encode` rejects null only after a row is read at a page edge.
- Affected API/configuration: `CursorPaginateAsync` with nullable `By` or `Then` keys.
- User impact: Some database sort orders put null rows after non-null rows. A later-page predicate such as `Score > @cursorScore` does not match SQL NULL, so the feed can look finished while null rows remain.
- Trigger/example: Rows have normal scores followed by null scores. The cursor reaches the final non-null score and the next query filters with a greater-than expression.
- Why this proves the concern: SQL comparisons with NULL do not evaluate as true. The generated predicate contains no separate branch to include null rows.
- Existing test gap: SQLite tests exercise a null that sorts to the page edge and a nullable column with no null data. No cross-provider test covers null rows that sort after the cursor.
- Required safe outcome: Either reject nullable key types when declared, or define and implement portable null ordering in both order and cursor predicate. Do not silently omit rows.
- Regression tests required: Walk rows with null and non-null keys in ascending and descending order on SQLite, PostgreSQL, MySQL and SQL Server; each row must occur once, or declaration must fail before query execution.
- Owner decision: Owner approval is needed if the selected correction rejects nullable key types that are currently accepted.

#### Plain-language explanation

**In everyday words:** The cursor asks for rows after a value. A database may not consider NULL greater than or equal to any value, so rows whose key is NULL can be skipped.

**A realistic example:** A feed is sorted by optional score. All scored rows are shown first, then rows without a score. After the last scored row the query asks for a score greater than that value; the NULL rows do not match and the feed stops early.

**What we checked:** `CursorKey<T,TKey>` unwraps nullable types for its rejection check, so nullable numeric/date/string keys remain allowed. `PaginationQueryableExtensions.After` builds ordinary equality and greater/less comparisons and has no explicit null-rank rule. `CursorCodec.Encode` rejects null only after a row is read at a page edge.

**Why that proves the concern:** SQL comparisons with NULL do not evaluate as true. The generated predicate contains no separate branch to include null rows.

**What is not yet proven:** the review inspected source and existing test code only. The behavior was not reproduced by running a test or a live service in this review.

**What should change:** Either reject nullable key types when declared, or define and implement portable null ordering in both order and cursor predicate. Do not silently omit rows.

**How we will prove the fix:** Walk rows with null and non-null keys in ascending and descending order on SQLite, PostgreSQL, MySQL and SQL Server; each row must occur once, or declaration must fail before query execution.


## Test assessment

- Existing tests inspected: `CursorPagingTests` and `OffsetPagingTests`..
- Coverage seen in source: forward/backward walks, tie-breakers, descending and mixed-order keys, inserts, invalid cursors, null at a SQLite page edge, page limits and ordered offset queries..
- Gaps: nullable cursor behavior with null rows on PostgreSQL, MySQL and SQL Server; all-provider null ordering contract.
- Tests actually run for this review: NONE.
- Build/restore: NOT RUN.
- External service/provider integration: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Documentation and packaging assessment

The guide states that null boundary values are unsupported and compound keys must be unique. The source accepts nullable key types but does not reject them before the query.

## Risk summary and next action

- P2: PGE-001.
- P0 / P1 / P3 / OBS / DOC: none recorded.

Recommended next task: settle nullable-key policy and add multi-provider contract tests before claiming cursor paging is cross-provider verified. Do not change runtime code in this audit without owner approval.

## Plain-language explanation

This package adds database paging to EF Core. Only source and test files were inspected; no build, automated test or relational provider execution ran.
