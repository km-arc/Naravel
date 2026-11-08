# Review Report — Naravel.Foundation

## Review metadata

- Package ID: Naravel.Foundation (default PackageId follows the project/assembly name; no explicit PackageId element is set).
- Source project: src/Naravel.Foundation/Naravel.Foundation.csproj
- Original source-review baseline: `99ea264e11bf4bcb9e4ae87a0d1519452f8570ed` (main).
- Latest revalidation baseline: `c2b7e88b552088b7606286173fa6e51156163f1d` (main, PR #244 merged).
- Review date: 2026-11-07
- Reviewer mode: principal-architect source review; independent fresh-session review NOT RUN
- Status: REVIEWED WITH FINDINGS (source review; runtime verification pending)
- Files inspected: all files under src/Naravel.Foundation/; the Foundation test project and its test files; docs/en/foundation.md; PDR-002; shared build/package metadata.
- Verification performed: source and project-file review; public API baseline inspected; test cases and assertions inspected; package README mapping inspected.
- Verification NOT RUN: dotnet restore, dotnet build, dotnet test, trimmed/AOT publish, concurrency stress reproduction, and mutation testing. No executable repository checkout/SDK was available in this review pass.

## Executive assessment

- Overall risk: **Medium (P2)** pending the findings below.
- Architectural fit: Strong alignment with accepted PDR-002: Foundation supplies the runtime named-driver manager/registry and leaves service construction to Microsoft DI.
- Release readiness: No immediate release blocker established by source inspection alone; the three correctness risks should be reproduced with regression tests and reviewed before the next package release.
- Summary: The package has a coherent public contract, useful separation between the driver registry and manager, a mature ownership model, explicit sync/async factory APIs, configuration reload handling, and meaningful tests for concurrency, replacement, DI ownership, and disposal. The highest-risk gaps are in edge-case serialization of configuration fingerprints and two concurrency/cleanup paths not represented in current tests.

## Package and dependency map

- Public package contract: Manager<TDriver,TOptions>, IDriverRegistry<TDriver>, DriverRegistry<TDriver>, IManagerOptions, ManagerOptions, ManagerOptionsExtensions.GetStore, ServiceCollectionExtensions, and DriverNotRegisteredException.
- Package metadata: net10.0; PackageReadmeFile=README.md; package README is packed from docs/en/foundation.md; description and tags are present; shared build metadata supplies version, authors, license, and common NuGet properties.
- Package ID: no explicit PackageId is declared; SDK default is the project/assembly name Naravel.Foundation.
- Project references: none.
- Package references: Microsoft.Extensions.Configuration, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Options, and Microsoft.Extensions.Options.ConfigurationExtensions.
- Registration / DI: AddNaravelManager registers the ownership tracker, one singleton registry per driver contract, the manager, and the direct default-driver service; registration callbacks are applied when the registry is first resolved.
- Driver/provider map: Foundation is the shared abstraction and does not ship a concrete backend driver.
- Resource ownership: manager-created drivers are disposed by the manager; the direct default-driver instance is marked as container-owned and skipped by manager disposal. Reference identity prevents double disposal for one object registered under multiple names.

## Second-pass architecture revalidation — 2026-11-08

- Latest revalidation baseline: `main` at `c0aa29b4743760d879c0dc1041f5669e92f5b592` (PR #246, `DriverAsync does not start over for a caller that has cancelled`, merged).
- The audit branch contains this main commit: GitHub compare reports main as the branch's merge base (`behind = 0`); the Foundation source, tests and guide blobs are identical on main and the audit branch.
- **FND-001 — VERIFIED CLOSED.** The fingerprint now length-prefixes tagged store/key/value components, preserves null separately from empty, and includes a store's own scalar value. Four collision/single-value regressions and an identical-reload guard were added (D-82).
- **FND-002 — narrowed, still OPEN.** The sync-to-async replacement race after `IsAsyncOnly` is fixed by creating through `IDriverRegistry.Create` and refusing an async factory selected at creation time. A narrower race remains: while `DriverAsync` is awaiting an old async factory, another thread replaces it with a sync factory; before the manager receives the registry's `Registered` event, `Driver` can observe the new sync registration but join the still-running old async entry and block. This is reproduced/documented in `docs/en/foundation.md` and D-83; eliminating it requires an atomic registry API that returns factory and factory kind together.
- **FND-003 — VERIFIED CLOSED.** Registry detach, options-subscription disposal and lifetime-token cancellation are guarded; failures are collected and reported with driver-disposal failures after every owned driver is disposed. Five regression tests were added (D-84).
- **D-85 — VERIFIED CLOSED in main.** PR #246 added a cancellation check before `DriverAsync` retries after a synchronous creator refused an entry. Its regression test `An_async_caller_that_cancelled_does_not_start_a_creation_when_the_refusal_ends_its_wait` proves a cancelled waiter receives `OperationCanceledException`, does not start a new async creation, and a subsequent live caller can create the driver.
- **FND-004 — remains OPEN and vetoed as AV-006.** Late-created driver disposal still goes through `DisposeQuietlyAsync`, which catches and discards every exception.
- PR #246's CI workflow run `42478727741` completed successfully. Windows, macOS, Ubuntu, Provider integration, Postgres, MySQL, SQL Server, service-image pins and `ci-ok` all completed with success; NuGet publish was skipped as expected for a non-tag event.
- Verification performed here: source/blob comparison, PR/CI metadata inspection and control-flow re-review. The repository's local .NET tests were NOT RUN by this audit session; the PR commit description reports its offline harness, but that is not substituted for a local full test run here.
- The .NET tests were NOT RUN locally by this audit session; PR #246 CI is the verification evidence for FND-001, FND-003 and D-85.
- No runtime code, tests, dependencies or public API were changed by this audit branch.

## Findings

### [P2 — VERIFIED CLOSED] FND-001 — Configuration fingerprint was not an unambiguous encoding

- Prior status: CONFIRMED BY SOURCE and reproduction described in upstream PR #244 / D-82.
- Current state: fixed in main at `c0aa29b4743760d879c0dc1041f5669e92f5b592`; keep verification follow-up until all required CI jobs are conclusively complete.
- Evidence: current `src/Naravel.Foundation/Manager.cs:554-592` writes tagged, length-prefixed components, includes the store's own value and differentiates null from empty. The old ambiguous `key=value;` encoding is removed.
- Regression tests added upstream: separator movement between a value and the next key; equals sign moved between key and value; missing versus empty value; scalar store value change; identical separator-bearing reload does not rebuild.
- Verification: PR #246 workflow run `42478727741` completed successfully on all listed build/test, provider and database jobs.
- Required follow-up: none. No further implementation is requested by this finding.
- Public API / PDR / release impact: implementation-only; no public API change.

### [P2] FND-002 — Driver can still join an in-flight asynchronous creation after factory replacement

- Remediation state: OPEN — NARROWED BY MAIN FIX; OWNER DECISION FOR FULL CLOSURE.
- Evidence status: CONFIRMED BY UPSTREAM TARGETED REPRODUCTION / DOCUMENTED LIMITATION; independent reproduction NOT RUN in this pass.
- Evidence: current `src/Naravel.Foundation/Manager.cs:108-137` checks the registry's current async-only flag and then calls `GetOrStart`. If `DriverAsync` has already created an entry from an async factory, another thread replaces that registration with a sync factory, and the registry's `Registered` event has not yet reached the manager, the existing incomplete entry can still be returned and synchronously awaited. The limitation is explicitly documented in `docs/en/foundation.md` and recorded in roadmap D-83.
- Fixed portion: the previous sync-to-async race (sync factory replaced with async after the check) is mitigated: creation for a sync caller now uses `IDriverRegistry.Create`, which reads the selected factory once and rejects an async factory; an async waiter that shared this refusal starts over.
- Affected contract: `Manager.Driver` guarantee that it does not block on an asynchronous factory.
- User impact: a synchronous caller may still block while an already-started asynchronous factory finishes, but only in the narrow replacement/event-delivery window described above.
- Required correction: close the atomicity gap between registry replacement and manager cache invalidation. The upstream D-83 record says the clean boundary requires `IDriverRegistry` to expose the factory and its kind in one atomic read; that is a public API change and remains an owner decision. Do not claim the guarantee is absolute until this is resolved.
- Regression tests required: a deterministic registry must hold the `Registered` notification after replacing an in-flight async factory with a sync factory; call `Driver` in that interval and assert it never waits on the old async creation. Also preserve the already-added sync-to-async replacement tests.
- Public API / PDR / release impact: likely public `IDriverRegistry` API change; owner decision required before implementation.

### [P2 — VERIFIED CLOSED] FND-003 — A throwing lifetime-cancellation callback could abort manager cleanup

- Prior status: CONFIRMED BY SOURCE and reproduced in upstream PR #244 / D-84.
- Current state: fixed in main at `c0aa29b4743760d879c0dc1041f5669e92f5b592`; keep verification follow-up until all required CI jobs are conclusively complete.
- Evidence: current `src/Naravel.Foundation/Manager.cs:445-522` guards registry unsubscription, options-subscription disposal and lifetime cancellation independently. Cancellation callback failures are collected, all owned drivers are still disposed, the token source is finalized, and one `AggregateException` reports all failures.
- Regression tests added upstream: throwing token callback during `Dispose` and `DisposeAsync`; aggregation with a driver-disposal failure; a second Dispose does not double-dispose; registry-detach and options-subscription disposal failures.
- Verification: PR #244 merged. Workflow run `42477770649` reports aggregate success and `ci-ok` success. The Windows job still appears `in_progress` in the jobs endpoint, so this audit does not independently attest every matrix leg.
- Required follow-up: re-check the final Windows job result before fully closing the verification record. No further implementation is requested by this finding.
- Public API / PDR / release impact: no public API change; the AggregateException message changed and is documented.

### [P2] FND-004 — Disposal failures after late creation are silently swallowed

- Remediation state: OPEN.
- Architecture disposition: VETO AV-006 — late resource cleanup failures must remain observable.
- Evidence status: CONFIRMED BY SOURCE.
- Evidence: src/Naravel.Foundation/Manager.cs:390, 526-544. RunCreationAsync disposes a driver that completes after manager disposal, and DisposeQuietlyAsync catches every exception without logging, returning it, or recording a diagnostic.
- Affected contract: manager lifetime, late driver creation, and resource ownership.
- User impact: a late-created connection pool, socket pool, or other disposable driver may fail to release its resources, while the host receives no signal. This hides cleanup failures during shutdown/reload races and prevents operators from distinguishing clean shutdown from leaked resources.
- Why existing tests do not catch it: current disposal tests verify normal disposal and that cleanup does not produce an unobserved background exception; they do not verify that a failure from disposing a late-created driver is observable.
- Required correction: preserve the no-unobserved-background-exception property, but report the failure through a manager-owned diagnostic/logging mechanism. The failure must be visible to the host/operator without preventing disposal of other owned drivers.
- Regression tests required: make a pending factory complete after manager disposal and return a driver whose Dispose/DisposeAsync throws; verify exactly-once disposal and an observable structured diagnostic; verify no unobserved task exception.
- Public API / PDR / release impact: a diagnostic mechanism may be internal or additive; choose it without forcing application code to depend on private manager internals.

## Test assessment

- Existing tests inspected: manager resolution/defaults, concurrent one-time creation, sync/async factories, cancellation of individual waiters, runtime extension/replacement, options reload, forgetting, manager disposal, DI ownership, duplicate driver-instance disposal, registry concurrency, and provider-aware registration callbacks.
- Missing regression coverage: fingerprint encoding collisions; sync-to-async replacement race across the IsAsyncOnly / CreateAsync boundary; throwing lifetime cancellation callback during disposal.
- Provider integration tests: not applicable to Foundation itself; provider-specific behavior belongs to each package review.
- Tests actually run: none in this remote source-inspection pass.
- Tests NOT RUN: Foundation test project and full solution tests; no runtime execution was performed.
- Mutation testing: NOT RUN; no mutation-testing tool/run was available in this pass.
- Independent review: fresh-session/independent reviewer NOT RUN; concurrency findings should receive that review before remediation is accepted.

## Documentation and packaging assessment

- Source-to-doc mismatches established: none yet.
- Code sample validity: quickstart API names and registration calls match the inspected source signatures; sample was not compiled.
- README pack metadata and actual file: project sets PackageReadmeFile=README.md and packs docs/en/foundation.md to that path.
- Dependencies: only Microsoft.Extensions packages are referenced directly; no unexpected Naravel project reference.
- Compatibility: source targets net10.0; the AOT-safe registration path is documented separately from the reflection-based configuration overload. The trim/AOT publish was not executed.
- Public API baseline: PublicAPI.Shipped.txt contains the reviewed API surface and PublicAPI.Unshipped.txt contains no pending members.

## Risk summary and next action

- P0: none established.
- P1: none established from this pass.
- P2: FND-002 and FND-004 remain OPEN; FND-004 is Architecture Veto AV-006. FND-001 and FND-003 are VERIFIED CLOSED in main; D-85 is also fixed and covered by PR #246 CI.
- P3: none established.
- OBS: none.
- DOC: none established.
- Recommended next task: obtain the owner's decision on the remaining FND-002 registry API race and include AV-006 in Foundation lifecycle remediation. No additional verification follow-up is required for FND-001/FND-003 after the fully green PR #246 run. The late-disposal failure must be observable while preserving exactly-once cleanup and avoiding unobserved background exceptions. No remediation has been made in this audit branch.
