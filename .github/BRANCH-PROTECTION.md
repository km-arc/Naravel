# Branch protection for `main`

CI is only a gate if GitHub refuses to merge without it. The workflow already ends in one aggregate check, **`ci-ok`**: it
fails when the build, the unit tests (three operating systems), the provider integration tests, the database legs (when
database code changed) or the service-image pin check fail. A filtered-out database leg counts as success; anything
cancelled or missing counts as failure.

## Settings (repository owner; Settings → Branches → Add branch protection rule, branch name `main`)

- Require a pull request before merging.
- Require status checks to pass before merging, and select **`ci-ok`** (the only required check).
- Require branches to be up to date before merging.
- Do not allow force pushes. Do not allow deletions.
- Leave "Do not allow bypassing the above settings" on so the owner cannot merge a red PR by accident.

The same through the CLI:

```bash
gh api -X PUT repos/km-arc/Naravel/branches/main/protection --input - <<'JSON'
{"required_status_checks":{"strict":true,"contexts":["ci-ok"]},
 "enforce_admins":true,
 "required_pull_request_reviews":{"required_approving_review_count":0},
 "restrictions":null,"allow_force_pushes":false,"allow_deletions":false}
JSON
```

Also enable "Automatically delete head branches" (Settings → General) so merged branches do not pile up.

## What each PR reports

Every test job writes a summary (Actions run page) and a check annotation with: tests executed, passed, failed, and the
**skipped tests grouped by reason** (for example an env-gated provider test whose service was not available). A skipped
test is never a pass: it is listed as NOT RUN with its reason. The database legs fail outright if any of their tests
are skipped.

## CI service images

Every service image is an exact version (`redis:7.4.2`, `mysql:8.4.6`, `postgres:17.6-alpine`,
`mcr.microsoft.com/mssql/server:2022-CU19-ubuntu-22.04`, ...). The `pins` job checks `ci.yml` and `docker-compose.yml` (the local
copy of the same images) and fails on a floating tag such as `latest`, `2022-latest` or `17-alpine`. Bump a version deliberately in its own reviewed change.

The same job also runs `.github/scripts/check-publish-gates.py`. Version tags are not covered by branch protection, so the
`publish` job (NuGet) lists every job that `ci-ok` requires in its own `needs:`, the `pins` job included, and `ci-ok` itself:
a floating image tag, a red test leg or a cancelled job on a tag run therefore stops the release before the `nuget-release`
approval is even asked. The script fails the build if the two lists drift apart.

The version comes from `Directory.Build.props`; the tag only names the release. After packing, the `publish` job runs
`.github/scripts/check-tag-version.py <tag> ./nupkgs`, which fails unless every `.nupkg` ends with `.<version>.nupkg` for the
version the tag names (`v0.1.0` means `0.1.0`), before the NuGet login and before anything is pushed. A tag cut from an older
commit or one that disagrees with `VersionPrefix`/`VersionSuffix` (for example `v0.1.0-preview` once the suffix is gone) stops there.
The `pins` job runs the script's `--self-test` on every pull request, because the publish job itself only runs on tags.
