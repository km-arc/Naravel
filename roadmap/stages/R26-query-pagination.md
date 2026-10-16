# R26 — Query builder: Pagination integration

Parity section: `R26` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-022 (EN+FA) approved first.
Read: PDR-022, PDR-020 and the R16 types, the coverage ledger row for R26.
Touch: `src/Naravel.Database/**`, `tests/Naravel.Database*.Tests/**`, docs EN/FA.
Out of scope: New pagination types (they belong to R16).
Decisions: Uses the R16 `Paginated<T>` and `CursorPaginated<T>`; this stage must not start before R16 is done.

## Tasks
- [ ] **R26.T01 — Page-based:** `PaginateAsync(page, perPage)` (count plus page) and `SimplePaginateAsync`. **Accept:** SQL tests per dialect; edge tests for empty, last and out-of-range pages.
- [ ] **R26.T02 — Cursor:** `CursorPaginateAsync` on an ordered unique column. **Accept:** stable cursor under inserts on SQLite; server tests env-gated.
- [ ] **R26.T03 — Validate:** Docs EN/FA, parity, changelog, full verification. **Accept:** DoD 5-7.
