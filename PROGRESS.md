# PROGRESS

Historical record of approved decisions and completed work. The authoritative current status and sole
`NEXT` pointer are now in [ROADMAP.md](ROADMAP.md); this file preserves the prior stage history and
decisions. Begin new roadmap work there.

Legend: `[x]` done · `[ ]` not started · `[~]` in progress

---

## Owner-approved decisions (do not re-ask, do not re-litigate)

1. **PDR-005 (config keyword):** every module's config section uses the literal key `"Stores"`.
   `Naravel.Queue`'s `"Connections"` must be renamed to `"Stores"` as part of PDR-006. Written:
   `docs/pdr/en/PDR-005-single-config-keyword-stores.md` (+ `docs/fa/`).
2. **Cache source & scope:** port from [km-arc/LaravelCacheNet](https://github.com/km-arc/LaravelCacheNet)
   (owner's own repo). **Exclude** its `Queue`/`IQueueStore`/`Manager/QueueManager.cs` entirely -
   redundant with `Naravel.Queue`. **Keep**: `ICacheStore` + `MemoryCacheStore`/`RedisCacheStore`/
   `MemcachedCacheStore`, `TaggedCacheStore` (tag-versioning technique), `ScopedCacheStore`,
   `ICacheLock` + `MemoryLock`/`RedisLock`/`MemcachedLock`.
3. **Cache package split:** `Naravel.Cache` (core: contracts, manager, array/memory driver, tagging,
   scoping, memory lock) + `Naravel.Cache.Redis` + `Naravel.Cache.Memcached` (heavy deps opt-in),
   mirroring the `Naravel.Queue` / `Naravel.Queue.Redis` pattern.
4. **PDR numbering, fixed:**
   - PDR-005: config keyword (`Stores`) - **written**, Stage 0.
   - PDR-006: Queue migration onto `Manager<IQueueDriver, QueueOptions>` - **written and implemented**, Stage 1.
   - PDR-007: Cache port - **accepted and implemented** (Stage 3; 27 tests).
   - PDR-008: Filesystem full Foundation migration - **approved and implemented 2026-10-04; verified** (Stage 2).
5. **Working style:** complete one approved stage at a time (code + tests + docs), update this file and stop.
6. **Historical sequence:** Stage 2 (Filesystem) and Stage 3 (Cache) are complete. Current cross-project sequencing is in [ROADMAP.md](ROADMAP.md).

7. **Routing + HTTP Middleware (PDR-009):** implementation history remains in `PROGRESS-ROUTING.md` (stages 4.0/4a/4b/4c); current priority is
   tracked in [ROADMAP.md](ROADMAP.md) (R05). It is independent of
   Stages 1-3 below. Architecture, package split and the three PDR sign-off items are owner-approved.

---

## Documentation audit (2026-10-04) - DONE (docs only, nothing compiled or run)

- [x] Historical audit reconciled Queue/Filesystem status and parity notes. Cache was implemented later under accepted PDR-007; the follow-up audit below updates those records to the current state.
- [x] Test counts are now **counted from source** (`[Fact]` + `[Theory]` x `[InlineData]`): Foundation 58, Queue 30, Filesystem 7 (docs said 6), Routing 64.
      This was the Stage 0 count; Stage 2 added 13 Filesystem tests. The latest full run passed 172/172 (Filesystem 20).
- [x] PDR-007 (Cache) was accepted and implemented; PDR-008 (Filesystem) was approved and implemented. PDR-005 has an implementation-status note.
- [x] `docs/helpers.md` turned into a bilingual troubleshooting page; PDR reading lists in `AGENTS.md` and the authoring guide include PDR-005, 006, 009.
- [x] Release hygiene: `bin/` and `obj/` were present in the received archive and are removed from the delivered one (`.gitignore` already covers them).
- [ ] **Still open:** package version review and tests for Queue's Redis/Database/RabbitMQ/Kafka drivers. Cache implementation and verification are complete; live Redis/Memcached integrations were not run.

---

## Verification and dependency hygiene (2026-10-04)

- [x] **Verified by the owner, first run (before the dependency edits below):** `dotnet test` 159/159 passed; `dotnet build Naravel.slnx -c Release` built all 16 projects
      cleanly (incl. Redis/Database/RabbitMQ/Kafka drivers and the sample). This closes the "Not run" items of Stage 0 (Filesystem tests),
      Stage 1 (Queue) and Routing Stage 4a. Note: `dotnet test` alone only builds projects the tests reference; use `dotnet build Naravel.slnx`
      to compile everything.
- [x] **Correction:** Stage 1 below says `Microsoft.Extensions.Configuration.Memory` had to be added to two test projects. No `.csproj` references it
      and everything builds, because `AddInMemoryCollection` lives in `Microsoft.Extensions.Configuration`. The dead `PackageVersion` entry was removed.
- [x] Edited and **verified by a second run (2026-10-04, fresh restore)**: dependency bumps (Kafka 2.15.1, Redis 2.13.17, Microsoft.* 10.0.10, Test.Sdk 18.0.1), NuGet audit enabled repo-wide,
      machine-specific source removed from `nuget.config`, `Naravel.Foundation` no longer uses the ASP.NET `FrameworkReference`, `TreatWarningsAsErrors`
      removed from Filesystem. Details: `CHANGELOG.md`. `dotnet build Naravel.slnx -c Release` built all 16 projects cleanly; `dotnet test` 159/159 passed; the audited build gave no NU19xx warnings. `dotnet list package --vulnerable/--outdated` printed no table (see CHANGELOG, Known gaps).
- [x] **Stage 2 verification (2026-10-04):** after the Filesystem migration, `dotnet build Naravel.slnx -c Release` built all 16 projects and `dotnet test Naravel.slnx -c Release` passed 172/172, including 20 Filesystem cases. Live AWS/S3 integration was not run.
- [ ] Open, need owner approval: RabbitMQ.Client 7.x (driver rewrite: `IModel`/`BasicGet` -> `IChannel` async), AWSSDK.S3 4.x (with PDR-008),
      StackExchange.Redis 3.x.

---

## Stage 0 - Filesystem emergency security patch (THIS STAGE: DONE)

- [x] **Critical fix:** `LocalStorageDriver.GetFullPath` had a path-traversal vulnerability
      (`../../etc/passwd` style inputs resolved outside the configured root - arbitrary file
      read/write/delete). Patched: the combined path is now run through `Path.GetFullPath` and
      rejected with `UnauthorizedAccessException` unless it stays inside the disk's root.
      File: `src/Naravel.Filesystem/Drivers/LocalStorageDriver.cs`.
- [x] Added `tests/Naravel.Filesystem.Tests` with 7 regression test cases (3 facts + a 4-case theory) (`LocalStorageDriverSecurityTests.cs`):
      relative traversal (`../`, `..\`, nested), an absolute path outside root, and two tests that
      ordinary usage (relative paths, a leading `/`) still works. Added to `Naravel.slnx`.
- [x] Removed stray scaffold left over in the zip: `src/Naravel.Filesystem/src/` (a duplicate,
      empty `Class1.cs` project - looked like an accidental second `dotnet new classlib` run inside
      the module folder) and `src/Naravel.Filesystem/readme.md` (documented an API -
      `options.AddLocalDisk(...)`/`AddS3Disk(...)` fluent builder - that does not exist in the
      code; superseded by real docs in Stage 3 below).
- [x] Wrote PDR-005 (EN + FA).
- [x] Updated `AGENTS.md` module status table for Queue/Filesystem/Cache to reflect the current,
      accurate state (previously said "not started"/"pending review" without detail).
- [x] **Historical note:** the Stage 0 author could not run `dotnet build`/`dotnet test`; its security tests were subsequently included in full-suite verification, most recently 172/172 on 2026-10-04.

### Stage 0 scope note (historical; resolved by Stage 2)
At Stage 0, Filesystem did not use Foundation, still used `DefaultDisk`/`Disks`, had no runtime `Extend` or config reload,
did not dispose the S3 client, and ignored `DiskOptions.Region`. Stage 2 below tracks resolution of those gaps.

---

## Stage 1 - Queue migration onto Foundation (PDR-006) - DONE

- [x] `docs/pdr/en/PDR-006-queue-on-foundation.md` + `fa/` written.
- [x] `QueueManager : Manager<IQueueDriver, QueueOptions>` (sealed, adds only a `Connection(name)`
      alias for `Driver(name)`). `IQueueManager` interface removed.
- [x] `IQueueDriverFactory` removed entirely. New shared helper
      `Naravel.Queue.Extensions.QueueDriverRegistrationExtensions.AddQueueDriver(...)` scans
      `{section}:Stores` at registration time for stores whose own `"Driver"` key matches, and
      registers one `IDriverRegistry<IQueueDriver>` factory per matching store name
      (`AddNaravelDriver<IQueueDriver>`). This is what makes "two stores, same driver type, different
      settings" work (e.g. two independent Redis connections) - see the multi-store test below.
- [x] All six `AddXxxDriver()` methods (`Memory`, `File`, `Redis`, `Database<TContext>`, `RabbitMQ`,
      `Kafka`) now take `(IServiceCollection, IConfiguration, string sectionName = "NaravelQueue")`
      and are implemented via `AddQueueDriver`. Driver *behavior* (including the Stage 0/-1
      crash-recovery and worker-hardening fixes) is unchanged.
- [x] Config: `"NaravelQueue:Connections:name"` → `"NaravelQueue:Stores:name"` everywhere (core,
      every driver package, the sample, the tests).
- [x] `QueueWorkerService.RunLoopAsync`: driver is now resolved from the manager at the top of every
      poll iteration (hot reload takes effect without a restart) and reused for that one message's
      full pop→handle→ack/release/fail cycle (RabbitMQ delivery tags / Kafka offsets are
      instance-scoped, so this must not change mid-message).
- [x] `JobDispatcher` and `QueueWorkerService` now depend on the concrete `QueueManager` class
      instead of the removed `IQueueManager` interface.
- [x] `samples/Naravel.Sample`: queue config uses the new config shape and
      `AddXxxDriver(configuration)` signatures.
- [x] New tests: `tests/Naravel.Queue.Tests/ManagerIntegrationTests.cs` - two stores sharing one
      driver type stay isolated, runtime `Extend` registers an unknown-at-startup driver, a config
      reload that changes a store rebuilds its driver, an unrelated reload does not.
- [x] **Found and fixed two pre-existing, unrelated latent bugs** while touching the test projects:
      `Naravel.Queue.Tests.csproj` and `Naravel.Foundation.Tests.csproj` called
      `ConfigurationBuilder.AddInMemoryCollection(...)` without referencing the
      `Microsoft.Extensions.Configuration.Memory` package that extension method lives in (it is a
      different package from plain `Microsoft.Extensions.Configuration`). Added to both `.csproj`
      files and to `Directory.Packages.props`. **This means these test projects may never have
      compiled before** - verify as part of Stage 1 sign-off.
- [x] `docs/en/queue.md` + `docs/fa/queue.md` written from scratch (config, quick start, 7 worked
      examples, crash recovery, delivery guarantee, limitations, what the tests cover).
- [x] `README.md`/`README.fa.md` Queue sections, both `laravel-parity.md` tables, and the Module
      status table in `AGENTS.md` updated.
- [ ] **Not run:** `dotnet build`/`dotnet test` - same environment limitation as Stage 0. **This
      stage touched the most code so far - verify it before starting Stage 2.** If anything doesn't
      compile, the likely first suspects are: a missed `Connections` → `Stores` rename somewhere, or
      the two `Configuration.Memory` package additions not being enough (double check no other test
      project has the same latent gap - `grep -rn AddInMemoryCollection tests/` and confirm every
      hit's `.csproj` references `Microsoft.Extensions.Configuration.Memory`).

### Explicitly NOT done in Stage 1 (by design)
- Filesystem and Cache are untouched in this stage (that's Stage 2 and Stage 3).
- No integration tests against real Redis/RabbitMQ/Kafka/a real database - still only the shared
  driver contract tests (Memory/File) plus the new Foundation-migration tests (which also only
  exercise the Memory driver, since they're testing *Manager* behavior, not driver behavior).
- The Database driver's "one DbContext per driver, no store/connection column" limitation (documented
  pre-existing) was not fixed - out of scope for "migrate onto Foundation", tracked as a known
  limitation in `docs/en/queue.md`.

## Laravel-inspired sample structure - current status

- [x] Renamed the runnable queue web sample to `samples/Naravel.Sample` and updated its solution entry.
- [x] Organized implemented features under `app/Jobs`, `app/Http/Controllers`, `config`, `routes`,
      and `resources/views`; queue settings are loaded from `config/appsettings.json`.
- [x] Added `app/Events`, `app/Models`, `app/Providers`, `bootstrap`, `database`, and `public` guide files
      to distinguish application structure from framework functionality that Naravel does not yet provide.
- [ ] Implement a general `Naravel.Events` module (event dispatch, listener registration, and queued listeners)
      before adding real application events under `app/Events`. Queue-specific lifecycle events are not a
      general event bus.
- [ ] Add and document database application conventions (models, migrations, factories, seeders) when a
      Naravel database module and its design decision are approved; the current sample has no ORM layer.
- [ ] Add application service-provider and bootstrap abstractions only when they provide value beyond the
      current ASP.NET Core host and dependency-injection setup.
- [ ] Add console commands and scheduling only as a separately scoped, documented feature; the sample does
      not claim Laravel Artisan or scheduler parity.

## Stage 2 - Filesystem full migration onto Foundation (PDR-008) - DONE (verified 2026-10-04)

- [x] `docs/pdr/en/PDR-008-filesystem-on-foundation.md` (+ `fa/`) approved: `IStorageDriver` became
      the `TDriver`, `FilesystemOptions : ManagerOptions` (config key `Stores`, not `Disks`; keep
      the *method* name `Disk()` on the manager for Laravel-authentic reading, per PDR-005's
      resolution). Drivers registered via Foundation's `IDriverRegistry<IStorageDriver>`.
- [x] Migrated the manager and registration to Foundation; removed duplicate factory/manager interfaces.
- [x] Kept disposal optional on `IStorageDriver`; Foundation disposes drivers that implement disposal, including the S3 client.
- [x] Made `DiskOptions.Region` effective for standard AWS regions and S3-compatible endpoint signing.
- [x] Reviewed project settings; no AWS SDK upgrade or new mocking package was added.
- [x] Added local driver contract/security coverage, manager named-store/`Extend`/reload/disposal tests, and S3 config/presigned URL/client-disposal tests (20 focused cases).
- [x] Added complete EN/FA filesystem docs and updated parity, README, PDR and module-status references.
- [x] Full verification: `dotnet build Naravel.slnx -c Release` built all 16 projects; `dotnet test Naravel.slnx -c Release` passed 172/172, including 20 Filesystem tests. Live AWS/S3 integration was not run.

## Stage 3 - Cache port (PDR-007) - DONE

**Status: DONE (verified 2026-10-05).**

- [x] PDR-007 accepted; implemented `Naravel.Cache`, `Naravel.Cache.Redis` and opt-in `Naravel.Cache.Memcached` on Foundation.
- [x] Added cache/lock contracts, managers, Memory/Redis/Memcached providers, scoped/tagged views and Laravel-style `RememberAsync` helpers.
- [x] Fixed first-use tag-version races by using version zero until flush; isolated named Memory stores by default; preserved no-expiry Memcached TTL semantics.
- [x] Memcached now rejects inconsistent per-store server lists rather than silently merging clusters. All Memcached stores share a pooled client; server-topology changes require host restart.
- [x] Updated EN/FA Cache guides, both parity tables, PDRs, README files, `AGENTS.md`, next-agent prompts and this progress tracker.
- [x] Cache tests: 27 passed, including named store/lock isolation, runtime `Extend`, reload, tag stability, prefix-bounded Redis flushing, provider registration and Memcached configuration/TTL behavior.
- [x] Full solution: 20 projects built cleanly; 199/199 tests passed. Live Redis/Memcached integration was not run.

---

## Verify (once NuGet is reachable)

```
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

If this fails with `NU1301`/"Connection refused" on `api.nuget.org`, that is a **network problem on
the machine running the command**, not a code problem - see the CI workflow
(`.github/workflows/ci.yml`), which runs on GitHub's runners and has NuGet access.
