## What and why

<!-- What changes, and the reason. The diff shows the what; the reason is what a reviewer cannot recover later. -->

Closes #

## Kind of change

- [ ] Bug fix
- [ ] New or changed feature (a PDR is linked or added)
- [ ] Breaking change to a released package (listed in `CHANGELOG.md`)
- [ ] Documentation, tests, CI or build only

## Checklist

- [ ] One concern per pull request, branched from the current `main`.
- [ ] Tests added or updated (runtime `Extend` / config-reload / contract tests for a driver module).
- [ ] New public API is declared in `PublicAPI.Unshipped.txt` and has XML docs.
- [ ] Docs updated in English and Persian (`docs/en` and `docs/fa`).
- [ ] `CHANGELOG.md` has a line under `[Unreleased]` for anything a package user could notice.
- [ ] `python3 roadmap/check.py` passes; no status or test count copied outside `ROADMAP.md`.

## Verification

<!-- What you ran and the result. Anything you could not run: write `NOT RUN: <reason>`. CI is the verification; do not describe unrun tests as passing. -->

```
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```
