# Review Report — Naravel.Database.EntityFrameworkCore

## Review metadata

- Package ID: `Naravel.Database.EntityFrameworkCore` (explicit `PackageId` in `src/Naravel.Database.EntityFrameworkCore/Naravel.Database.EntityFrameworkCore.csproj`).
- Source project: `src/Naravel.Database.EntityFrameworkCore/Naravel.Database.EntityFrameworkCore.csproj`.
- Source-review baseline: `main` at `44e957ca3db6d9f15665fc8fa070f70522c163c9`; rechecked against current `main` at `c6a7d58ffb84d885741998bb32d2082c54c4ab61`. EF adapter source, tests and project references are unchanged across that range.
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source and contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH OBSERVATION (source inspection only; runtime verification pending).
- Files inspected: project metadata and EF Relational reference; `DbContextQueryExtensions`; `EfCoreConnectionScope`; `EfCoreDatabase`; core DatabaseExecutor and QueryObserver integration; all `EfCoreInteropTests`; R25 stage; PDR-022; EN guide and NuGet README.
- Dependency boundary: the adapter depends on EF Core Relational and Naravel.Database; EF Core remains absent from the core Database package.
- Verification performed: provider identification and dialect mapping; EF connection reference-count ownership; current-transaction capture; borrowed-transaction non-ownership; observer forwarding path; execution strategy and test coverage review.
- Verification NOT RUN: `.NET restore/build/test`, live SQL Server/PostgreSQL/MySQL EF interop, full CI, mutation testing and independent review.

## Executive assessment

- The adapter uses EF Core's relational connection reference counting rather than taking ownership of or disposing the DbContext's physical connection.
- It captures `Database.CurrentTransaction` for each command, sets that transaction on the ADO.NET command through the core executor, and never commits or rolls back a transaction it did not start.
- The two extension entry points have deliberately different observability: `DbContext.Query` uses no Naravel observer; `IDatabase.Using(DbContext)` attempts to preserve the observer from the supplied database.
- Provider detection maps SQLite, SQL Server, Npgsql and MySQL provider names to the four core dialects. Unsupported/non-relational providers fail explicitly.
- One observability edge remains: `Using` preserves listeners/query logging only when the supplied `IDatabase` is the internal concrete `DatabaseService`; an otherwise valid decorator or custom `IDatabase` silently falls back to `QueryObserver.None`.
- No source-level transaction ownership or connection-lifetime defect was identified in the inspected adapter.

## Findings

### [OBS] EF-001 — Observer forwarding depends on the concrete internal DatabaseService type

- Evidence status: CONFIRMED BY SOURCE.
- Evidence: `DbContextQueryExtensions.Using` uses `database is DatabaseService service ? service.Observer : QueryObserver.None`. The public parameter type is `IDatabase`, but a decorator, proxy or alternate implementation that wraps the registered `DatabaseService` is not itself that concrete type, so its underlying listeners/query log are not forwarded.
- Affected contract: `IDatabase.Using(DbContext)` and its XML/documentation promise that queries reach the listeners and query log of the supplied database.
- User impact: adding a standard DI decorator around `IDatabase` can silently remove query observation for EF-bound builder queries while ordinary builder queries still pass through the decorator.
- Recommendation: either expose observer propagation through a narrow internal capability that decorators can preserve, add an explicit observer-bearing overload, or narrow the documentation to the concrete default registration. Do not make a public API change without owner approval.

## Existing guarantees and coverage

- Tests prove EF writes and builder writes share one SQLite connection and transaction for both commit and rollback.
- Tests prove the adapter does not commit a transaction it did not begin and refuses to start its own transaction on a borrowed database.
- `EfCoreConnectionScope` opens and closes through EF Core's reference-counted APIs; the physical connection remains EF-owned.
- The current transaction is read for each command, matching the documented requirement to begin the EF transaction before the first builder operation.
- R25 explicitly accepted SQLite interop tests; provider-name mapping and the generic relational connection APIs are source-reviewed for the other supported providers, but live cross-provider EF interop is not claimed by this review.
- The documentation correctly warns that builder writes bypass EF change tracking and that retrying execution strategies must wrap the whole manual transaction block.

## Test assessment

- Reviewed: `EfCoreInteropTests` (8 cases), R25, PDR-022 and EN documentation.
- Tests run for this review: NONE.
- Build/CI: NOT RUN.
- Live provider interop: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Owner decision gate

No owner decision is required to record the observer-forwarding limitation. Any public API redesign to make arbitrary decorators forward observers requires owner approval.
