# Contributing to Naravel

Thank you for helping. Naravel is a .NET port of the *ideas* of Laravel, not a line-by-line clone, so most of the work is
deciding what is worth porting and then doing it small and well. This guide is the short path; the binding rules are in
[AGENTS.md](AGENTS.md) and [ROADMAP.md](ROADMAP.md). Persian: [CONTRIBUTING.fa.md](CONTRIBUTING.fa.md). Everyone who takes part follows the [Code of Conduct](CODE_OF_CONDUCT.md).

## Ways to help

| You found | Do this |
|---|---|
| A bug | Open a [bug report](https://github.com/km-arc/Naravel/issues/new?template=bug_report.yml). The form asks for the package, the version and a minimal reproduction; a failing test is the best reproduction. |
| A missing or wrong Laravel-style feature | Open a [feature request](https://github.com/km-arc/Naravel/issues/new?template=feature_request.yml) **before** writing code. A new Laravel equivalent needs a PDR first (see below), and a large one needs owner approval. |
| A security problem | Do **not** open a public issue. Follow [SECURITY.md](SECURITY.md). |
| A typo, a broken link, an unclear sentence | A pull request is welcome without an issue. Docs exist in English and Persian; fix both or say which one you could not. |
| A question | Open an issue and say what you tried. Questions that expose a missing line in the docs become docs fixes. |

## Before you write code

1. **Rule zero.** Implement a Laravel feature only if it brings value in .NET. If .NET already solves the problem
   idiomatically (sessions, password hashing, route authorization), the right change is a documented recipe, not a package.
2. **PDR first.** A new Laravel equivalent starts as a Parity Decision Record in [docs/pdr/en](docs/pdr/en) and
   [docs/pdr/fa](docs/pdr/fa): Laravel feature, native .NET option, value added, decision, rejected alternatives.
3. **Read what applies.** [AGENTS.md](AGENTS.md) lists the hard rules (one-way dependencies, `Manager<TDriver, TOptions>`
   for driver modules, async with `CancellationToken`, no CLR type resolved from stored data, no new dependency beyond
   `Microsoft.Extensions.*` without a PDR). [docs/en/module-authoring-guide.md](docs/en/module-authoring-guide.md) walks
   through a new module. [ROADMAP.md](ROADMAP.md) is the only place for a stage's status; do not copy a status or a test
   count into another file (`roadmap/check.py` fails on it).

## Set up

You need the .NET 10 SDK; `global.json` pins the feature band so your compiler matches CI. Or open the repository in the
dev container (`.devcontainer/`, VS Code or Codespaces).

```sh
git clone https://github.com/km-arc/Naravel.git
cd Naravel
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

Everything is one repository and one solution (`Naravel.slnx`). Do not create a second solution, a per-module workflow or
a copy of the shared build files. NuGet versions live only in `Directory.Packages.props`; target framework, package
metadata and the lock-step version live only in `Directory.Build.props`.

## Making a change

Keep a pull request to one concern. Branch from `main` and name the branch for the kind of change (`fix/...`, `feat/...`,
`docs/...`, `test/...`, `ci/...`).

A change is ready when:

- **It builds clean.** Warnings are errors for everything under `src/`, including the trim/AOT analyzers and the missing
  XML comment check (`CS1591`). Public types need XML docs with the purpose, the *Laravel equivalent*, *why it exists* and
  *what was intentionally not ported*.
- **New public API is declared.** `Microsoft.CodeAnalysis.PublicApiAnalyzers` fails the build until each new public member
  is listed in that project's `PublicAPI.Unshipped.txt` (the IDE quick fix adds the line). Removing or changing a public
  member of a released package is a breaking change: say so in the pull request and in `CHANGELOG.md` (Naravel is 0.x, so
  it is allowed when listed, OD-09).
- **It has tests.** xUnit and FluentAssertions 7.x (8.x and later are commercially licensed, so the repository stays on
  7.x). A driver module also needs a runtime `Extend` test, a config-reload test when it is config-driven, a contract test
  for every driver and a fake. Tests that need a real Redis, RabbitMQ, Kafka or Memcached are gated on the
  `NARAVEL_TEST_*` variables below and show up as skipped, never as passed, when the service is missing.
- **A new package has its metadata.** A `<Description>` (at least 40 characters) and `<PackageTags>` in its `.csproj`, and its
  package page `docs/nuget/<PackageId>.md` packed as `README.md`; the rest of what NuGet shows comes from
  `Directory.Build.props`. CI checks every packed package (`.github/scripts/check-package-metadata.py` and
  `check-package-readmes.py`), because NuGet cannot edit a version once it is pushed.
- **Docs are in both languages.** A change to behaviour updates `docs/en/*` and `docs/fa/*` together. Put the *reason* for
  a decision in the docs or the commit message, not only the decision.
- **`CHANGELOG.md` has a line under `[Unreleased]`** for anything a user of a package could notice.
- **The roadmap check passes:** `python3 roadmap/check.py`.
- **You say what you did not run.** If you could not run `dotnet build`, `dotnet test` or a provider's service, write
  `NOT RUN: <reason>` in the pull request. CI is the verification, and an unrun test is never described as passing.

### Commits and pull requests

Commit subjects follow `type(scope): summary` in the imperative, for example `fix(queue): never leave a reserved message
behind` or `docs(roadmap): close D-76`. Types in use: `feat`, `fix`, `docs`, `test`, `build`, `ci`, `chore`. The body says
why; the diff already says what.

Open the pull request against `main`. The pull request template asks for the same things as the list above. `main` is
protected: the aggregate check **`ci-ok`** must pass and the branch must be up to date (see
[.github/BRANCH-PROTECTION.md](.github/BRANCH-PROTECTION.md)). Follow-up commits are better than force-pushes while a
review is open.

AI agents contributing to this repository follow [AGENTS.md](AGENTS.md) like any other contributor.

## Testing

Run the solution checks with the .NET 10 SDK:

```sh
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

To work on one module, run its test project, for example `dotnet test tests/Naravel.Cache.Tests -c Release`.

### Provider integration tests

Start the local services with `docker compose up -d --wait`. Set the same service endpoints used by CI, then run the
solution tests so environment-gated provider cases execute:

```sh
NARAVEL_TEST_REDIS=localhost:6379 \
NARAVEL_TEST_RABBITMQ='amqp://naravel:naravel@localhost:5672/%2f' \
NARAVEL_TEST_KAFKA=localhost:9092 \
NARAVEL_TEST_MEMCACHED=localhost:11211 \
dotnet test Naravel.slnx -c Release
```

Stop the services with `docker compose down` when finished.

### One-command local run (same as CI)

`scripts/test-local.sh` (Linux/macOS) or `scripts/test-local.ps1` (Windows) starts the services, exports the
`NARAVEL_TEST_*` variables, then restores, builds and tests the whole solution, so a CI failure can be reproduced
before pushing:

```sh
scripts/test-local.sh                                   # services + all tests
scripts/test-local.sh --down                            # ...then stop the services
scripts/test-local.sh --no-services                     # fast loop, provider tests skipped
scripts/test-local.sh --db                              # also MySQL, PostgreSQL, SQL Server (about 2 GB RAM)
scripts/test-local.sh --db -- --filter "Dialect=Postgres"   # one database dialect only
scripts/test-local.sh -- --filter "FullyQualifiedName~Redis"
```

### Check the packed packages like a user

Every test project here references Naravel through `ProjectReference`, so nothing else installs a packed `.nupkg`. CI does:
`consumer-smoke.py` restores every packed package into a consumer outside the repository, loads every Naravel assembly and runs the
`samples/Naravel.AotSmoke` program against the packages, so a missing dependency or a framework reference that does not flow shows up
before a version is pushed (a pushed version cannot be edited). Locally:

```sh
dotnet pack Naravel.slnx -c Release -o ./nupkgs-check
python3 .github/scripts/consumer-smoke.py --local ./nupkgs-check
```

To check a version that is already on nuget.org, run the "Package smoke (published version)" workflow with that version
(`gh workflow run package-smoke.yml -f version=0.1.1`) or `python3 .github/scripts/consumer-smoke.py --nuget 0.1.1`.

### Dev container

Open the repository in VS Code or Codespaces ("Reopen in Container") for a ready .NET 10 SDK with Docker access
(`.devcontainer/`). `global.json` pins the SDK feature band, so local and CI builds use the same compiler.

## License

Naravel is released under the [MIT License](LICENSE). Contributions are accepted under the same license.
