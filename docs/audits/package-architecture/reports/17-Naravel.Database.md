# Review Report — Naravel.Database

## Review metadata

- Package ID: `Naravel.Database` (explicit `PackageId` in `src/Naravel.Database/Naravel.Database.csproj`).
- Source project: `src/Naravel.Database/Naravel.Database.csproj`.
- Source-review baseline: `main` at `44e957ca3db6d9f15665fc8fa070f70522c163c9`; rechecked against current `main` at `c6a7d58ffb84d885741998bb32d2082c54c4ab61`. Database core source and tests are unchanged across that range.
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source and contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (5 P2 architecture vetoes, 3 OBS; source inspection only).
- Files inspected: project metadata and package references; database service/manager/connection scopes; transaction and ambient transaction lifecycle; executor and query observer; value conversion and database fake; identifier grammar; SQL compiler for select/insert/update/delete/upsert/CTE/JSON; query builder operations; PDR-022; transaction, query, hostile-input, parameter-limit, dialect-contract, fake and observability tests; English database guide and NuGet README.
- Dependency boundary: core package owns SQL compilation and ADO.NET execution, references Dapper only for complex row mapping, and has no provider-specific driver dependency.
- Verification performed: source-level transaction/lease lifecycle tracing; SQL marker and binding flow; parameter-limit coverage across compiler paths; JSON mutation compilation; fake-vs-real conversion/cancellation comparison; dialect grammar and identifier safety review.
- Verification NOT RUN: `.NET restore/build/test`, SQLite runtime suite, MySQL/PostgreSQL/SQL Server server suites, full CI, mutation testing and independent review.

## Executive assessment

- The package has a strong provider-neutral boundary: pure dialect grammars compile SQL, while an ADO.NET lease owns connection/transaction lifetime and Dapper is limited to complex row mapping.
- Identifier parsing is centralized, restrictive and covered by hostile-input tests across all four dialects. Values are parameterized; raw SQL is explicit; unsupported dialect features fail at compile time.
- The transaction design supports nested savepoints, ambient participation by the injected database, retry of recognized deadlocks, and borrowed EF Core connections. The public documentation explicitly limits concurrent use of one transaction from multiple flows.
- Five source-level defects/architecture hazards remain: JSON-path writes of JSON null fail on three dialects; insert-with-generated-key skips the final parameter-limit check; raw SQL can collide with the builder's reserved `@pN` parameter names; detached tasks can retain a stale ambient transaction scope after the callback returns; and transaction disposal can skip disposing the owned connection lease if provider transaction disposal throws.
- Three additional observations concern same-connection aliases in transaction-bound database views, rollback-error masking in multi-statement operations, and behavioral fidelity of the test fake.
- No runtime code, tests, public API, dependencies or roadmap stage were changed by this review.

## Findings

### [P2] DBQ-001 — JSON-path updates cannot write JSON null on SQLite, MySQL or PostgreSQL

- Remediation state: OPEN.
- Architecture disposition: VETO AV-014 — supported JSON-path update values must include JSON null on dialects whose documented implementation can represent it; unsupported dialect behavior must fail explicitly.
- Evidence status: CONFIRMED BY SOURCE; regression reproduction NOT RUN.
- Evidence: `Query.UpdateAsync` documents that `null` writes SQL `NULL` for ordinary columns and only calls out SQL Server as refusing a JSON-path null. SQLite, MySQL and PostgreSQL `JsonSet` implementations call `JsonText(value!)`. `Grammar.JsonText(object value)` has no null case and its fallback dereferences `value.GetType()`; an explicit `null` therefore throws before SQL compilation. SQL Server intentionally throws `NotSupportedException` because `json_modify` interprets null as key deletion.
- Affected contract: `Table(...).Where(...).UpdateAsync({ ["data->path"] = null })`.
- User impact: callers cannot set a JSON member to JSON null on three of the four supported dialects, despite the documented behavior distinguishing this from SQL NULL of the whole column.
- Required correction: encode JSON null as the JSON text `null` for SQLite/MySQL/PostgreSQL; preserve SQL Server's explicit `NotSupportedException` unless the owner approves a distinct delete-key API.
- Regression tests required: compile tests asserting the JSON-null binding is the string `"null"` for SQLite/MySQL/PostgreSQL; a SQL Server test retaining the explicit unsupported exception; real SQLite execution proving the member is JSON null and the containing column is not replaced with SQL NULL.

### [P2] DBQ-002 — InsertGetId bypasses the actual parameter-count limit for Raw values

- Remediation state: OPEN.
- Architecture disposition: VETO AV-015 — every compiled statement must enforce its dialect's maximum against the final expanded binding count.
- Evidence status: CONFIRMED BY SOURCE; oversized-statement execution NOT RUN.
- Evidence: `Raw` is explicitly supported as an insert value and `CompileContext.Add(Raw)` expands all of its bindings. `Grammar.CompileInsert` checks `context.Bindings.Count` after expansion, but `Grammar.CompileInsertGetId` checks only `columns.Count` before expanding the row and returns the compiled query without checking the final binding count.
- Affected contract: `InsertGetIdAsync<T>` with one or more `Raw` values containing multiple bindings.
- User impact: a generated-key insert can exceed SQL Server's or another dialect's parameter limit even though other insert paths reject the same statement at compile time; failure is deferred to the provider and the error is less deterministic.
- Required correction: check the final `context.Bindings.Count` after `AppendInsertValues`, using the same dialect-specific limit and diagnostic convention as the other compiler paths.
- Regression tests required: for each dialect, a `Raw` value whose expanded bindings equal the limit compiles and limit+1 fails; retain ordinary InsertGetId behavior and MySQL same-connection identity retrieval.

### [P2] DBQ-003 — Raw IDatabase SQL can collide with reserved generated parameter markers

- Remediation state: OPEN.
- Architecture disposition: VETO AV-016 — raw SQL entry points must reject `@p` followed by digits, just as Query's `*Raw` methods and `Raw` already do.
- Evidence status: CONFIRMED BY SOURCE; provider execution NOT RUN.
- Evidence: `Query.CheckRaw` and the `Raw` constructor call `Grammar.ContainsParameterMarker` and reject `@pN`. In contrast, all direct `IDatabase.SelectAsync`, `ScalarAsync`, `StatementAsync` and `AffectingStatementAsync` paths call `DatabaseExecutor.Prepare`, which replaces each `?` with `@pN` but never checks whether the original SQL already contains that reserved marker.
- Affected contract: direct raw SQL methods when SQL contains an existing `@pN` token as well as one or more `?` placeholders.
- User impact: generated parameter names can alias an existing SQL token. For example, `select @p0, ?` with one binding becomes `select @p0, @p0`; both expressions receive the same parameter instead of preserving the caller's SQL meaning. This also makes direct raw SQL behave differently from Query raw methods.
- Required correction: reject reserved markers in `DatabaseExecutor.Prepare` before replacement, with the same clear diagnostic used by Query raw methods. Do not silently rename user SQL.
- Regression tests required: direct Select/Scalar/Statement paths reject `@p0` plus `?`; a plain SQL variable name such as `@userId` remains permitted; Query raw and direct raw SQL have the same reserved-marker rule.

### [P2] DBQ-004 — Detached tasks can retain an ambient transaction scope after callback completion

- Remediation state: OPEN — OWNER DECISION AD-022.
- Architecture disposition: VETO AV-017 — an ambient transaction scope must not remain usable in an execution context after its owning callback has completed and its transaction has committed/rolled back.
- Evidence status: CONFIRMED BY .NET ExecutionContext/AsyncLocal SEMANTICS AND SOURCE; deterministic reproduction NOT RUN.
- Evidence: `AmbientTransaction` stores the scope in a static `AsyncLocal`. `TransactionAsync` pushes the node before invoking the callback and restores the parent in the callback's own execution context. A child task created inside the callback captures its own ExecutionContext and retains the node when the parent restores its value. `LeasedTransactionScope.AcquireAsync` has no active/completed-state guard and continues returning borrowed leases over the connection and transaction after the owner disposes them.
- Affected contract: injected `IDatabase` participation in `TransactionAsync` callbacks when a callback starts work that is not awaited before it returns.
- User impact: a detached task may attempt to execute against a committed/disposed connection or transaction, producing late provider errors and making background work appear to participate in a transaction that has already ended.
- Required correction: invalidate the leased scope when commit, rollback or disposal begins; every later `AcquireAsync` through a captured ambient context must fail with a clear Naravel lifecycle exception before touching the disposed provider connection. Document that work intended to participate in the transaction must be awaited before the callback returns; do not promise that parent-context restoration clears child ExecutionContexts.
- Regression tests required: create a child task inside a callback, let the callback commit, then release the child and verify it is rejected by the scope guard; verify an awaited child task still joins the transaction; verify nested savepoints and retry attempts do not invalidate the active parent scope.
- Owner decision required: AD-022 asks whether the framework should enforce post-completion invalidation (recommended) in addition to documenting the no-detached-work rule, or whether the owner explicitly wants a weaker documentation-only contract.

### [P2] DBQ-005 — Transaction disposal can leak its owned connection lease

- Remediation state: OPEN.
- Architecture disposition: VETO AV-018 — disposal of a transaction must release its owned connection lease even when provider transaction disposal fails.
- Evidence status: CONFIRMED BY SOURCE; fault-injection reproduction NOT RUN.
- Evidence: `DatabaseTransaction.DisposeAsync` attempts rollback when incomplete, then awaits `transaction.DisposeAsync()`, then awaits `lease.DisposeAsync()` sequentially. If provider transaction disposal throws, control never reaches lease disposal. The lease owns the pooled connection and is the resource that returns/closes it.
- Affected contract: `IDatabaseTransaction.DisposeAsync` on provider disposal failure, including commit/rollback failure paths.
- User impact: a provider exception during transaction disposal can leave the connection lease undisposed and hold a pool slot/resource until external cleanup.
- Required correction: put lease disposal in a guaranteed `finally` path; preserve the primary transaction-disposal exception if lease disposal also fails, while retaining both failures for diagnosis.
- Regression tests required: fake lease and transaction where transaction disposal throws; assert lease disposal is still attempted exactly once; test dual transaction/lease disposal failures and deterministic exception preservation.

### [OBS] DBQ-006 — Transaction-bound Connection rejects aliases of the same default connection

- Evidence status: CONFIRMED BY SOURCE.
- Evidence: `DatabaseService.Connection` compares the requested name and stored `connectionName` as raw ordinal strings whenever the scope is a `LeasedTransactionScope`. The database contract also defines `null` as the default connection. If the default connection is named `main`, a transaction opened as `db.Connection("main")` rejects `tx.Database.Connection(null)`, and a transaction opened as `db` rejects `tx.Database.Connection("main")`, despite both names resolving to the same connection.
- User impact: harmless aliases of the same connection are rejected inside a transaction, while the error message suggests only a true connection switch is prohibited.
- Recommendation: compare a captured, normalized connection identity rather than the raw nullable alias; add both default-to-explicit and explicit-to-default tests. Capture the resolved identity at transaction start so a configuration reload cannot change the meaning of `null` mid-transaction.

### [OBS] DBQ-007 — Multi-statement rollback failure masks the original statement failure

- Evidence status: CONFIRMED BY SOURCE.
- Evidence: `DatabaseExecutor.NonQueryManyAsync` catches a statement/commit exception and awaits `own.RollbackAsync(CancellationToken.None)` before rethrowing. If rollback also throws, the rollback exception escapes and replaces the original operation exception. The savepoint rollback path explicitly suppresses a secondary rollback exception to preserve the original, but this path does not.
- User impact: the primary SQL error can be lost precisely when the provider/connection is unhealthy.
- Recommendation: preserve the original exception and retain the rollback failure as secondary diagnostic information; add a fault-injected transaction test.

### [OBS] DBQ-008 — DatabaseFake conversion and cancellation do not fully match the real executor

- Evidence status: CONFIRMED BY SOURCE.
- Evidence: `DatabaseFake.Convert<T>` uses `Convert.ChangeType`, whereas production reads use `ValueConverter.Convert<T>` (which handles Guid, DateOnly, TimeOnly, enums and provider-specific values). The fake's async stream yields a pre-materialized list without checking cancellation between items, and `FakeTransaction.CommitAsync/RollBackAsync` ignore their cancellation tokens.
- User impact: tests using the fake can fail to model real scalar conversions and cancellation behavior, allowing application tests to pass under semantics that differ from the real provider path.
- Recommendation: reuse the production value converter for canned scalar results and honor cancellation during fake streaming and transaction completion. Add Guid/DateOnly/enum conversion and mid-stream/commit/rollback cancellation tests.

## Existing guarantees and coverage

- PDR-022 A3 identifier grammar and hostile-input coverage are strong; all four dialects are exercised by pure compilation tests.
- Parameter-limit tests cover just-below/at/above limits for SELECT and the compiler checks final binding counts for SELECT, UPDATE, DELETE, batch INSERT, INSERT USING and UPSERT. The single-row INSERT GET ID path is the uncovered exception.
- Transaction tests cover commit, rollback, nested savepoints, deadlock retries, ambient injected-database participation and transaction lifetime in the parent flow. They do not cover detached ExecutionContext retention or provider disposal failures.
- Query tests cover JSON read/search and non-null JSON-path writes; SQL Server explicitly tests that JSON-path null is unsupported. The other three dialects lack JSON-null update tests.
- Fake tests cover pre-cancelled operations and simple numeric scalar conversion; they do not cover conversion parity for Guid/DateOnly/enums or cancellation after stream enumeration begins.
- No provider-specific driver implementation was changed or re-reviewed in this package report.

## Test assessment

- Reviewed: `TransactionTests`, `ServerTransactionTests`, `ContractTests`, `IdentifierSafetyTests`, `ParameterLimitTests`, `DatabaseFakeTests`, `QueryUpdateTests`, `QueryRawTests`, `QueryReturningTests`, `QueryObserver`, `DeadlockDetector`, PDR-022 and EN documentation.
- Tests run for this review: NONE.
- Build/CI: NOT RUN.
- SQLite execution: NOT RUN.
- MySQL/PostgreSQL/SQL Server services: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Owner decision gate

See [OWNER-DECISIONS.md](../OWNER-DECISIONS.md), AD-022. No runtime fix, public API change, dependency change or roadmap-stage change is authorized by this source review.

## Plain-language explanation of the findings

The following explains the findings without requiring the reader to know the database internals. The source evidence above remains the precise technical record.

- **DBQ-001 — Writing JSON null:** A JSON column can contain an object such as `{"name":"Ada"}`. The query builder promises that setting `data->name` to `null` writes the JSON value `null` inside that object. On SQLite, MySQL and PostgreSQL, the code tries to inspect the value before turning it into JSON text. Because the value is actually null, that code throws instead. In simple terms: the user asks to store “no value” inside the JSON document, but the query fails before it reaches the database. We saw this in `Query.UpdateAsync`, the three dialect `JsonSet` methods and `Grammar.JsonText`; we did not run a reproducing test. SQL Server is different: its current implementation treats JSON null as deleting the property, so it deliberately rejects this request.
- **DBQ-002 — Generated-ID insert can exceed the database limit:** A normal insert checks how many values will be sent to the database. An insert that also asks for the new row's ID does not perform that final check. A `Raw` expression can contain several values even though it looks like one insert field. So a caller can build a statement with more parameters than the database allows; the database then rejects it later instead of Naravel rejecting it early and clearly. We traced `CompileInsert`, `CompileInsertGetId` and `CompileContext.Add(Raw)`. We did not send an oversized statement to a live database.
- **DBQ-003 — Raw SQL parameter names can clash:** Naravel replaces each `?` in direct SQL with names such as `@p0`. If the SQL already contains `@p0`, both places can end up using the same value. For example, `select @p0, ?` can become `select @p0, @p0`; the second value is no longer separate. The query-builder raw methods already block this reserved name, but the direct `IDatabase` methods do not. We traced `DatabaseExecutor.Prepare` and compared it with `Query.CheckRaw`; we did not execute this against a provider.
- **DBQ-004 — Work started in the background can outlive its transaction:** If code starts a task inside a transaction callback but does not wait for it, that task can keep a copy of the transaction context. The callback can then finish and close the transaction while the background task still believes it is inside it. The background task may try to use a connection that has already been closed. We saw that the transaction context is stored in `AsyncLocal` and that the leased scope has no “already finished” check. This conclusion follows from .NET task-context behavior and the source path; a deterministic reproducing test has not been run. Owner choice AD-022 is still required.
- **DBQ-005 — A cleanup error can leave a connection checked out:** When a transaction is disposed, Naravel should also return its connection to the connection pool. The current code disposes the database transaction first and the connection lease second. If the first cleanup step throws, the second step is skipped. That can leave a connection unavailable for other work. We traced `DatabaseTransaction.DisposeAsync`; we have not run a fault-injection test that makes transaction disposal throw.
- **DBQ-006 — Two names for the same connection are treated as different:** A configured default connection may be called `main`. Inside a transaction, asking for the default connection and asking for `main` should refer to the same database. The current comparison checks the literal names, so it can reject one spelling even though both resolve to the same connection. This is a usability/consistency observation, not a demonstrated data corruption issue. We traced the name comparison in `DatabaseService.Connection`; no runtime test was run.
- **DBQ-007 — A rollback error can hide the original SQL error:** Suppose a multi-statement operation fails because one SQL statement is invalid. Naravel then tries to roll the transaction back. If rollback also fails, the rollback error can replace the original SQL error. The person debugging the incident may see only the cleanup failure and lose the reason the operation first failed. We traced the catch/rollback path in `DatabaseExecutor.NonQueryManyAsync`; fault injection has not been run.
- **DBQ-008 — The test fake can behave differently from a real database:** The fake database used in unit tests converts returned values using simpler rules than the real database code. It also does not always notice cancellation after a stream has started. A test can therefore pass with the fake even though the real provider behaves differently for values such as GUIDs, dates or enums, or when a caller cancels midway. We compared `DatabaseFake.Convert<T>` and its async stream/transaction methods with `ValueConverter.Convert<T>` and the real executor. The suggested parity tests have not been run.

**Verification reminder:** these are source-review findings, not claims that the suggested regression tests have passed. No Database runtime code was changed in this audit.
