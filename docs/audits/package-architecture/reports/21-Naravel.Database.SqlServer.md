# Review Report — Naravel.Database.SqlServer

## Review metadata

- Package ID: `Naravel.Database.SqlServer` (explicit `PackageId` in `src/Naravel.Database.SqlServer/Naravel.Database.SqlServer.csproj`).
- Source project: `src/Naravel.Database.SqlServer/Naravel.Database.SqlServer.csproj`.
- Source-review baseline: `main` at `44e957ca3db6d9f15665fc8fa070f70522c163c9`; rechecked against current `main` at `c6a7d58ffb84d885741998bb32d2082c54c4ab61`. SQL Server driver source and package metadata are unchanged across that range.
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source and contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED — NO SOURCE-LEVEL FINDINGS (runtime verification pending).
- Files inspected: project metadata and central SqlClient/Pkcs pins; `SqlServerDatabaseConnection`; DI registration and connection-string handling; shared dialect contract fixtures; SQL Server server transaction integration tests; PDR-022; SQL Server NuGet README and English database guide.
- Dependency boundary: optional provider package references `Naravel.Database`, `Microsoft.Data.SqlClient` and the explicitly pinned Pkcs dependency; SqlClient types do not leak into the core API.
- Verification performed: async open/cancellation/failure cleanup trace; provider pooling and connection ownership review; dependency pin rationale; SQL Server contract and server test mapping.
- Verification NOT RUN: `.NET restore/build/test`, live SQL Server 2022 provider tests, full CI, mutation testing and independent review.

## Executive assessment

- The driver is stateless apart from its immutable name and connection string; it retains no open connection and delegates pooling to Microsoft.Data.SqlClient.
- Every lease creates a logical `SqlConnection`, opens it asynchronously with the caller's cancellation token, disposes it on open failure, and returns it to the provider pool when disposed.
- Configuration registration is limited to `sqlserver` sections and requires a connection string. TLS guidance distinguishes local/test `TrustServerCertificate=true` from production.
- The package explicitly pins `System.Security.Cryptography.Pkcs` to the patched central version required by SqlClient's older target assets; the package boundary remains isolated from the core database API.
- Shared dialect and server transaction tests cover SQL Server's OFFSET/ORDER BY, parameter limit, OUTPUT, locking, savepoint and forced-deadlock behavior.
- No source-level architecture or correctness finding was identified in this provider adapter.

## Existing guarantees and coverage

- Connection opening is asynchronous and cancellation-aware; failed opens dispose the connection object before propagating the error.
- Provider pooling is not reimplemented by Naravel; leases own only the logical connection wrapper.
- SQL Server-specific parameter and offset constraints are enforced by the core grammar before provider execution.
- Server tests are environment-gated and carry the `Dialect=SqlServer` trait used by the CI database matrix; a skipped local server test is NOT RUN, not a pass.
- The README warns against trusting self-signed certificates in production and keeps credentials in the connection string configuration.

## Test assessment

- Reviewed: SQL Server driver source, central package pins, `ContractTests`, `ServerTransactionTests`, PDR-022 and SQL Server package documentation.
- Tests run for this review: NONE.
- Build/CI: NOT RUN.
- Live SQL Server service: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Owner decision gate

No owner decision is required for this provider adapter based on source inspection. Do not interpret this review as runtime verification or as closure of any roadmap stage.

## Plain-language explanation of this review

No source-level defect was found in the SQL Server connection adapter. In everyday terms, it creates a connection for each operation, opens it asynchronously, passes cancellation through, and returns it to the SQL Server library's connection pool when the operation finishes. It does not build a second connection pool of its own. The package also keeps SQL Server-specific types out of the shared Database core.

We checked the source, package references and the tests intended for SQL Server. We did **not** build the project or run those tests against a live SQL Server. The statement “no source-level findings” must not be read as “all SQL Server runtime behavior has been proven.”
