# Review Report — Naravel.Foundation

## Review metadata

- Package ID: Naravel.Foundation (default PackageId follows the project/assembly name; no explicit PackageId element is set).
- Source project: src/Naravel.Foundation/Naravel.Foundation.csproj
- Baseline commit: e57d6e017c00e0a0d1c83fb7a767ba640ff4e20a (main)
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

## Findings

### [P2] Configuration fingerprint is not an unambiguous encoding

- Status: CONFIRMED BY SOURCE-LEVEL ANALYSIS; runtime reproduction NOT RUN.
- Evidence: src/Naravel.Foundation/Manager.cs:489-507, especially line 503; OnOptionsChanged compares the resulting string at lines 373-384.
- Affected contract: hot reload of named store configuration.
- User impact: a changed store configuration can produce the same fingerprint as the previous configuration, causing OnOptionsChanged to return without retiring the old driver. The application can continue using a driver configured with stale endpoint, credentials, or other settings after a reload.
- Trigger / failure scenario: the fingerprint concatenates raw keys and values with '=' and ';' and does not escape or length-prefix them. For one store, these two configurations serialize identically:
  - ConnectionString = "Host=db;Port=5432"
  - ConnectionString = "Host=db" and Port = "5432"
  Both can produce ConnectionString=Host=db;Port=5432;. Null and empty values are also represented identically by StringBuilder.Append.
- Why existing tests do not catch it: reload tests cover a simple host value changing from one string to another, an identical reload, an unrelated section, and a changed default. They do not exercise separator-bearing values, alternate key/value partitions, or null-versus-empty values.
- Recommended correction: encode each sorted store/key/value as an unambiguous tuple (for example, length-prefixed UTF-8 components or a canonical escaping/serialization scheme); preserve null separately from empty; add collision regression tests before changing implementation.
- Regression tests required: prove that the two configurations above have different fingerprints and that a reload retires/recreates the cached driver; add null-to-empty and empty-to-null coverage if those values are supported by the configuration contract.
- Public API / PDR / release impact: implementation-only unless the team chooses to change documented reload semantics.

### [P2] Sync Driver can race with runtime replacement and block on an async-only factory

- Status: CONFIRMED BY SOURCE-LEVEL CONTROL-FLOW ANALYSIS; deterministic reproduction NOT RUN.
- Evidence: src/Naravel.Foundation/Manager.cs:102-130; src/Naravel.Foundation/DriverRegistry.cs:23-45 and 62-71.
- Affected contract: Manager.Driver guarantee that it rejects an asynchronous-only factory rather than waiting on it.
- User impact: a synchronous call can block on an asynchronous operation (including network I/O) and may contribute to thread-pool starvation.
- Trigger / failure scenario:
  1. A name is initially registered with a synchronous factory.
  2. Driver(name) passes IsAsyncOnly(name) at line 118.
  3. Concurrently, another thread calls Extend / Register and replaces that factory with an async-only factory; the registry entry is replaced and the Registered event evicts the manager cache.
  4. The original Driver call proceeds to GetOrStart; RunCreationAsync re-reads the registry through CreateAsync and invokes the new async factory.
  5. Driver then synchronously waits at line 130.
- Why existing tests do not catch it: tests cover async-only registration before the sync call and a completed async driver later being returned synchronously; they do not force a sync-to-async replacement between the pre-check and factory lookup.
- Recommended correction: make the sync/async decision and factory selection atomic for one creation attempt (for example, use an immutable registry entry/snapshot passed into the cache entry); do not rely on a separate IsAsyncOnly check followed by a later registry lookup.
- Regression tests required: use barriers to pause a sync call after the check, replace the registration with an async-only factory, then resume and assert the sync call never waits for that async factory. Also test the reverse replacement direction.
- Public API / PDR / release impact: implementation-only if the public contract remains unchanged; review concurrency semantics before remediation.

### [P2] A throwing lifetime-cancellation callback can abort manager cleanup

- Status: CONFIRMED BY SOURCE-LEVEL CONTROL-FLOW ANALYSIS; runtime reproduction NOT RUN.
- Evidence: src/Naravel.Foundation/Manager.cs:410-449, especially line 448; factory tokens are supplied by RunCreationAsync at lines 319-320.
- Affected contract: IDisposable.Dispose and IAsyncDisposable.DisposeAsync cleanup.
- User impact: user-provided async factories receive the manager lifetime token. If one registered cancellation callback throws, CancellationTokenSource.Cancel() can throw an AggregateException. That exception escapes BeginDispose before the previously collected owned drivers are disposed and before FinishDispose disposes the token source. Since _disposed has already been set, a subsequent Dispose call returns early and cannot complete the skipped cleanup.
- Trigger / failure scenario: an asynchronous factory registers a callback on the supplied token that throws; manager disposal begins while that factory is still pending.
- Why existing tests do not catch it: current tests verify normal pending-creation disposal, disposal exceptions from completed drivers, and idempotency; they do not make a factory cancellation callback throw during disposal.
- Recommended correction: make cancellation callback failure unable to bypass disposal of already-collected resources; aggregate/report cancellation failures alongside driver-disposal failures while guaranteeing finalization. Preserve the original exception information.
- Regression tests required: have a pending async factory register a throwing token callback, create an already-cached disposable driver, dispose the manager, and assert the cached driver is still disposed exactly once and the cancellation failure is observable.
- Public API / PDR / release impact: implementation-only; no public API change should be needed.

### [OBS] Disposal failures after late creation are intentionally swallowed

- Status: VERIFIED IMPLEMENTATION BEHAVIOR; NOT classified as a defect.
- Evidence: src/Naravel.Foundation/Manager.cs:461-478.
- Impact: if creation completes after the manager has been disposed, DisposeQuietlyAsync suppresses any exception from disposing that late driver. This avoids an unobserved background exception but also makes disposal failure invisible to the host.
- Recommendation: retain this as an explicit operational trade-off or consider a future diagnostic hook/logging strategy only through an approved design. Do not add a dependency or public hook as part of this audit.

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

- P0: none identified.
- P1: none identified from this pass.
- P2: three source-confirmed control-flow/configuration risks listed above.
- P3: none established.
- OBS: late-driver disposal exceptions are silently suppressed after manager disposal.
- DOC: none established.
- Recommended next task: owner review of the three P2 findings; if approved, create a tightly scoped remediation task with deterministic regression tests, then run the Foundation test project, full Release build/test, roadmap validation, and independent concurrency-focused review. No remediation has been made in this audit branch.
