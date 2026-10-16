# R21 — Query builder: Where family, joins, grouping and ordering helpers

Parity section: `R21` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-022 (EN+FA) approved first.
Read: PDR-022, the coverage ledger rows for R21, the R19/R20 grammars and contract suite.
Touch: `src/Naravel.Database/**`, `tests/Naravel.Database*.Tests/**`, docs EN/FA.
Out of scope: Aggregate functions and streaming (R22); writes (R23); unions, subqueries and raw expressions (R24).
Decisions: Every method is added to all four grammars and to the contract suite in the same task. Methods a dialect cannot express throw `NotSupportedException` with the dialect named, and the dialect matrix in the docs is asserted by a test.

## Tasks
- [ ] **R21.T01 — Value and null where:** `WhereIn`, `WhereNotIn`, `WhereBetween`, `WhereNotBetween`, `WhereBetweenColumns`, `WhereNull`, `WhereNotNull`, `WhereColumn` and every `OrWhere` form. **Accept:** SQL and bindings tests on four grammars; empty `WhereIn` list produces a false condition, not invalid SQL.
- [ ] **R21.T02 — Date and multi-column where:** `WhereDate`, `WhereMonth`, `WhereDay`, `WhereYear`, `WhereTime`, `WhereAny`, `WhereAll`, `WhereNone`. **Accept:** tests per dialect (`strftime`, `EXTRACT`, `DATEPART`/`YEAR()` differences).
- [ ] **R21.T03 — Pattern, full text and JSON:** `WhereLike`, `WhereNotLike`, `WhereFullText`, `WhereJsonContains`, `WhereJsonDoesntContain`, `WhereJsonContainsKey`, `WhereJsonLength`, JSON path columns. **Accept:** per-dialect SQL tests; `WhereFullText` on SQLite throws `NotSupportedException`; execution on the servers env-gated.
- [ ] **R21.T04 — Grouping, exists and conditionals:** Nested closures, `WhereExists`, `WhereNotExists`, subquery where, `When`, `Unless`. **Accept:** tests including deep nesting and binding order.
- [ ] **R21.T05 — Joins:** `Join`, `LeftJoin`, `RightJoin`, `CrossJoin`, advanced join clauses (`On`, `OrOn`, `Where` inside a join). **Accept:** tests per dialect; `RIGHT JOIN` on SQLite documented per the engine version in use.
- [ ] **R21.T06 — Group, having and ordering:** `GroupBy`, `Having`, `OrHaving`, `HavingBetween`, `HavingNull`, `Latest`, `Oldest`, `InRandomOrder`, `Reorder`, `ReorderDesc`. **Accept:** tests per dialect (`RANDOM()`, `RAND()`, `NEWID()`).
- [ ] **R21.T07 — Validate:** Docs EN/FA usage and dialect matrix, parity, changelog, PublicAPI, full verification. **Accept:** DoD 5-7.
