# R16 — Pagination

Parity section: `R16` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-020 (EN+FA) approved first.
Read: EF Core query operator docs; `src/Naravel.Routing/Url/IUrlGenerator.cs` (for links).
Touch: new `src/Naravel.Pagination/**` (no EF dependency), `src/Naravel.Pagination.EntityFrameworkCore/**`, `tests/Naravel.Pagination.Tests/**`, `Naravel.slnx`, docs/PDR-020.
Out of scope: ORM features beyond pagination; Dapper/other ORMs unless the PDR adds them.
Decisions: S2: keyset (cursor) pagination avoids OFFSET cost; EF dependency lives only in the adapter project; SQLite is the approved test database (OD-01).

## Tasks
- [x] **R16.T01 — PDR-020:** page-based, length-aware and cursor types, JSON shape (`data`, `links`, `meta` like Laravel), quickstart (`await query.PaginateAsync(page, perPage)`). **Accept:** PDR EN+FA. Written and approved on 2026-10-19 with the recommended answer to each of its five questions.
- [x] **R16.T02 — Core types:** `Paginated<T>`, `CursorPaginated<T>` with `System.Text.Json` shape. **Accept:** serialization snapshot tests. Added `Paginated<T>`, `SimplePaginated<T>` and `CursorPaginated<T>` with exact-JSON tests; build and tests NOT RUN locally (no .NET SDK), CI is the check.
- [x] **R16.T03 — EF adapter:** `PaginateAsync`, `CursorPaginateAsync` (keyset on ordered unique column). **Accept:** SQLite tests for page edges, empty set, stable cursor under inserts. Added `PaginateAsync`, `SimplePaginateAsync`, `CursorPaginateAsync`, `CursorKeys<T>`, `PaginationOptions`, `InvalidCursorException` and the internal cursor codec, with SQLite tests; build and tests NOT RUN locally (no .NET SDK), CI is the check.
- [x] **R16.T04 — Validate:** benchmark offset vs keyset, docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7. Added `PaginationBenchmarks` (first and deep page, offset against keyset, 50 000 rows on SQLite) and its EN/FA section in `benchmarks.md`; **no numbers recorded: the benchmark was NOT RUN (no .NET SDK), so no speed claim is made (PDR-020 S2)**. Deviation from PDR-020 S2: the cursor codec reads with `JsonDocument` (strict, depth-limited) rather than source generation; the envelope is tiny and fixed, and the hand-written reader is what makes the validation strict. Docs, parity and changelog were done with T02/T03; `roadmap/check.py` passed.
