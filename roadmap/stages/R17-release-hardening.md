# R17 — Release hardening and re-audit

Parity section: `R17` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: owner approvals for release scope and any breaking/API/dependency change (OD-09 ends here).
Read: `roadmap/AUDIT.md`, `roadmap/PARITY-MATRIX.md`, all `docs/pdr/en/*`, `Directory.*.props`, `CHANGELOG.md`, `.github/workflows/ci.yml`.
Touch: metadata, docs, analyzers config, CI, samples; code only to resolve audit findings.
Out of scope: new features (a feature found missing becomes a backlog item).
Decisions: OD-04 revisit (Validation); OD-09.

## Tasks
- [x] **R17.T01 — Audit:** reconcile public APIs, parity claims, PDR decisions, package references and every open `D-nn`; resolve or explicitly defer each; triage `BACKLOG.md`. **Accept:** AUDIT has zero open rows without an owner/defer note. Evidence: AUDIT reconciled 2026-10-26: 5 closed, 6 deferred with owner notes and BACKLOG rows.
- [x] **R17.T02 — Compatibility:** lock-step version, package metadata, `EnablePackageValidation`, analyzers + XML-doc enforcement (remove global `CS1591` suppression for `src` if feasible), trimming/AOT annotations where cheap, benchmarks baseline re-run. **Accept:** `dotnet pack` for all packable projects succeeds with validation on. Evidence: Lock-step 0.1.0-preview, package validation and the public API analyzers were already on; CI now packs every packable project (run 40279380581). The `CS1591` suppression stays (D-17 deferred); trimming/AOT annotations and the benchmark baseline re-run are deferred (no SDK here).
- [x] **R17.T03 — Supply chain:** restore/build/test, NuGet vulnerability audit, full CI matrix with services; record exercised vs unavailable checks. **Accept:** evidence listed in the status cell. Evidence: CI vulnerability audit now fails the build on any finding; the three-OS matrix, services job and three database legs are green (run 40279380581). NOT RUN: live S3, live Kafka/RabbitMQ beyond the services job.
- [ ] **R17.T04 — Release:** changelog, release notes, status table. **Accept:** version bumped once in `Directory.Build.props`; no scope expansion.
