# R23 — Query builder: Insert, update, upsert and delete

Parity section: `R23` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-022 (EN+FA) approved first.
Read: PDR-022, the coverage ledger rows for R23, per-dialect upsert and returning syntax notes in PDR-022.
Touch: `src/Naravel.Database/**`, `tests/Naravel.Database*.Tests/**`, docs EN/FA.
Out of scope: Transactions (R25); Eloquent-style models.
Decisions: Writes require an explicit `Where` for `Update` and `Delete` unless the call is `UpdateAllAsync`/`DeleteAllAsync`-style explicit, to avoid accidental full-table writes; the PDR confirms the exact names.

## Tasks
- [x] **R23.T01 — Insert:** `InsertAsync` (single and batch), `InsertOrIgnoreAsync`, `InsertGetIdAsync`, `InsertUsingAsync`. **Accept:** SQL tests per dialect (`INSERT OR IGNORE`, `INSERT IGNORE`, `ON CONFLICT DO NOTHING`, SQL Server equivalent); `InsertGetId` per dialect (`last_insert_rowid`, `LAST_INSERT_ID`, `RETURNING`, `OUTPUT`). Added with compile tests on four grammars and contract scenarios (single and batch, split over limits, atomic batch, ignore, get-id, insert-using), EN/FA docs; build and tests NOT RUN locally (no .NET SDK); CI is the check. Deviation: SQL Server `InsertOrIgnoreAsync` throws `NotSupportedException` (Laravel does too): there is no single atomic statement for it; `UpsertAsync` (T03) is the SQL Server route.
- [ ] **R23.T02 — Update:** `UpdateAsync`, `UpdateOrInsertAsync`, JSON column update, `IncrementAsync`, `DecrementAsync`, `IncrementEachAsync`, `DecrementEachAsync`, update with joins where the dialect supports it. **Accept:** SQL and execution tests; unsupported combinations throw.
- [ ] **R23.T03 — Upsert:** `UpsertAsync(rows, uniqueBy, update)`. **Accept:** SQL tests (`ON CONFLICT`, `ON DUPLICATE KEY UPDATE`, SQL Server `MERGE`); idempotent re-run test.
- [ ] **R23.T04 — Delete and truncate:** `DeleteAsync`, `TruncateAsync` (identity reset per dialect). **Accept:** SQL and execution tests; full-table protection tests.
- [ ] **R23.T05 — Returning:** `RETURNING`/`OUTPUT` support where the dialect has it. **Accept:** tests on SQLite, PostgreSQL and SQL Server; MySQL throws `NotSupportedException`.
- [ ] **R23.T06 — Validate:** Docs EN/FA, parity, changelog, full verification. **Accept:** DoD 5-7.
