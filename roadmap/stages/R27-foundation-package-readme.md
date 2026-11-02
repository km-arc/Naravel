# R27 — Foundation NuGet package README

Parity section: `R27` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: - (packaging metadata and documentation only; no public API, runtime behavior, or dependency change)
Read: `AGENTS.md`, `ROADMAP.md`, `roadmap/PARITY-MATRIX.md`, `roadmap/STAGE-TEMPLATE.md`, `Directory.Build.props`, `src/Naravel.Foundation/Naravel.Foundation.csproj`, `src/Naravel.Foundation/ServiceCollectionExtensions.cs`, `src/Naravel.Foundation/Manager.cs`, `docs/en/architecture.md`, `docs/fa/architecture.md`, `.github/workflows/ci.yml`, `CHANGELOG.md`, `README.md`, `README.fa.md`.
Touch: `.github/scripts/check-foundation-readme.py`, `.github/workflows/ci.yml`, `CHANGELOG.md`, `README.md`, `README.fa.md`, `docs/en/foundation.md`, `docs/fa/foundation.md`, `src/Naravel.Foundation/Naravel.Foundation.csproj`, `roadmap/AUDIT.md`, `roadmap/PARITY-MATRIX.md`, `roadmap/check.py`, `roadmap/stages/R27-foundation-package-readme.md`, `ROADMAP.md`, `ROADMAP.fa.md`.
Out of scope: Other packages' README metadata; changing or republishing the immutable 0.1.0 package; version bumps, tags, and NuGet publishing; runtime code/API changes; unrelated PDR or AOT work.
Decisions: No new feature, public API, runtime behavior, or dependency. Use a Foundation-specific English Markdown file as the embedded NuGet README and keep its full EN/FA guides in `docs/`.

## Tasks
- [ ] **R27.T01 — Foundation consumer documentation:** add matching English and Persian Foundation guides with a verified quickstart and link them from the repository READMEs. **Accept:** `docs/en/foundation.md` and `docs/fa/foundation.md` describe the same package behavior and the English guide is suitable for NuGet Gallery rendering.
- [ ] **R27.T02 — NuGet README metadata:** pack the English Foundation guide as `README.md` at the package root and set `PackageReadmeFile` to that exact path. **Accept:** the packed `Naravel.Foundation.nuspec` contains `<readme>README.md</readme>` and the `.nupkg` contains root `README.md`.
- [ ] **R27.T03 — Regression gate and records:** validate the packed artifact in CI and record the finding/fix. **Accept:** the pack job runs the package README validator, it rejects missing metadata or content, and `roadmap/check.py` passes; CI is green before the stage is closed.

## Exit evidence
Record the PR and CI run in the `ROADMAP.md` status cell. State explicitly that this fixes future packages and does not alter the already-published 0.1.0 artifact.
