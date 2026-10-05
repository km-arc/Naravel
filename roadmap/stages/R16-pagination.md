# R16 — Pagination

Parity section: `R16` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-020 (EN+FA) approved first.
Read: EF Core query operator docs; `src/Naravel.Routing/Url/IUrlGenerator.cs` (for links).
Touch: new `src/Naravel.Pagination/**` (no EF dependency), `src/Naravel.Pagination.EntityFrameworkCore/**`, `tests/Naravel.Pagination.Tests/**`, `Naravel.slnx`, docs/PDR-020.
Out of scope: ORM features beyond pagination; Dapper/other ORMs unless the PDR adds them.
Decisions: S2: keyset (cursor) pagination avoids OFFSET cost; EF dependency lives only in the adapter project; SQLite is the approved test database (OD-01).

## Tasks
- [ ] **R16.T01 — PDR-020:** page-based, length-aware and cursor types, JSON shape (`data`, `links`, `meta` like Laravel), quickstart (`await query.PaginateAsync(page, perPage)`). **Accept:** PDR EN+FA.
- [ ] **R16.T02 — Core types:** `Paginated<T>`, `CursorPaginated<T>` with `System.Text.Json` shape. **Accept:** serialization snapshot tests.
- [ ] **R16.T03 — EF adapter:** `PaginateAsync`, `CursorPaginateAsync` (keyset on ordered unique column). **Accept:** SQLite tests for page edges, empty set, stable cursor under inserts.
- [ ] **R16.T04 — Validate:** benchmark offset vs keyset, docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7.
