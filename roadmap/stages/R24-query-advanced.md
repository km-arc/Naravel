# R24 — Query builder: Unions, subqueries, CTEs, locks, raw and debugging

Parity section: `R24` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-022 (EN+FA) approved first.
Read: PDR-022, the coverage ledger rows for R24, the `Raw` safety rules from R19.
Touch: `src/Naravel.Database/**`, `tests/Naravel.Database*.Tests/**`, docs EN/FA.
Out of scope: Transactions (R25).
Decisions: Every `*Raw` method takes bindings and is the only way SQL text enters a query; a test greps the public API for string parameters that are interpreted as SQL outside `Raw`.

## Tasks
- [ ] **R24.T01 — Unions:** `Union`, `UnionAll` with ordering and limit on the union. **Accept:** SQL tests per dialect (SQL Server and SQLite parenthesis rules).
- [ ] **R24.T02 — Subqueries:** `SelectSub`, `FromSub`, `JoinSub`, `LeftJoinSub`, `RightJoinSub`, `JoinLateral`, `LeftJoinLateral`, subquery where forms. **Accept:** binding-order tests; lateral on SQLite throws `NotSupportedException`; SQL Server uses `CROSS/OUTER APPLY`.
- [ ] **R24.T03 — CTEs:** `WithExpression`, recursive CTE. **Accept:** SQL tests per dialect (`WITH RECURSIVE` vs SQL Server `WITH`); a tree-walk execution test.
- [ ] **R24.T04 — Raw expressions:** `Raw`, `SelectRaw`, `WhereRaw`, `OrWhereRaw`, `HavingRaw`, `OrHavingRaw`, `OrderByRaw`, `GroupByRaw`, `FromRaw`. **Accept:** binding tests and injection tests.
- [ ] **R24.T05 — Locks:** `SharedLock`, `LockForUpdate`. **Accept:** SQL per dialect (`FOR SHARE`, `FOR UPDATE`, `WITH (HOLDLOCK)`/`UPDLOCK`); SQLite throws `NotSupportedException`.
- [ ] **R24.T06 — Debugging:** `ToSql`, `ToRawSql` finalized and documented; `dump`/`dd` not ported. **Accept:** `ToRawSql` escapes values per dialect and is documented as for logs only, never for execution.
- [ ] **R24.T07 — Validate:** Docs EN/FA, parity, changelog, full verification. **Accept:** DoD 5-7.
