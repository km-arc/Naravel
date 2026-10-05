# REVIEW-01 — roadmap integrity and best-practice review (2026-10-05)

Scope: static review of the supplied snapshot. **NOT RUN:** `dotnet restore/build/test` (no .NET SDK, no NuGet access in
the review environment). Claimed counts were re-derived from source: Foundation 58, Queue 30, Cache 27, Filesystem 20,
Routing 64 = **199**, matching the docs. Package versions were not verified against NuGet.

## 1. Integrity (code vs docs vs Laravel 13)
- All 13 `src` + 5 `tests` + 1 sample projects are in `Naravel.slnx`; no project-reference cycles; Foundation depends on nothing.
- `check.py` passes; module-status claims in AGENTS.md match source except stale test totals (D-18).
- Verified real defects beyond the original D-01..D-08: D-09..D-19 in `AUDIT.md` (security type-resolution, chain options,
  dead `IFailedJobStore`, Redis non-atomic pop, attempts on reclaim, Kafka concurrency, RabbitMQ sync, no CI file, build
  hygiene, duplicate status sources, no metrics/fakes).
- Laravel 13 namespaces: 34 listed; 14 had no recorded disposition. See the coverage table in `PARITY-MATRIX.md`.
  Real gaps: Encryption, Validation, Testing fakes, Observability (BACKLOG).

## 2. Roadmap compliance with project goal (rule zero: value in .NET, native first)
| Check | Result | Action taken |
|---|---|---|
| One authoritative NEXT, one stage per pass, stage files small | Good | none |
| PDR-first gates | Good, but 15/18 gated -> agents idle often | Batch PDRs per wave (suggestion, owner choice) |
| Status vocabulary lets a PDR conclude "native is enough" | **Missing** (R11/R12/R15 likely) | Added `DECLINED`; check.py treats it as satisfied |
| Dependencies reflect shared code | **Wrong**: R03/R05 on R02, R15/R16/(R06) on R00, R04 on R03 | Fixed in table + stage files |
| R04 mixes finished Cache with open RateLimiter | Confusing | Kept ID (check.py fixed IDs); stage file marks finished work; scope is T01-T03 only |
| Verification honesty (`NOT RUN`) | Good | none |
| check.py validates structure only | Weak | Now also: NEXT deps done, DONE<->checklist, status values, slnx membership, EN/FA file parity, FA NEXT sync |

## 3. Best-practice gaps (owned tasks)
Security guard before deserialize (R00.T01) · chain options (R00.T02) · CI baseline (R00.T05) · single status source
(R00.T03) · Redis/Kafka reproduce-then-fix (R01.T05) · warnings-as-errors/SourceLink/package validation (R01.T03, R17.T02) ·
failed-job wiring, attempts on reclaim, per-job timeout, async RabbitMQ, metrics, Queue fake (R07.T05) ·
benchmarks for the "speed" goal (add to R17.T02) · trimming/AOT annotations (R17, optional).

## 4. Token-cost guidance for agents
Reading path stays: AGENTS.md -> ROADMAP.md -> one stage file. Added `Files:` list to R00 as the template; add the same
block (files to read, out-of-scope, acceptance tests) to each stage when it becomes NEXT. Do not read PROGRESS*.md,
FA files, or other stages. Expect ~4-6k tokens of orientation.

## 5. Needs owner decision
1. Testcontainers vs CI service containers + env-gated tests (R01).
2. Approve RabbitMQ.Client async migration (breaking) (R07.T05).
3. Encryption and Validation: new stages or Native? (BACKLOG).
4. Batch PDR approvals per wave to reduce idle time.

## 6. Applied after owner approval (2026-10-05)
Owner accepted the recommendations: ROADMAP rewritten with binding decisions OD-01..OD-09, new default order
(R00→R01→R07→R04→R05→R06→R08→R09→R10→R18→R03→R15→R16→R11→R12→R13→R14→R17), PDR waves, quality principles S1-S3, new
stage R18 (Encryption), every stage file now has `Read/Touch/Out of scope/Decisions` and `Accept:` per task.
