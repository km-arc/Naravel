# R25 — Query builder: Transactions and EF Core interop

Parity section: `R25` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-022 (EN+FA) approved first.
Read: PDR-022 sections 3 and 6, `src/Naravel.Queue.Database/**` (existing EF usage), the R19-R24 execution code.
Touch: `src/Naravel.Database/**`, new `src/Naravel.Database.EntityFrameworkCore/**`, `tests/Naravel.Database*.Tests/**`, `Directory.Packages.props` (Microsoft.EntityFrameworkCore.Relational, version already used by the repo), docs EN/FA.
Out of scope: Distributed transactions; a unit-of-work abstraction.
Decisions: Neither side commits or rolls back a transaction it did not start. EF Core is used only in the adapter project, never in core.

## Tasks
- [ ] **R25.T01 — Transactions:** `TransactionAsync(callback)` with commit/rollback, `BeginTransactionAsync`, `CommitAsync`, `RollBackAsync`, savepoints for nesting, optional deadlock retry count. **Accept:** SQLite tests for commit, rollback on exception, nested savepoint, cancellation; server tests env-gated including a forced deadlock retry on SQL Server and PostgreSQL.
- [ ] **R25.T02 — Ambient transaction:** Queries inside a transaction callback automatically use it. **Accept:** test proving two builders in one callback share the connection and roll back together.
- [ ] **R25.T03 — EF Core adapter:** `db.Using(dbContext)` / `dbContext.Query("table")` borrow `Database.GetDbConnection()` and the current `IDbContextTransaction`. **Accept:** SQLite test where an EF write and a builder write commit or roll back together; a test that the adapter never commits a transaction it did not begin.
- [ ] **R25.T04 — Validate:** Docs EN/FA, parity, changelog, full verification. **Accept:** DoD 5-7.
