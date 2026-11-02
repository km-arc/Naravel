# R29 — Public API baseline at 0.1.0

Parity section: `R29` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: - (release bookkeeping; no public API, runtime behavior, or dependency change)
Read: `AGENTS.md`, `ROADMAP.md`, `roadmap/PARITY-MATRIX.md`, `roadmap/STAGE-TEMPLATE.md`, `Directory.Build.props`, every `src/*/PublicAPI.Shipped.txt` and `src/*/PublicAPI.Unshipped.txt`.
Touch: `src/*/PublicAPI.Shipped.txt`, `src/*/PublicAPI.Unshipped.txt`, `AGENTS.md`, `CHANGELOG.md`, `roadmap/AUDIT.md`, `roadmap/PARITY-MATRIX.md`, `roadmap/check.py`, `roadmap/stages/R29-public-api-baseline.md`, `ROADMAP.md`, `ROADMAP.fa.md`.
Out of scope: any source change; version bumps, tags and publishing; a CI guard that requires `PublicAPI.Unshipped.txt` to be empty on a release tag (candidate for a later stage); package validation against a published baseline version.
Decisions: Microsoft.CodeAnalysis.PublicApiAnalyzers (already referenced from `Directory.Build.props`, warnings are errors under `src/`) compares the code with `Shipped` plus `Unshipped`. It reports a removed or changed member only when that member is in `Shipped` (RS0017 and related rules). All 35 projects still listed every public member in `Unshipped` after 0.1.0 was published, so the analyzer could not see a breaking change after 0.1.0. The tag `v0.1.0` (commit `9bcc543`) and `main` have the same `src/**/*.cs` and `PublicAPI*.txt` files, so today's `Unshipped` is exactly the 0.1.0 API.

## Tasks
- [ ] **R29.T01 — Freeze the baseline:** move every entry of each `PublicAPI.Unshipped.txt` into its `PublicAPI.Shipped.txt`, leaving `Unshipped` with only `#nullable enable`. **Accept:** for every project the entries now in `Shipped` equal the entries that were in `Unshipped`, `Unshipped` has no entries, no source file changed, and CI (build with the analyzer, warnings as errors) is green.
- [ ] **R29.T02 — Process and records:** state the rule for maintainers and record the finding. **Accept:** `AGENTS.md` says where new public API goes and when it moves, `AUDIT.md` D-79, the `CHANGELOG.md` entry, the parity section, and `python3 roadmap/check.py` passing.

## Exit evidence
Record the PR and the CI run in the `ROADMAP.md` status cell, and say that the check of equal entries was done by script and the analyzer result comes from CI (no local .NET SDK).
