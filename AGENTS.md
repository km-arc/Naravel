# AGENTS.md — start here (AI agents and new contributors)

## Goal and quality bar
**Laravel's simplicity with .NET's speed.** Every stage must meet S1 (≤ 3 lines of user code for the common case, ≤ 10-line
quickstart), S2 (no reflection on hot paths, async + `CancellationToken`, benchmarked) and S3 (Meter/ActivitySource + a
Fake) from the Definition of done in `ROADMAP.md`. Binding owner decisions are `OD-01…OD-09` in `ROADMAP.md`.

## Roadmap quickstart
The authoritative roadmap and sole `NEXT` pointer are in [ROADMAP.md](ROADMAP.md). Persian owner overview:
[ROADMAP.fa.md](ROADMAP.fa.md). The roadmap table is the only place for stage status; consult
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
| `docs/en`, `docs/fa` | Parallel English / Persian docs (same file names). |
| `docs/pdr/en`, `docs/pdr/fa` | **PDRs** — Parity Decision Records: why each Laravel feature was adopted, adapted or rejected. |
| `docs/en/next-agent-prompt.md` | Ready-to-paste task prompt for the next agent. |

## Module status
The status table in [ROADMAP.md](ROADMAP.md) is the single source of truth. See the module docs for current APIs and usage.

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
4. **Docs in both languages.** Every module ships `docs/en/*` and `docs/fa/*` and PDRs in both. Public types have XML docs with: purpose, *Laravel equivalent*, *why it exists*, *what was intentionally not ported*.
5. **Tests are mandatory**, including a runtime `Extend` test and (if config-driven) a config-reload test.
6. **Don't port PHP mechanics** (magic `__call`, array configs, reflection tricks) just for resemblance.
7. **Async-first** for anything that touches I/O; `CancellationToken` on every async API.
8. **Do not add dependencies** beyond `Microsoft.Extensions.*` abstractions without a PDR (already approved: see OD-01/OD-02 in `ROADMAP.md`). FluentAssertions stays on 7.x (8.x+ is commercially licensed).
9. **Be honest about verification.** If you could not run `dotnet build`/`dotnet test` (no SDK, no NuGet access), say so explicitly and never describe unrun tests as passing.
10. **Never resolve a CLR type from stored data** (queue/cache/session payloads) except through an explicit alias registry (OD-07).
11. **Stay inside `Touch:`** of the active stage file; log anything else in `roadmap/AUDIT.md`.

## Adding or changing a module (monorepo checklist)
1. Read the PDRs; write the module's PDR; wait for approval.
2. Create `src/Naravel.<Module>` (+ provider projects) and `tests/Naravel.<Module>.Tests`; add all to `Naravel.slnx`.
3. Add new package versions to `Directory.Packages.props` only.
4. Implement on `Manager<TDriver, TOptions>`; add tests; add EN + FA docs; update the parity tables and the **Module status** table above.
5. Run the verify command above (or rely on CI) and report exactly what was and was not verified.

## Verify
```
dotnet restore Naravel.slnx
dotnet test Naravel.slnx
```
