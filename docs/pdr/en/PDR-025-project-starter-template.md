# PDR-025 — Project starter: the `Naravel.Templates` template pack

**Native-first verdict:** the .NET SDK already has the mechanism for "one command to a new project": a `dotnet new` template pack, a NuGet package of package type `Template`, installed with `dotnet new install <package id>` and found by `dotnet new search`. No tool has to be built for that. What does not exist is the content. Today a Naravel project is `dotnet new web`, then `dotnet add package`, then a `Program.cs` written by hand from the guides: there is no folder structure, no sample configuration and no generator. **Proposed outcome: accept, as a template pack and not as a command-line tool.** The pack carries the starter application and a few item templates; it adds no runtime API and no dependency to any existing package.

Status: **Approved 2026-11-14** by merging it (PR #285; see *Approval*). The approach, the defaults and the release were agreed in the session of 2026-11-14; this document is the written decision. Stage R34 starts from this approval.

## Context

What a Laravel user gets in one step, and what a Naravel user has today:

| Laravel | Naravel today | After this PDR |
|---|---|---|
| `laravel new blog` (the installer, once per machine) or `composer create-project laravel/laravel blog` | `dotnet new web -n Blog`, `dotnet add package Naravel.Cache`, then register by hand ([getting-started](../../en/getting-started.md)) | `dotnet new install Naravel.Templates` once, then `dotnet new naravel -n Blog` |
| A fixed structure: `app/`, `config/`, `routes/`, `resources/`, `storage/`, `tests/` | none; the repository's `samples/Naravel.Sample` has the shape but is never packed and carries the demos of every module | the same shape, without the demos |
| `config/*.php`, one file per module, with every driver listed | one `appsettings.json` that the guide for each module shows in pieces | `config/*.json`, one file per module, the chosen driver active |
| `.env` and `php artisan key:generate` | environment variables and user secrets (native); no application key | the same, written into the generated README |
| `php artisan make:job`, `make:event`, `make:listener`, `make:mail`, `make:controller` | write the class by hand | `dotnet new naravel-job -n SendWelcome` and the other item templates |
| `php artisan serve`, `php artisan test` | `dotnet run`, `dotnet test` | the same, no wrapper |

Facts this decision rests on, each checked in the source or the Microsoft documentation:

- **The mechanism.** A template pack is a project that sets `<PackageType>Template</PackageType>`, includes the template folders as content (`<IncludeBuildOutput>false</IncludeBuildOutput>`, `<ContentTargetFolders>content</ContentTargetFolders>`, `<NoWarn>$(NoWarn);NU5128</NoWarn>`, `<NoDefaultExcludes>true</NoDefaultExcludes>`, the properties of the Microsoft tutorial), and is packed with `dotnet pack`. Each template is a folder with `.template.config/template.json`. `identity` and `shortName` are mandatory; `sourceName` is the name in the source tree that is replaced by the name the user passes with `-n`; `tags.type` is `project` or `item`; a `choice` parameter lists its `choices`; content is made conditional with `#if (symbol)` blocks (with `#else` and `#endif`), written as a `#if` directive in C# files, as `//#if` in JSON and as `<!--#if (symbol) -->` in XML and MSBuild files; a choice is compared as `symbol == "value"`. Sources: [tutorial: create a template package](https://learn.microsoft.com/en-us/dotnet/core/tutorials/cli-templates-create-template-package), the [template.json reference](https://github.com/dotnet/templating/wiki/Reference-for-template.json) and [conditional processing](https://github.com/dotnet/templating/wiki/Conditional-processing-and-comment-syntax) of the `dotnet/templating` project. The exact spelling of each construct is proved by the CI job of this PDR, not by this text.
- **JSON configuration accepts comments.** The configuration JSON parser reads with `CommentHandling = Skip` and `AllowTrailingCommas = true`, and it throws on a key that appears twice ([source](https://source.dot.net/Microsoft.Extensions.Configuration.Json/JsonConfigurationFileParser.cs.html)). A `config/*.json` file can therefore carry comments, and a driver can be shown only as a comment, never as a second copy of a key.
- **Source order decides which value wins.** `WebApplication.CreateBuilder` adds `appsettings.json`, `appsettings.{Environment}.json`, user secrets (Development), environment variables and the command line, in that order, and a source added later overrides an earlier one. A `config/*.json` file added after them would override an environment variable and a user secret. `samples/Naravel.Sample/Program.cs` already works around this for environment variables by calling `AddEnvironmentVariables()` again after `AddJsonFile`.
- **The queue `database` driver needs an EF Core context.** `AddDatabaseDriver<TContext>(configuration)` is generic over the context, and the registration table in `getting-started.md` says the driver also needs `ConfigureQueueJobs()` model mapping. A starter that has no EF Core context cannot offer it.
- **The query builder has no migrations.** `Naravel.Database` is a query builder; "EF Core stays the tool for entities and migrations" (`database.md`, PDR-022). The starter therefore creates no `database/` folder and no schema tooling.
- **Naravel has no view engine and no command-line host.** Views are static HTML or plain ASP.NET Core; PDR-017 declined an Artisan-style command layer. This PDR adds neither.
- **What the release machinery sees.** Every packable project under `src/` is a library with an API baseline, analyzers and an entry in the install-all consumer smoke; the guards listed in *Where the template meets the existing guards* find packages by looking under `src/`. A template pack has no assembly. It fits none of those rules and must be handled on purpose.

## Proposed quickstart (S1)

```sh
dotnet new install Naravel.Templates        # once per machine
dotnet new naravel -n Blog                  # a working app in ./Blog
cd Blog
dotnet run                                  # prints the URL it listens on; / is the welcome page, /up the health check
dotnet test tests/Blog.Tests                # the generated tests
```

```sh
dotnet new naravel -n Blog --database postgres --cache redis --queue redis
dotnet new naravel-job -n SendWelcome -o app/Jobs       # a job class in the right folder; the other item templates work the same way
```

The default needs the .NET 10 SDK and nothing else: no database server, no Redis, no broker, no account (see D4).

## Options

| | Option | What the user installs | Cost to Naravel |
|---|---|---|---|
| **A (recommended)** | A `dotnet new` template pack, `Naravel.Templates`, with one project template and a few item templates | one NuGet package, one command | a content-only package and its CI proof; no code is shipped to applications |
| B | A global tool, `naravel new`, that calls the same templates | a .NET tool package, then the command | everything of A, plus a tool host, tool packaging and a second command surface to keep stable; PDR-017 declined a command layer for the framework |
| C | A GitHub template repository ("use this template") | nothing to install; a repository copy | no version pin to the packages, no test per release, no item templates, no `dotnet` entry point; the copy goes stale at once |
| D | Document the steps only (the status quo) | `dotnet new web` and the guides | none; the starter stays a page of instructions, and the structure stays an accident of each project |

Why A: it is the native way, it needs no tool and no code that applications load, the template can be built and started in CI from the packed packages exactly as a user would, and it is the base on which B can still be added later (the tool would call `dotnet new`). C and D leave the version pin and the proof out.

## Decision (option A)

**D1 — Distribution.** One new package, `Naravel.Templates`, package type `Template`, short names `naravel` (the project) and `naravel-<kind>` (item templates). It is versioned in lock-step with every other package (`VersionPrefix` of `Directory.Build.props`), so `Naravel.Templates` 0.3.0 generates an application that uses the 0.3.0 packages. The package id is not checked on nuget.org from the authoring environment (no access); the first publish reserves it.

**D2 — Where the project lives.** `templates/Naravel.Templates/`, not under `src/`. The template content is a tree of files under `templates/Naravel.Templates/content/`. `Naravel.Templates.csproj` is the only project; it sets `IncludeBuildOutput` to false, `EnableDefaultItems` to false (no source file of the content may compile into it) and `IncludeSymbols` to false (nothing to symbolize), is listed in `Naravel.slnx` under a `/templates/` folder so that `dotnet pack Naravel.slnx` and the publish job pack it, and is never referenced by another project. Reasons for not putting it under `src/`: the API baseline, the analyzers and the "install every package into one project" consumer smoke are for libraries.

**D3 — The generated application.** The seed is `samples/Naravel.Sample` without the demos. For `-n Blog`:

```text
Blog/
├── Blog.csproj              net10.0, Microsoft.NET.Sdk.Web, Naravel.* at one exact version, <Compile Remove="tests/**" />
├── Program.cs               about twenty lines: load config, register the modules, UseNaravelRouting, map routes
├── .gitignore               bin/ obj/ storage/*  (storage/.gitkeep stays)
├── README.md                what the folders are, how to change a driver, where secrets go
├── app/
│   ├── Http/Controllers/    WelcomeController
│   ├── Jobs/                SendWelcomeEmail
│   ├── Events/              UserRegistered
│   ├── Listeners/           LogRegistration
│   └── Mail/                WelcomeMail
├── config/                  app.json, cache.json, database.json, logging.json, mail.json, queue.json
├── routes/web.cs            the route map, with the welcome page and /up
├── resources/views/         welcome.html
├── storage/                 queue files, the SQLite database file; git-ignored
└── tests/Blog.Tests/        xUnit; the examples above under test with the fakes of the packages
```

The tests project sits inside the application folder, so the application project removes `tests/**` from its own compile items; without that line the SDK's default globbing compiles the test sources (and their `obj` files) into the application. A folder is created only when the template puts something in it: there is no empty `bootstrap/` and no `database/` (see Context).

The default module set is Routing, Cache, Queue (with the worker), Events, Mail and Database. Notifications, Encryption, Scheduling, Broadcasting and Filesystem are not in the starter; the registration table of `getting-started.md` is how they are added, and the generated README links it. Adding them to the template is a separate, later change that needs no new decision beyond this table.

`/` serves a static HTML page through plain ASP.NET Core (`Results.File`, as the sample does). `/up` is `AddHealthChecks()` and `MapHealthChecks("/up")`, native to ASP.NET Core; it checks nothing that needs a server, so it answers on a machine that has none.

**D4 — Defaults need no external service; parameters change them.** The default application uses the memory cache, the `log` mail transport, the `file` queue under `storage/` and a SQLite file under `storage/`. Parameters (working names; the docs follow whatever `dotnet new` accepts):

| Parameter | Values (first is the default) | Packages and calls it adds |
|---|---|---|
| `--database` | `sqlite`, `postgres`, `mysql`, `sqlserver`, `none` | `Naravel.Database` + `Naravel.Database.Sqlite`, `.Postgres`, `.MySql` or `.SqlServer`; `AddNaravelDatabase(configuration)` and the dialect's `AddNaravel...Database(configuration)` |
| `--cache` | `memory`, `redis` | `Naravel.Cache` (+ `Naravel.Cache.Redis`); `AddNaravelCache(configuration)` (+ `AddNaravelRedisCache(configuration)`) |
| `--queue` | `file`, `memory`, `redis`, `rabbitmq`, `kafka` | `Naravel.Queue` + the driver package; `AddQueue(configuration)` and the driver's `Add...Driver(configuration)`; the worker is registered for the chosen connection |
| `--mail` | `log`, `smtp` | `Naravel.Mail` (+ `Naravel.Mail.Smtp`); `AddNaravelMail(configuration)` (+ `AddNaravelMailSmtp(configuration)`) |
| `--no-tests` | off | leaves out `tests/` |

The queue `database` driver is not a choice (needs an EF Core context, see Context). Every parameter value changes three things together: the `PackageReference` lines, the registration lines in `Program.cs` and the active section of the matching `config/*.json`. The generator never emits a half-wired combination.

**D5 — Configuration style.** One JSON file per module under `config/`, each with the chosen driver active, the keys that driver reads, and one comment line that links the module's guide for the other drivers. The alternatives are not pasted in as comments: a pasted alternative is text that nothing builds or starts, so it would rot; the matrix of D9 emits and exercises each active choice instead. Loading order is a requirement, not a code listing: **config files, then user secrets (Development), then environment variables, then the command line**, the order ASP.NET Core documents for `appsettings.json`. The proof is a generated test, `Environment_variables_override_config_files`.

**D6 — Secrets and keys.** Environment variables (`Cache__Stores__redis__ConnectionString`) and `dotnet user-secrets`, as for any ASP.NET Core application. No `.env` file and no `.env` loader (a new dependency or a custom parser for something the configuration system already does), and no `key:generate`: the starter has no application key. Encryption is not in the starter; when it is added, `Naravel.Encryption` uses ASP.NET Core Data Protection and its key ring (OD-03), and the generated README says where the keys must live in production.

**D7 — Item templates.** `naravel-job`, `naravel-event`, `naravel-listener`, `naravel-mailable` and `naravel-controller`, each for a module that is in the default set, so each generated file compiles in a default application. The user chooses the folder with `-o`; the namespace comes from the project when the template engine can bind it (its project-context binding) and from a `--namespace` parameter otherwise; T05 of the stage picks whichever the smoke proves, and the command in the docs does not change. `make:command` is not ported (PDR-017); `make:notification`, `make:model` and the like wait for their modules to be in the starter.

**D8 — Versions and pins.** The version of the `Naravel.*` references in the generated project is written in `template.json` once, and nowhere else. A new script, `.github/scripts/check-template-pins.py` (working name), fails the `pins` job when that value is not the `VersionPrefix` of `Directory.Build.props`, and when the versions of `xunit`, `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` in the tests project differ from `Directory.Packages.props`. Those three are the only packages the starter references directly that are neither Naravel's nor part of the ASP.NET Core shared framework; they are test-only, are approved by this PDR for the generated tests project, and match the versions the repository's own tests use (xunit stays on the 2.9.3 line, see the note in `Directory.Packages.props`). The generated tests use the fakes of the packages (`QueueFake`, `MailFake`, `EventFake`, `CacheFake`) and need no HTTP host, so `Microsoft.AspNetCore.Mvc.Testing` is not added.

**D9 — CI is the gate.** There is no .NET SDK in the authoring environment, so nothing here is run before CI. A new job, `template-smoke`, in `ci.yml` and in `ci-ok`'s `needs` (and so, through `check-publish-gates.py`, in the publish job's `needs`):

1. packs `Naravel.Templates` and every other package to one directory, installs the template from that `.nupkg` (`dotnet new install <file>`), and checks that `dotnet new list naravel` shows it;
2. generates the application into a directory outside the repository, with a `nuget.config` that takes `Naravel.*` from the packed directory (the mapping `consumer-smoke.py` already writes), so the repository's `Directory.Build.props` and `Directory.Packages.props` do not apply and the packages are the packed ones, not project references;
3. for every parameter value once, and for a few combinations, runs `dotnet build`, and `dotnet test` when it has tests;
4. for the default application only, runs `dotnet publish`, checks that the output has `config/*.json` and `resources/views/welcome.html`, starts the published app, waits for `/up`, and checks that `/up` answers 200 and `/` answers 200 with HTML;
5. for each item template, generates the item into the default application and builds it.

A combination other than the default is built and tested but not started, because it would need its server; that is a limit of R34, recorded here and in the docs, not a claim. After a release, `package-smoke.yml` gets the same steps against the version on nuget.org.

**D10 — Release.** The template is the 37th packable package and ships with the next minor, 0.3.0 (new package id, so a minor step, as for `Naravel.Routing.Cache` in 0.2.0; OD-09 allows it before 1.0). The release is a separate stage, as R33 was; stage R34 ends with the template merged, proved by CI and documented, and unreleased. The owner checks that the nuget.org publishing policy covers the new package id before the tag, the step R33 had for `Naravel.Routing.Cache`.

## Where the template meets the existing guards

Verified by reading each script and workflow; each row is a task of the stage.

| Script or file | What it does today | What the template needs |
|---|---|---|
| `check-package-versions.py` | reads every `*.csproj` of the repository; rejects an inline `Version` on a `PackageReference` and a package missing from `Directory.Packages.props` | the starter's csproj files must be skipped (they pin versions on purpose, for a user who has no central file) and checked by `check-template-pins.py` instead |
| `check-package-readmes.py` | expects one `.nupkg` for each `src/*/*.csproj`, with the README its project declares; any other `.nupkg` in the directory is an error | a rule for the template package and its page `docs/nuget/Naravel.Templates.md` (absolute links only), with a self-test |
| `check-package-metadata.py` | requires description, MIT, URLs, at least four tags, the copyright line of `LICENSE`, release notes that link the release of that version, and the icon iff `build/icon.png` exists | `Directory.Build.props` sets copyright, tags, release notes and the icon only for projects under `src/`, so the template project sets them itself, or the condition is widened; the script itself does not change |
| `check-tag-version.py` | every `.nupkg` must end in `.<version>.nupkg` | nothing; `VersionPrefix` already applies |
| `consumer-smoke.py` | installs every package under `src/` into one project | nothing; a template pack is not a library and stays out of that list |
| `check-publish-gates.py` and `ci.yml` | the publish job must wait for every job `ci-ok` waits for | `template-smoke` is added to both lists |
| `roadmap/check.py` | stage ids, the parity matrix, the default order, `NEXT` | `EXPECTED_IDS` gains R34, the parity matrix a section R34 |
| `.github/dependabot.yml` | watches the repository root | the starter's three test-package pins are not seen by it; `check-template-pins.py` is what keeps them in step |

## Approval

On 2026-11-14 the owner asked for Naravel to be installable like Laravel, "with one command, with an initial structure and sample configuration", and approved the proposal to build it as a `dotnet new` template pack, with these answers to the three open questions: **`dotnet new` first, a command-line tool only later and only on real need**; **the default application needs no external service**; **the release is 0.3.0**. The owner asked for the work to continue with the greatest care. Merging this PDR is the approval of what is written here, in particular of D3 (the structure and the default module set), D4 (the parameters), D8 (the three test packages) and D10.

## Not decided here

A `naravel new` tool, a `Naravel.Console` package or an Artisan-style command layer (PDR-017 stays as it is); starter kits with authentication (Breeze or Jetstream style); Notifications, Encryption, Scheduling, Broadcasting and Filesystem in the starter; the queue `database` driver as a choice; Docker or `docker-compose` files and a CI workflow for generated applications; migrations and seeders; a `.env` loader; any change to the public API of an existing package.

## Verification required

| Proof | Where |
|---|---|
| The packed template installs and `dotnet new list naravel` shows it | `template-smoke`, step 1 |
| Every parameter value, alone and in a few combinations, restores, builds and passes its generated tests against the packed packages only | `template-smoke`, steps 2 and 3 |
| The published default application starts, `/up` answers 200, `/` answers 200 with HTML, and `config/*.json` and the welcome page are in the publish output | `template-smoke`, step 4 |
| An environment variable overrides a value of `config/*.json` | generated test `Environment_variables_override_config_files` |
| The generated tests project is not compiled into the application | step 3 (a duplicate-attribute or ambiguity error would fail the build) |
| Each item template builds inside the default application | `template-smoke`, step 5 |
| The template's version equals `VersionPrefix`, and the test-package pins equal `Directory.Packages.props` | `check-template-pins.py` with `--self-test`, in the `pins` job |
| The template package passes the README, metadata and tag-version checks | the pack steps of `fast`, and the self-tests of the changed scripts |
| After the release, the published template generates and builds from nuget.org | `package-smoke.yml`, run by the owner |

Build, tests and the template run are verified by CI only; the authoring environment has no .NET SDK and no NuGet access.

## Alternatives considered

1. **A global tool, `naravel new`.** Rejected for now: it needs the same templates plus a tool host and a stable command surface, and PDR-017 declined a command layer for lack of value over `dotnet` itself. It can be added later on top of the pack without changing the pack.
2. **A GitHub template repository.** Rejected: no version pin, no test per release, no item templates, and the copy diverges from the packages at once.
3. **Ship the sample project as the starter.** Rejected: it references the projects of the repository, not the packages, and carries the demos of every module.
4. **Offer every driver as a commented alternative in each config file.** Rejected in D5: the text is neither built nor started, so it rots. The guide of each module lists the drivers and the matrix exercises the chosen ones.
5. **A `.env` file with a loader and `key:generate`.** Rejected in D6: it adds a dependency or a parser for what environment variables and user secrets already do, and Naravel has no application key to generate.
6. **Put the template project under `src/`.** Rejected in D2: every guard that scans `src/` would apply library rules (an API baseline, analyzers, the install-all smoke) to a package that has no assembly.
7. **Floating `Naravel.*` versions in the generated project.** Rejected: pre-1.0 packages are built and tested together and a floating reference can resolve a mix that was never tested.
