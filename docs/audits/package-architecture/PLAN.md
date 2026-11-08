# Package Architecture Review Program

## Objective

Perform a source-led, principal-architect review of every packable Naravel package. The purpose is to identify correctness, security, lifecycle, extensibility, performance, compatibility, operability, packaging, and documentation risks before proposing implementation changes.

This is an audit, not an implementation campaign. Do not change runtime code, public APIs, package dependencies, configuration contracts, defaults, or test expectations during the review. Record evidence and recommendations first. Any remediation that changes public behavior, dependencies, architecture, or release contracts requires a separate owner-approved task/PDR.

## Baseline and scope

- Current repository synchronization baseline: `main` at `a69e0becca197ee8dc14d2a576b45dd763263833` (2026-11-08). The audit branch is synchronized with this commit. Each package report retains the exact source-review baseline used for that package. The intervening main changes since the previous audit sync were compared against the already reviewed Foundation/Cache/Redis package source and test paths; those paths were unchanged.
- Target framework / repository version are determined from the checked-in project metadata at the baseline, not assumed from this document.
- Audit order is dependency-aware; foundational projects are reviewed before their adapters/providers.
- Include every packable project under `src/`, including `Naravel.Foundation`.
- Inventory result: 36 source projects under `src/`; 35 package-specific pages under `docs/nuget/`; `Naravel.Foundation` uses `docs/en/foundation.md` as its packed README. Therefore the review register contains 36 packages, not 35.
- The 36 project files inspected in the inventory do not declare `IsPackable=false`; package IDs are explicit where declared and otherwise follow the project/assembly name. Confirm again against pack output during the packaging pass.

## Review rules

1. Use the checked-out source at the pinned baseline as the authority. Verify every API, configuration key, default, dependency, exception, and behavior from implementation, tests, project metadata, shipped API baseline, and maintained English docs.
2. Never invent or infer an API from a package name, Laravel analogy, sample, or outdated document.
3. Do not edit code or tests to make findings disappear. Do not weaken, skip, suppress, or delete tests.
4. Every finding must have file/line evidence, affected package/version, impact, severity, a concrete failure scenario, and a testable recommendation.
5. Separate confirmed defects from design questions, test gaps, documentation drift, and improvement suggestions.
6. Do not silently broaden scope. Cross-package findings are recorded once with all affected packages linked.
7. All artifacts are English-only. Do not generate, translate, sync, or scaffold localized documentation.
8. Report verification honestly. A source inspection is not a build; a test recommendation is not a test run.

## Review dimensions (apply to every package)

- **Package contract:** PackageId, versioning, target framework, README metadata/content, license/description/tags, symbols, transitive dependencies, dependency direction, and restore compatibility.
- **Public API:** shipped/unshipped API baselines, naming, nullability, overload design, XML docs, breaking-change exposure, and consistency across providers.
- **Architecture:** responsibilities, abstraction boundaries, coupling, dependency inversion, driver/manager/factory patterns, extension points, and duplication.
- **DI and lifetime:** registration idempotency, singleton/scoped/transient correctness, options ownership, disposal exactly once, replacement/override behavior, and thread safety.
- **Configuration:** actual keys/defaults, validation, reload behavior, named instances, invalid-value behavior, and configuration precedence.
- **Async and reliability:** async I/O, CancellationToken propagation, sync-over-async, timeouts, retries/backoff, cancellation races, idempotency, ordering, concurrency, and failure recovery.
- **Time:** injected TimeProvider for current-time decisions, deterministic tests, expiration boundaries, and correct separation of wall-clock time from elapsed time or external timestamps.
- **Security and data:** serialization/type resolution, secret handling, cryptographic boundaries, path traversal, SQL parameterization, tenant isolation, payload validation, and trust boundaries as applicable.
- **Observability:** package-appropriate Meter/ActivitySource instrumentation, useful dimensions, cardinality, failure visibility, and fake/test-double parity.
- **Performance and AOT:** hot-path allocations, reflection, unbounded caches, resource leaks, trimming/AOT analyzer warnings, and evidence for performance claims.
- **Tests:** public contract coverage, edge/boundary cases, regression coverage, driver contract reuse, real-provider integration gates, deterministic fakes, and meaningful assertions.
- **Documentation:** every code sample/API/default/provider guarantee checked against source and tests; links, install instructions, and package-specific README accuracy.

## Severity scale

- **P0 – Critical:** exploitable security flaw, data loss/corruption, or widespread production outage with a credible path.
- **P1 – High:** major correctness, isolation, concurrency, durability, or compatibility defect likely to affect real users.
- **P2 – Medium:** material edge-case defect, missing protection, operational blind spot, or significant maintainability/performance risk.
- **P3 – Low:** limited-impact defect or quality issue with a clear user/developer consequence.
- **OBS – Observation:** verified design trade-off, improvement opportunity, or owner decision; not a defect.
- **DOC – Documentation mismatch:** user-facing claim is demonstrably inconsistent with implementation/tests/metadata.

A severity is not assigned based on intuition alone. Explain likelihood and impact. If evidence is incomplete, mark the item `UNCONFIRMED` and list the missing proof.

## Per-package workflow

For each entry in `REGISTER.md`:

1. Read the package project file, all source files for that package, its tests, relevant shared test helpers, relevant package docs, and applicable PDR/roadmap decisions.
2. Build the package dependency map and public API map from source.
3. Trace representative public operations through registration, options, manager/driver, I/O, cancellation, failure, metrics, and disposal.
4. Inspect tests against the actual contract; identify untested branches and missing negative/boundary cases.
5. Check package metadata and packed README path against the package project file and release/build scripts.
6. Produce a report using `REPORT-TEMPLATE.md`; cite exact paths and line numbers.
7. Update the register with status, findings, and commit/report link.
8. Present the result in the owner tracking thread before moving to the next package.

## Finding lifecycle and closure

Every finding receives a stable ID in `FINDINGS.md` and a state:

- `OPEN`: evidence recorded; no remediation authorized or performed.
- `APPROVED FOR REMEDIATION`: the owner explicitly authorized a separate fix task/PDR.
- `IN REMEDIATION`: an approved change is being implemented on its own task/branch.
- `FIXED — WAITING VERIFICATION`: code/docs changed, but required regression tests and CI are not yet proven green.
- `VERIFIED CLOSED`: the exact regression tests, relevant package/solution verification, roadmap checks, and required CI jobs passed; evidence links are recorded.
- `ACCEPTED RISK`: the owner explicitly chose not to fix the item and the rationale is recorded.

A report's source-level finding must never be marked resolved merely because a proposed patch exists or a commit was created. No fix is made by this audit branch. The owner authorizes remediation separately; test weakening or warning suppression is never an acceptable closure path.

## Exit criteria

A package is `REVIEWED WITH FINDINGS` when all review dimensions have been addressed, evidence is linked, tests run/not-run are explicit, and open owner decisions are identified. A package can be reviewed with open findings; findings remain open until the owner authorizes remediation and verification.

The full audit is complete only when all 36 packages have reviewed reports, cross-package findings are deduplicated, and a prioritized remediation roadmap is proposed. No remediation is merged by this audit branch.


