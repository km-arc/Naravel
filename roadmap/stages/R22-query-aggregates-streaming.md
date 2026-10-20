# R22 — Query builder: Aggregates, existence and streaming

Parity section: `R22` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-022 (EN+FA) approved first.
Read: PDR-022, the coverage ledger rows for R22, `src/Naravel.Database/**` as left by R21.
Touch: `src/Naravel.Database/**`, `tests/Naravel.Database*.Tests/**`, docs EN/FA.
Out of scope: Pagination (R26); writes (R23).
Decisions: Streaming uses `IAsyncEnumerable<T>` and never loads the full result set; chunking is stable under concurrent inserts only when ordered by a unique column, which the docs state.

## Tasks
- [x] **R22.T01 — Aggregates:** `CountAsync`, `MinAsync`, `MaxAsync`, `SumAsync`, `AvgAsync`, `ExistsAsync`, `DoesntExistAsync`. **Accept:** SQL tests on four grammars; execution tests including empty tables (`Sum` of nothing is `null`/0 as documented). Added with compile tests, four contract scenarios (including the empty table: `Sum` is 0, `Avg`/`Min`/`Max` are null) and EN/FA docs; build and tests NOT RUN locally (no .NET SDK); CI is the check. The R22 row in `ROADMAP.md` stays `TODO` until R21.T07 closes R21 (R22 depends on R21), then flips to `DOING`.
- [x] **R22.T02 — Chunking:** `ChunkAsync`, `ChunkByIdAsync`, `ChunkMapAsync`, `EachAsync`, `EachByIdAsync`. **Accept:** tests with exact chunk boundaries, early stop, and rows added during iteration. Added with SQLite tests for exact boundaries, early stop, an empty table, rows added and deleted during the loop and cancellation, plus EN/FA docs; build and tests NOT RUN locally (no .NET SDK); CI is the check.
- [x] **R22.T03 — Lazy and cursor:** `LazyAsync`, `LazyByIdAsync`, `LazyByIdDescAsync`, `CursorAsync` as `IAsyncEnumerable`. **Accept:** memory-bounded test over a large SQLite table; cancellation disposes the reader and connection. Added with a large-table memory-bound test, tests that `break` and cancellation release the reader (a `drop table` succeeds afterwards), page-command counts and EN/FA docs; build and tests NOT RUN locally (no .NET SDK); CI is the check. The memory-bound test measures managed memory with a 25 MB ceiling and may need tuning on CI.
- [x] **R22.T04 — Validate:** Benchmark streaming vs buffered, docs EN/FA, parity, changelog, full verification. **Accept:** DoD 5-7. Build, tests and the benchmark NOT RUN locally (no .NET SDK); CI is the check.
