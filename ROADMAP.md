# ROADMAP — Naravel: closing the Laravel ⇄ .NET gap

Goal: **Laravel's simplicity with .NET's speed.** Port an idea only if it adds practical value in .NET (rule zero);
otherwise use the native solution and record it in a PDR. Persian owner overview: [ROADMAP.fa.md](ROADMAP.fa.md).
Audit: [roadmap/AUDIT.md](roadmap/AUDIT.md) · Matrix: [roadmap/PARITY-MATRIX.md](roadmap/PARITY-MATRIX.md) ·
Backlog: [roadmap/BACKLOG.md](roadmap/BACKLOG.md) · Review: [roadmap/REVIEW-01.md](roadmap/REVIEW-01.md) ·
Stage template: [roadmap/STAGE-TEMPLATE.md](roadmap/STAGE-TEMPLATE.md).

## How to run one pass (agents: follow exactly, read nothing else)
1. Read [AGENTS.md](AGENTS.md), this file, and `roadmap/stages/<NEXT>-*.md`. Do **not** read other stage files, FA files,
   `PROGRESS*.md` or other docs unless the stage's `Read:` line lists them.
2. Gate check (see *Gates*). If unmet, follow the *PDR-batching rule* and stop.
3. Do the tasks in order. Each task has an **Accept:** line = the verifiable condition. Tick `- [x]` only when it holds.
4. A stage may take several passes: tick finished tasks, set status `DOING`, keep `NEXT`. Never start another stage.
5. When every task is ticked: run the *Definition of done*, set status `DONE (date, verification)`, set `NEXT` to the first
   stage in **Default order** that is not `DONE`/`DECLINED` and whose dependencies are all `DONE`/`DECLINED`, then stop.
6. Report: what changed, what was verified, what was **NOT RUN** and why. Never claim an unrun test passed.

## NEXT
```text
NEXT: R07
```

## Stages
Gate: `-` none · `PDR` an EN+FA PDR must exist **and be owner-approved** before code · `GO` owner must say go.
Status lives **only here**: `TODO` · `DOING` · `DONE (date, verification)` · `BLOCKED (reason)` · `DECLINED (PDR-nnn, reason)`.
`DECLINED` = the PDR concluded native .NET is enough; it is a valid result, counts as satisfying dependents, and still
needs the EN+FA PDR.

| ID | Title | Deps | Gate | Status |
|---|---|---|---|---|
| R00 | Queue correctness: job type registry + type guard, chain options, docs, CI baseline | - | - | DONE (2026-10-05; build succeeded, 208/208 tests) |
| R01 | Driver integration tests (env-gated, CI service containers), Redis/Kafka fixes, benchmarks | R00 | - (OD-01) | DONE (2026-10-05; 202/205 tests passed with Redis + SQLite exercised; RabbitMQ/Kafka/Memcached skipped locally; roadmap check passed) |
| R02 | Filesystem on Foundation + API parity (legacy Stage 2, PDR-008) | R00 | PDR | DONE (2026-10-04; build clean, 172/172 tests) |
| R03 | Naravel.Redis shared connections (PDR-010) | R01 | PDR | TODO |
| R04 | RateLimiter on Cache (Cache/locks already done; PDR-007 addendum) | R01 | PDR | DONE (2026-10-06; Release restore/build/test, 225 passed, 7 skipped; roadmap check passed) |
| R05 | Http.Middleware + routing completion (legacy 4b) | R04 | GO | DONE (2026-10-06; Release restore/build/test, 233 passed, 7 skipped; routing benchmark and roadmap check passed) |
| R06 | Events (PDR-011) | R00 | PDR | DONE (2026-10-07; Release restore/build/test, 243 passed, 8 skipped; Events benchmark 137.8 ns/24 B; roadmap check passed) |
| R07 | Queue completion A: failed jobs, attempts/timeouts, batches, worker controls, RabbitMQ async, metrics, fake (PDR-012) | R01 | PDR | DOING (residual PDR-012 items open; see roadmap/stages/R07) |
| R08 | Queue completion B: unique/overlap/rate-limited/throttle middleware (needs Cache) | R04, R07 | PDR | TODO |
| R09 | Mail (PDR-013) | R06, R07 | PDR | TODO |
| R10 | Notifications (PDR-014) | R07, R09 | PDR | TODO |
| R11 | Session (PDR-015) | R04, R05 | PDR | TODO |
| R12 | Auth-lite route middleware (legacy 4c, PDR-016) | R05, R11 | PDR | TODO |
| R13 | Console + Scheduling (PDR-017) | R04, R05, R07 | PDR | TODO |
| R14 | Broadcasting (PDR-018) | R06, R07 | PDR | TODO |
| R15 | Hashing (PDR-019) | - | PDR | TODO |
| R16 | Pagination (PDR-020) | - | PDR | TODO |
| R17 | Release hardening + re-audit | R01, R03, R04, R05, R06, R07, R08, R09, R10, R11, R12, R13, R14, R15, R16, R18 | owner approvals | TODO |
| R18 | Encryption facade + encrypted jobs (PDR-021) | R07 | PDR | TODO |

R17 lists every stage in release scope; the owner may mark an out-of-scope stage `DECLINED`/`BLOCKED` to release without it.
R02 was completed before this roadmap existed; its R00 dependency is nominal and does not invalidate it.
## Single source of truth (D-18)

- **Stage status and `NEXT`:** only the table and `NEXT` block in this file. Other documents link here; they do not copy a stage status.
- **Verified test counts:** only in the dated evidence of a stage's status cell above (R00.T06). Live numbers come from the
  [CI run summary](https://github.com/km-arc/Naravel/actions/workflows/ci.yml) ("Test results by project"). No other document states a test total.
- **Historical records** (`PROGRESS*.md`, `CHANGELOG.md`, `roadmap/REVIEW-01.md`, the resolved notes in `roadmap/AUDIT.md`) keep
  dated snapshots. They are never current status.
- `roadmap/check.py` enforces this: it fails on a hand-typed test count or a repeated stage status outside those places. Put
  `<!-- check:allow-count -->` on a line only when a number is not a test total (for example HTTP status codes).

**Default order:** R07 → R08 → R09 → R10 → R18 → R03 → R15 → R16 → R11 → R12 → R13 → R14 → R17.
Rationale: correctness and proof first (R00, R01); then the two modules that carry the project's value (Queue, Cache);
then the most Laravel-feeling features (middleware, events, mail); thin or likely-native stages last.
Never run two stages that edit the same project in parallel. Parallel-safe: R15, R16, R06 touch no other module.
PDR numbers are fixed: 007a RateLimiter · 008 Filesystem · 009 Routing (done) · 010 Redis · 011 Events · 012 Queue II ·
013 Mail · 014 Notifications · 015 Session · 016 Auth · 017 Console · 018 Broadcasting · 019 Hashing · 020 Pagination ·
021 Encryption.

## Owner decisions (binding; recorded 2026-10-05)
| ID | Decision |
|---|---|
| OD-01 | No Testcontainers. Provider tests are **env-gated** (skipped when the variable is unset) and run in CI against GitHub service containers (Linux job only). Database driver tests use SQLite. Approved **test-only** packages: `Microsoft.EntityFrameworkCore.Sqlite`, `BenchmarkDotNet` (benchmarks project, never packed). |
| OD-02 | `RabbitMQ.Client` 6.x → 7.x async API is approved **inside R07 only**. `AWSSDK.S3` 4.x and `StackExchange.Redis` 3.x still need separate approval. |
| OD-03 | Encryption is a stage (R18): thin facade over ASP.NET Core Data Protection; no custom cryptography. |
| OD-04 | Validation / Form Requests is **not built**. Document FluentValidation / DataAnnotations usage; revisit after R17. |
| OD-05 | PDRs are written in **waves** (see *PDR-batching rule*). |
| OD-06 | Quality principles S1–S3 apply to every stage (see *Definition of done*). |
| OD-07 | Queue job identity is an explicit **alias registry**, not `AssemblyQualifiedName` (R00.T01). |
| OD-08 | Expected thin/native outcomes: R11, R12, R15 likely `DECLINED` or tiny; R13 commands likely native; R14 thin over SignalR. A PDR must still argue it. |
| OD-09 | Pre-1.0 (`0.x-preview`): breaking changes are allowed if listed in `CHANGELOG.md`. Revisit at R17. |

## PDR-batching rule
If `NEXT` has gate `PDR`/`GO` and no approved PDR exists, do **not** idle: write the EN+FA PDRs for **every stage of
that wave** (a PDR is a document, so dependency status does not matter), set those rows `BLOCKED (awaiting PDR approval)`,
and stop. The owner approves the wave in one review; afterwards the stages run without further stops.
- **Wave A:** PDR-007a (R04), PDR-012 (R07); GO for R05.
- **Wave B:** PDR-011 (R06), PDR-013 (R09), PDR-014 (R10), PDR-021 (R18).
- **Wave C:** PDR-010 (R03), PDR-019, 020, 015, 016, 017, 018.
Every PDR opens with a **native-first verdict** (can plain .NET do this in ≤ 3 lines?) and may conclude `DECLINED`.

## Definition of done (every stage)
1. Gate satisfied (above). Driver modules extend `Manager<TDriver,TOptions>`; projects are in `Naravel.slnx`; package
   versions only in `Directory.Packages.props`; no dependency beyond OD-01/OD-02 without a PDR.
2. **S1 Simplicity:** the module README/docs start with a ≤ 10-line quickstart; the common case needs ≤ 3 lines of user
   code (registration + use). The PDR states the quickstart before implementation.
3. **S2 Speed:** no reflection or `Type.GetType` on hot paths (use registries / `System.Text.Json` metadata), async +
   `CancellationToken` on all I/O, no sync-over-async, no avoidable per-call allocation. Add or extend a benchmark in
   `benchmarks/` for any new hot path (after R01.T05 creates the project).
4. **S3 Observable & testable:** I/O modules emit `System.Diagnostics.Metrics` (`Meter` named after the package) and
   `ActivitySource` spans; every module ships a Fake/in-memory test double (`Naravel.<Module>.Testing` namespace).
5. Tests: runtime `Extend`, config-reload (config-driven modules), contract tests for every driver, fake usage.
6. XML docs on public types (purpose, *Laravel equivalent*, *why it exists*, *not ported*); EN+FA docs and PDRs; update
   both parity tables, `CHANGELOG.md`, and this table.
7. Run `dotnet restore Naravel.slnx && dotnet build Naravel.slnx -c Release && dotnet test Naravel.slnx -c Release` and
   `/usr/bin/python3 roadmap/check.py`. Record the actual result in the status cell; if impossible write `NOT RUN: <reason>`.

## Rules that keep this cheap
- Task IDs are `R##.T##`; cite them in commits and reports. A stage file never repeats these rules.
- A defect found outside the active stage goes into [roadmap/AUDIT.md](roadmap/AUDIT.md) as the next `D-nn` with an owning
  task; do not expand scope.
- Re-derive test counts from source (`[Fact]` + `[InlineData]` rows), never from docs.
- Never change a stage status outside this table. `PROGRESS*.md` are history, not pointers.
