## What and why

<!-- What changes, and the reason. The diff shows the what; the reason is what a reviewer cannot recover later. -->

Closes #

## Kind of change

- [ ] Bug fix
- [ ] New or changed feature (a PDR is linked or added)
- [ ] Breaking change to a released package (listed in `CHANGELOG.md`)
- [ ] Documentation, tests, CI or build only

## Checklist

- [ ] One concern per pull request; default size is at most 500 changed non-generated lines and 20 files, or an owner-approved indivisible/bulk-change exception is explained below.
- [ ] Tests added or updated; no test, assertion, filter, or coverage was weakened to obtain a passing result.
- [ ] New public API is declared in `PublicAPI.Unshipped.txt` and has XML docs; no API/package/config key was assumed or invented.
- [ ] Maintained docs are English-only; no Persian documentation was generated, translated, synced, scaffolded, or published.
- [ ] All affected inbound documentation links were searched across tracked files, including every `docs/nuget/*.md` page, and updated in this change.
- [ ] API names, signatures, defaults, exceptions, provider behavior, compatibility claims, and examples were checked against source and tests; executable examples have test coverage or are explicitly labelled illustrative.
- [ ] Warnings were fixed where possible; every necessary suppression is narrow and has a one-line adjacent explanation.
- [ ] PR description lists source files and tests used as evidence for documentation accuracy, and states tests actually run and not run.
- [ ] `CHANGELOG.md` has a line under `[Unreleased]` for anything a package user could notice.
- [ ] `/usr/bin/python3 roadmap/check.py` and affected local-link checks pass; no status or test count copied outside `ROADMAP.md`.

## Verification

<!-- What you ran and the result. Anything you could not run: write `NOT RUN: <reason>`. CI is the verification; do not describe unrun tests as passing. -->

```
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```
