# AGENTS.md — start here (AI agents and new contributors)

## Goal and quality bar
**Laravel's simplicity with .NET's speed.** Every stage must meet S1 (≤ 3 lines of user code for the common case, ≤ 10-line
quickstart), S2 (no reflection on hot paths, async + `CancellationToken`, benchmarked) and S3 (Meter/ActivitySource + a
Fake) from the Definition of done in `ROADMAP.md`. Binding owner decisions are `OD-01…OD-09` in `ROADMAP.md`.

## Roadmap quickstart
The authoritative roadmap and sole `NEXT` pointer are in [ROADMAP.md](ROADMAP.md). The roadmap table is the only place for stage status; consult
[roadmap/AUDIT.md](roadmap/AUDIT.md), [roadmap/PARITY-MATRIX.md](roadmap/PARITY-MATRIX.md), and
[roadmap/BACKLOG.md](roadmap/BACKLOG.md) for their named purposes.

For roadmap work, read this file, [ROADMAP.md](ROADMAP.md), and **only** the stage file named by `NEXT`;
read only that stage's named parity section. Do not start later stages or bypass their PDR/GO/owner-approval
gates. At stage completion, update the roadmap status table, stage checklist, related docs/tests, and run
`/usr/bin/python3 roadmap/check.py`. Existing `PROGRESS*.md` files are historical records and approved
decisions, not competing roadmap pointers.

## What this repository is
**Naravel** ports the *ideas* of the Laravel framework to .NET (target: latest LTS, currently `net10.0`).
It is **not** a line-by-line clone. Rule zero:

> Implement a Laravel feature only if it brings value in .NET. If .NET already solves the problem natively and
> idiomatically, use the native solution and record that decision.

## This is a monorepo (one repo, one solution, all modules)
Every module lives in **this** repository and is built by **one** solution, `Naravel.slnx`. Never create a separate
repository, solution or copy of shared build files for a module. One command verifies everything:

```
dotnet restore Naravel.slnx && dotnet build Naravel.slnx -c Release && dotnet test Naravel.slnx -c Release
```

| Shared, repo-wide (edit once) | Rule |
|---|---|
| `Directory.Build.props` | Target framework, nullable, NuGet metadata, **one lock-step version for all packages** (`VersionPrefix`). Projects must not repeat these. |
| `Directory.Packages.props` | Central Package Management. **Every** NuGet version lives here; `.csproj` files use `<PackageReference Include="X" />` without `Version`. |
| `Naravel.slnx` | Every new project (src, tests, samples) must be added here, or CI will not build it. |
| `.github/workflows/ci.yml` | Restore, build, test the whole solution. Do not add per-module workflows. |

## Where things are
| Path | What |
|---|---|
| `src/Naravel.Foundation` | Shared **Manager → Driver → Factory** base (Laravel's `Illuminate\Support\Manager`). Every driver-based module builds on it. |
| `src/Naravel.<Module>` | A module: contract, options, manager, built-in dependency-free drivers, `AddNaravel<Module>()` / `Add<Module>()` extension. |
| `src/Naravel.<Module>.<Provider>` | A driver that pulls a heavy dependency (Redis, RabbitMQ, EF Core ...), so users only install what they use. |
| `tests/Naravel.<Module>.Tests` | xUnit + FluentAssertions (7.x) tests for that module. |
| `samples/` | Runnable samples (never packed). |
| `docs/en` | Authoritative English module guides. |
| `docs/README.md`, `docs/assets` | English documentation index and README images. The README stays a short front page: put detail in `docs/en`, not in the README. |
| `docs/pdr/en` | **PDRs** — Parity Decision Records: why each Laravel feature was adopted, adapted or rejected. |
| `docs/en/next-agent-prompt.md` | Ready-to-paste task prompt for the next agent. |
| `docs/nuget` | English NuGet package pages: one `<PackageId>.md` per package, packed as its root `README.md` and checked by CI. A new package adds its file here and the `PackageReadmeFile` metadata. |

## Module status
The status table in [ROADMAP.md](ROADMAP.md) is the single source of truth. See the module docs for current APIs and usage.
Verified test totals are recorded only in a stage's ROADMAP status cell, and live numbers come from CI. Do not copy a
status or a count into other documents; `roadmap/check.py` fails on it (see "Single source of truth" in ROADMAP.md).

## Routing / middleware work
Routing's current state and remaining work are tracked in [ROADMAP.md](ROADMAP.md) (R05); detailed Stage 4a/4b
history and the accepted PDR-009 decisions remain in [PROGRESS-ROUTING.md](PROGRESS-ROUTING.md).

## Read in this order
1. `ROADMAP.md` — authoritative `NEXT`, dependencies, gates, and stage status.
2. Only the stage file named by `NEXT`; consult the one parity-matrix section it names.
3. The relevant accepted PDR / historical progress record when that stage calls for it.
4. `docs/en/overview.md`, applicable PDRs, `docs/en/module-authoring-guide.md`, and `docs/en/laravel-parity.md`
   when needed for implementation.

## Hard rules
1. **PDR first.** Before adding any Laravel-equivalent feature, write a PDR (Laravel feature → native .NET option → value added? → decision → rejected alternatives). Get owner approval for large decisions.
2. **Never re-implement** driver resolution, caching of driver instances, runtime `Extend`, or config-driven default selection. Subclass `Manager<TDriver, TOptions>`.
3. **Dependencies point one way.** Foundation depends on no module. Modules depend on Foundation. A module may depend on another module only if a PDR says so (for example Events → Queue for queued listeners); no cycles.
4. **English-only, source-verified docs.** English is the sole documentation language. Keep maintained guides in `docs/en/`, PDRs in `docs/pdr/en/`, and package pages in `docs/nuget/`. **Never create, generate, translate, sync, scaffold, or publish Persian-language documentation** (including `docs/fa/`, `docs/pdr/fa/`, `*.fa.md`, generated translations, or temporary draft files). Do not create Persian documentation and rely on CI to reject it; prevention happens before generation. If a request conflicts with this rule, stop and ask the owner rather than producing Persian documentation. Documentation is part of the public contract: every API name and signature, default, option, exception, provider, framework target, behavior, guarantee, and example must be checked against the current implementation, tests, project metadata, and CI. Source code and executable tests win over old docs, Laravel behavior, assumptions, or memory. Public types have XML docs with: purpose, *Laravel equivalent*, *why it exists*, *what was intentionally not ported*.
5. **Tests are mandatory**, including a runtime `Extend` test and (if config-driven) a config-reload test.
6. **Don't port PHP mechanics** (magic `__call`, array configs, reflection tricks) just for resemblance.
7. **Async-first** for anything that touches I/O; `CancellationToken` on every async API.
8. **Do not add dependencies** beyond `Microsoft.Extensions.*` abstractions without a PDR (already approved: see OD-01/OD-02 in `ROADMAP.md`). FluentAssertions stays on 7.x (8.x+ is commercially licensed). `.github/dependabot.yml` proposes version bumps of packages and actions that are already in the repository (never a new one) and its `ignore` entries repeat the exceptions above; its pull requests are dependency changes, so the owner reviews and merges them like any other.
9. **Be honest about verification.** If you could not run `dotnet build`/`dotnet test` (no SDK, no NuGet access), say so explicitly and never describe unrun tests as passing.
10. **Never resolve a CLR type from stored data** (queue/cache/session payloads) except through an explicit alias registry (OD-07).
11. **Stay inside `Touch:`** of the active stage file; log anything else in `roadmap/AUDIT.md`.
12. **Public API baseline.** New public API goes into the project's `PublicAPI.Unshipped.txt`. `PublicAPI.Shipped.txt` holds what a published version contains (0.1.0 since R29); do not edit its entries to make a build pass, because a removed or changed member is a breaking change that needs an owner decision and an AUDIT entry. When a version is released, move that version's `Unshipped` entries into `Shipped`.
13. **Never invent an API, a package or a configuration key; verify it in the code or the docs first.** Check it where it is defined: a method, type or overload in the source under `src/` and in `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`; a package ID in the `PackageId` (or project name) of `src/*/*.csproj` and in `docs/nuget`; a configuration key in the options class the module binds and in its registration code and tests; a default or an exception message in the implementation and its tests. Source and tests win over prose. If you cannot find it, stop and ask the owner; never guess. A name that is only a placeholder must be labelled as one (see "Evidence before claims" below).

## AI agent operating rules

These rules are mandatory for every AI agent and automated coding assistant working in this repository.

### Evidence before claims
- Never invent, assume, or autocomplete a public API, type, method, overload, package, NuGet package ID, provider, configuration key, option name, default, or command.
- Before using or documenting one, verify it in current source, project/package metadata, tests, or authoritative maintained documentation. Prefer implementation and tests over prose. If it cannot be verified, stop and ask; do not guess.
- Do not add a dependency or package unless it is already approved by an accepted PDR and the repository's dependency rules.

### Tests must not be weakened
- Never delete, skip, disable, quarantine, loosen, or rewrite a test/assertion merely to make a build or CI pass.
- Never reduce test coverage, remove test data/edge cases, change filters, or weaken expected values to hide a regression.
- If a test is wrong, demonstrate why from the contract/source, explain the correction in the PR, and replace it with an equally strong or stronger test. Preserve failing evidence until the underlying defect is fixed.

### Stop and ask instead of guessing
Stop before editing and ask the owner when:
- the task or acceptance criteria are ambiguous or conflict with repository rules;
- implementation requires a public API, package, configuration key, dependency, compatibility promise, or architectural decision not explicitly authorized by the brief or an accepted PDR;
- source, tests, and documentation disagree and intended behavior cannot be established;
- proceeding would require guessing, silently changing scope, or making an irreversible/release-impacting decision.
State the exact uncertainty, evidence checked, and smallest decision needed. Do not fill gaps with an invented design.

### Plan before substantial implementation
- For multi-file, cross-module, architectural, migration, or otherwise substantial tasks, first provide a concise plan: scope/Touch paths, source-of-truth inspection, design and alternatives, tests, documentation, risks, and verification commands.
- Wait for owner approval when the plan introduces an unapproved API/dependency/architecture decision or changes scope. Do not begin implementation while such approval is pending.
- Small, local, unambiguous fixes may proceed without a separate plan.

### Self-review and pull-request size
- Before committing or opening a PR, review the complete diff as a fresh reviewer. Check scope, correctness, API compatibility, tests, docs, generated files, secrets, accidental unrelated edits, and whether every claim is evidenced.
- **Default PR size limit: at most 500 changed non-generated lines (additions + deletions) and at most 20 changed files.** Split work before opening the PR when either limit would be exceeded. Do not evade the limit by moving code, hiding deletions, or misclassifying files. An exception is allowed only for a genuinely indivisible or explicitly owner-requested bulk migration/removal; obtain the owner's approval before proceeding and record the reason and approval in the PR description. A bulk deletion is not silently exempt.
- The PR description must state behavior changed, important design choices, tests actually run and not run, and source/test evidence for documentation claims.
- **Documentation change closure (mandatory).** Before changing, deleting, or renaming a document, identify its inbound references across the entire tracked repository. Search all tracked files—not only `docs/en`—including `docs/nuget`, root Markdown, PDRs, templates, scripts, workflows, and package metadata. Update or remove every reference in the same change. After editing, search again for every removed/renamed path and validate all affected local Markdown links. Run `/usr/bin/python3 roadmap/check.py` from the repository root; fix every stale-link error before committing. CI is a backstop, not a substitute for this pre-commit review. Never assume a link is safe because the target file was deleted intentionally.

### Warnings and suppressions
- Do not add or broaden warning suppressions, pragmas, NoWarn entries, analyzer exclusions, or #pragma warning disable merely to get a green build.
- Every necessary suppression must be as narrow and local as possible and include a one-line explanation immediately beside it stating why the warning is a false positive or why the exception is safe, with a reference to the relevant issue/PDR when applicable.
- Prefer fixing the underlying warning. Remove obsolete suppressions.

### Time-dependent code
- Use injected TimeProvider and timeProvider.GetUtcNow() for production code that needs the current time. Do not introduce direct calls to DateTime.UtcNow or DateTimeOffset.UtcNow in application/runtime logic.
- Pass the provider through appropriate constructors/DI and use FakeTimeProvider (or the repository's established test fake) for deterministic time-dependent tests.
- Do not perform a blind mechanical replacement: distinguish wall-clock UTC time, elapsed-time measurement, timestamps supplied by external systems, and date-only/business-calendar semantics. Preserve behavior and test boundary conditions.
- Treat existing direct-clock call sites as migration work: inventory them, migrate only within the approved stage/task scope, and record remaining work in the roadmap/audit rather than silently expanding scope.

### Independent review and mutation testing
- For security-sensitive, concurrency-sensitive, serialization, retry, throttling, expiration, and other critical business logic, request a review from a fresh agent/session that did not author the change. Give it the brief, relevant source/tests, and diff; ask it to identify defects and missing cases independently.
- For such critical logic, use mutation testing when a suitable tool and test scope are available. Confirm that tests detect meaningful mutations (for example boundary changes, removed guards, inverted conditions, or altered retry/expiry behavior).
- Do not claim independent review or mutation testing unless it was actually performed. If unavailable or out of scope, state that explicitly and explain the remaining risk.

## Adding or changing a module (monorepo checklist)
1. Read the PDRs; write the module's PDR; wait for approval.
2. Create `src/Naravel.<Module>` (+ provider projects) and `tests/Naravel.<Module>.Tests`; add all to `Naravel.slnx`.
3. Add new package versions to `Directory.Packages.props` only.
4. Implement on `Manager<TDriver, TOptions>`; add tests; update the English docs and PDRs against the implementation; update the parity tables and the **Module status** table above.
5. Run the verify command above (or rely on CI) and report exactly what was and was not verified.

## Verify
```
dotnet restore Naravel.slnx
dotnet test Naravel.slnx
```

## Documentation is an implementation contract

- English is the only maintained documentation language. Do not add Persian translations, `*.fa.md` files, `docs/fa/`, or `docs/pdr/fa/`.
- Before documenting a public API, inspect its current declaration and implementation in `src/`, its tests, package metadata, and the relevant provider registration. Do not copy Laravel signatures or infer .NET behavior from Laravel.
- Verify every code sample against the current API. Prefer executable documentation tests in the relevant test project; if a snippet cannot be executed, label it clearly as illustrative and keep it minimal.
- Document actual defaults, overloads, cancellation behavior, exceptions, provider differences, platform/framework support, and failure guarantees. Do not promise behavior that is not implemented and tested.
- Update the guide, NuGet package page, XML docs, parity record, and changelog in the same change whenever the affected public behavior or package contract changes.
- In the pull request, list the source files and tests used to verify documentation claims. A passing build alone does not prove that prose is accurate.
