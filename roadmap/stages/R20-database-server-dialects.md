# R20 — Query builder: MySQL, PostgreSQL and SQL Server

Parity section: `R20` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-022 (EN+FA) approved first.
Read: PDR-022, the R19 grammar tests, `.github/workflows/ci.yml` (`services` job), `tests/Shared/ServiceFactAttribute.cs`.
Touch: new `src/Naravel.Database.MySql/**`, `src/Naravel.Database.SqlServer/**`, `src/Naravel.Database.Postgres/**`, matching tests, `Naravel.slnx`, `Directory.Packages.props` (MySqlConnector, Microsoft.Data.SqlClient, Npgsql), `.github/workflows/ci.yml`, docs EN/FA.
Out of scope: New query methods (later stages); a connection pool of our own.
Decisions: Env-gated tests (`NARAVEL_TEST_MYSQL`, `NARAVEL_TEST_POSTGRES`, `NARAVEL_TEST_SQLSERVER`) run only on Linux in the separate parallel `database` job (PDR-022 A6; OD-01, no Testcontainers). Skipped means NOT RUN locally.

## Tasks
- [ ] **R20.T01 — Package versions:** Verify current stable versions of MySqlConnector, Microsoft.Data.SqlClient and Npgsql for `net10.0`; add to `Directory.Packages.props`. **Accept:** restore and build pass; NuGet audit has no findings.
- [ ] **R20.T02 — CI service containers:** Add MySQL, PostgreSQL and SQL Server containers with health checks to the `services` job and export the three `NARAVEL_TEST_*` variables. **Accept:** the job starts all containers and a trivial `SELECT 1` test runs on each. PDR-022 A6: use a separate Linux `database` job with one matrix leg per server (parallel), tmpfs and relaxed-durability settings, NuGet cache, path filter for PRs (always on `main` and nightly), and a final `ci-ok` job that treats skipped as success. Record the measured duration of each leg. Owner action after merge: switch the required checks of branch protection to `ci-ok` alone.
- [ ] **R20.T03 — Driver packages:** Three driver packages with `AddNaravelMySqlDatabase`, `AddNaravelSqlServerDatabase`, `AddNaravelPostgresDatabase`. **Accept:** `Extend`/reload tests and a connection-open test per server.
- [ ] **R20.T04 — Shared contract suite:** One parameterized suite that runs every method of R19 on SQLite and on each env-gated server, asserting identical results. **Accept:** green on all four in CI; dialect differences found are fixed in the grammar or documented as `NotSupportedException`. PDR-022 A2/A6: one shared xUnit collection fixture per dialect (schema and seed once, unique prefix, tests in parallel on separate tables), a type round-trip test, and an xUnit trait `Dialect` so each leg runs only its own suite.
- [ ] **R20.T05 — Validate:** Docs EN/FA (connection strings, per-server notes, dialect matrix), parity, changelog, full verification. **Accept:** DoD 5-7.
