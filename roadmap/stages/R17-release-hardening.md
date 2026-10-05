# R17 — Release hardening and re-audit

Parity section: `R17` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: owner approvals for release scope and any breaking/API/dependency change (OD-09 ends here).
Read: `roadmap/AUDIT.md`, `roadmap/PARITY-MATRIX.md`, all `docs/pdr/en/*`, `Directory.*.props`, `CHANGELOG.md`, `.github/workflows/ci.yml`.
Touch: metadata, docs, analyzers config, CI, samples; code only to resolve audit findings.
Out of scope: new features (a feature found missing becomes a backlog item).
Decisions: OD-04 revisit (Validation); OD-09.

## Tasks
- [ ] **R17.T01 — Audit:** reconcile public APIs, parity claims, PDR decisions, package references and every open `D-nn`; resolve or explicitly defer each; triage `BACKLOG.md`. **Accept:** AUDIT has zero open rows without an owner/defer note.
- [ ] **R17.T02 — Compatibility:** lock-step version, package metadata, `EnablePackageValidation`, analyzers + XML-doc enforcement (remove global `CS1591` suppression for `src` if feasible), trimming/AOT annotations where cheap, benchmarks baseline re-run. **Accept:** `dotnet pack` for all packable projects succeeds with validation on.
- [ ] **R17.T03 — Supply chain:** restore/build/test, NuGet vulnerability audit, full CI matrix with services; record exercised vs unavailable checks. **Accept:** evidence listed in the status cell.
- [ ] **R17.T04 — Release:** changelog, release notes, status table. **Accept:** version bumped once in `Directory.Build.props`; no scope expansion.
