# Review Report — Naravel.Database.Sqlite

## Review metadata

- Package ID: `Naravel.Database.Sqlite` (explicit `PackageId` in `src/Naravel.Database.Sqlite/Naravel.Database.Sqlite.csproj`).
- Source project: `src/Naravel.Database.Sqlite/Naravel.Database.Sqlite.csproj`.
- Source-review baseline: `main` at `44e957ca3db6d9f15665fc8fa070f70522c163c9`; rechecked against current `main` at `c6a7d58ffb84d885741998bb32d2082c54c4ab61`. SQLite driver source and its Database.Tests coverage are unchanged across that range.
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source and contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (1 P2, 1 DOC; source inspection only).
- Files inspected: project metadata and pinned package references; `SqliteDatabaseConnection`; registration/connection-string normalization; in-memory keeper and disposal lifecycle; DatabaseManager tests for SQLite memory/file behavior; database PDR and dialect guide; NuGet README.
- Dependency boundary: optional provider package depends on `Naravel.Database` and `Microsoft.Data.Sqlite`; SQLite provider types remain outside the core package.
- Verification performed: connection-string mode/cache behavior trace; named in-memory database isolation/lifetime analysis; cancellation/open failure and disposal path inspection; package-version pin comparison.
- Verification NOT RUN: `.NET restore/build/test`, SQLite runtime reproduction for `Mode=Memory`, concurrent dispose/open fault injection, full CI, mutation testing and independent review.

## Executive assessment

- The provider is small and correctly isolates Microsoft.Data.Sqlite from the provider-neutral core.
- The `:memory:` path is deliberately converted to a unique named shared-cache database and a keeper connection preserves it across per-operation connections. Tests cover persistence across leases and isolation between two `:memory:` connection names.
- The documented `Mode=Memory` alternative is not normalized in the same way: when a named in-memory database uses the default private cache, operation connections do not see the keeper's database; if callers explicitly use shared cache and reuse the same data-source name across Naravel connection definitions, the definitions can share a database contrary to the class's isolation guarantee.
- The central package pin is `SQLitePCLRaw.lib.e_sqlite3` 2.1.14, while the English database guide still says 2.1.13.
- No provider code or dependency version was changed by this review.

## Findings

### [P2] SQL-001 — Mode=Memory is not converted to a per-definition shared in-memory database

- Remediation state: OPEN.
- Architecture disposition: VETO AV-019 — every SQLite in-memory connection definition must have a unique named shared-cache database held alive by its keeper; no connection may silently use a private per-operation database or share state with another Naravel connection name.
- Evidence status: CONFIRMED BY SOURCE AND MICROSOFT.DATA.SQLITE CONNECTION-STRING DEFAULTS; runtime reproduction NOT RUN.
- Evidence: `SqliteDatabaseConnection` treats `builder.Mode == SqliteOpenMode.Memory` as an in-memory database and opens a keeper, but it only rewrites the data source and forces `Cache=Shared` when the original data source is exactly `:memory:`. For a named `Data Source=memdb;Mode=Memory` with the default `Cache=Private`, each new operation connection gets a separate private in-memory database and cannot see the keeper's schema/data. If the caller supplies `Cache=Shared` and reuses the same data-source name for two Naravel connection definitions, those definitions can share the same named in-memory database, contrary to the class documentation that two connection names never share one.
- Affected contract: SQLite driver support for `Mode=Memory` connection strings and per-name isolation.
- User impact: a documented memory-mode configuration may appear to open successfully but lose schema/data between operations, or two logically separate configured connections may unintentionally share data.
- Required correction: normalize all in-memory configurations, not only the literal `:memory:` form: generate a unique internal data-source name per driver definition, force shared cache for the keeper and operation connections, and preserve the rest of the caller's relevant options. Alternatively reject unsupported private-cache memory forms explicitly rather than silently claiming they work.
- Regression tests required: named `Mode=Memory` with omitted Cache persists data across leases; two connection names configured with the same requested source are isolated; explicit shared-cache memory config is normalized to the same per-name isolation; file-backed connections retain their configured pooling/cache behavior.

### [DOC] SQL-002 — Documented SQLite native library patch version is stale

- Evidence status: CONFIRMED.
- Evidence: `Directory.Packages.props` pins `SQLitePCLRaw.lib.e_sqlite3` at 2.1.14 and the SQLite project comment identifies 2.1.14 as the patched version; `docs/en/database.md` still states that the repository pins 2.1.13.
- User impact: security/version documentation does not describe the dependency actually restored by the repository.
- Required correction: update the English guide to 2.1.14 and keep the central version and security rationale in sync. No dependency change is requested by this finding.

## Existing guarantees and coverage

- `:memory:` is rewritten to a unique internal name containing the connection name and a GUID, with `Mode=Memory` and `Cache=Shared`.
- A keeper connection is opened once and disposed with the driver; each query opens and disposes its own logical connection through the core lease.
- Open failure disposes the just-created connection; cancellation is passed to `OpenAsync`; sequential driver disposal prevents subsequent opens.
- File-backed database behavior is tested across leases, and the package pins the patched native SQLite library centrally.
- Tests do not currently cover the class-documented `Mode=Memory` path, shared-cache cross-name isolation for named memory sources, or concurrent disposal/open races.

## Test assessment

- Reviewed: `DatabaseManagerTests` in `tests/Naravel.Database.Tests`, SQLite driver source, central package versions, PDR-022 and English database documentation.
- Tests run for this review: NONE.
- Build/CI: NOT RUN.
- SQLite runtime reproduction: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Owner decision gate

No owner decision is required for the documented `Mode=Memory` guarantee. This audit does not authorize runtime code or dependency changes.

## Plain-language explanation of the findings

- **SQL-001 — Named in-memory databases may not behave like one persistent database:** An in-memory database exists only while its connections keep it alive. Naravel keeps one connection open so later operations can see the same database. That works for the special `:memory:` spelling. But with a named setting such as `Data Source=memdb;Mode=Memory`, the code does not force the later connections to use the same shared in-memory database. A later operation may therefore see an empty database instead of the tables created earlier. If two Naravel connection names use the same shared memory name, they may also see each other's data, even though the package says those names are isolated. We traced the connection-string handling in `SqliteDatabaseConnection` and compared it with the existing memory tests. We did not run a test for this named `Mode=Memory` case, so the report labels it as a source-confirmed behavior that still needs runtime reproduction.
- **SQL-002 — The documented SQLite library version is out of date:** The repository currently pins SQLite's native library package to version 2.1.14, but the English database guide says 2.1.13. This does not mean the dependency itself is wrong; it means the guide tells readers the wrong version. We compared `Directory.Packages.props`, the SQLite project comment and `docs/en/database.md`. No package version was changed.

**Verification reminder:** no SQLite runtime tests or build were run for this review. The report does not claim that the proposed correction has been implemented.
