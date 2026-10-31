# Trimming and Native AOT status

**Naravel's own code is clean under the trim, Native AOT and single-file analyzers (0 warnings across `src/`, enforced by the build since 2026-10-31), but no package declares `IsTrimmable` or `IsAotCompatible` yet.** The expanded smoke sample has now been published with `PublishAot` and run successfully in CI. It covers selected paths across several modules, but the run reports warnings from a reachable dependency; it does not certify every public API, provider, or transitive dependency. This page is the measured history and what is still open, not a promise.

## Current state (2026-10-31)

- **0 distinct warnings in `src/`** (was 126 on 2026-10-28). `Directory.Build.props` turns `EnableTrimAnalyzer`, `EnableAotAnalyzer` and `EnableSingleFileAnalyzer` on for every project under `src/`; with `TreatWarningsAsErrors` a new unannotated reflection-based call fails the build on every pull request. The report-only evidence workflow stays as the way to list everything at once.
- **Expanded Native AOT smoke (2026-10-31, PR #182):** [workflow run 41196158945](https://github.com/km-arc/Naravel/actions/runs/41196158945) published `samples/Naravel.AotSmoke` with `PublishAot` (ILC), then ran the native binary successfully. All **14 smoke checks passed** across Cache, Events, Mail, Queue and its queued adapters, SQLite/database query execution, Encryption, local Filesystem, Scheduling and Pagination. The binary was 15,716 KB. ILC reported **28 distinct warnings, all attributed to Dapper** in the reachable database path. The sample therefore demonstrates successful execution of these exercised paths, not warning-free or blanket AOT compatibility. `aot-smoke.yml` is report-only and is not a merge gate.
- **Reflection stays where it is by design, and it warns at the call site.** These entry points carry `RequiresUnreferencedCode` and `RequiresDynamicCode`, each with a `JsonTypeInfo` or compile-time counterpart: `Encrypter` typed `Encrypt<T>`/`Decrypt<T>`, `AddNaravelManager(services, configuration)` (use `Configure<TOptions>(section)` with the configuration binding generator, then `AddNaravelManager(services)`), `AddJob<TJob>(alias)` and `AddJobsFromAssembly` (use `AddJob(JsonTypeInfo<TJob>)`), `BatchOptions.Then/Catch/Finally` and `BatchCallbackRegistry.Register` without metadata, the Events/Notifications queue overloads without `JsonTypeInfo`, `MapNaravel` route mapping and URL generation by reflection.
- **What is not claimed:** the dependencies are not vouched for (Dapper, EF Core, `AWSSDK.S3`, `Confluent.Kafka`, `RabbitMQ.Client` and others have their own trimming story), and an analyzer-clean library is not the same as a published Native AOT application that runs. `IsTrimmable` and `IsAotCompatible` are set per package only after a `PublishAot` sample that uses it has been built and run in CI.

## How it is measured

`.github/workflows/evidence.yml` builds every project under `src/` with `IsAotCompatible`, `EnableTrimAnalyzer`, `EnableAotAnalyzer` and `EnableSingleFileAnalyzer` set at **restore and build** time (the ILLink analyzer pack is only restored when the properties are set at restore), with warnings kept as warnings. It is report-only and runs on demand (`workflow_dispatch`) and on pull requests that touch it or the benchmarks.

The separate `.github/workflows/aot-smoke.yml` publishes the focused `samples/Naravel.AotSmoke` application with `PublishAot`, reports ILC warnings from the sample and its reachable dependencies, and runs the resulting native binary. It is also report-only; the expanded 2026-10-31 success is linked above.

## Inventory (2026-10-28, run 40759982552)

126 distinct warnings in 18 packages:

| Code | Count | Cause |
|---|---:|---|
| IL2026 | 62 | a member marked `RequiresUnreferencedCode` (almost all `System.Text.Json` reflection serialisation, and configuration binding) |
| IL3050 | 57 | a member marked `RequiresDynamicCode` (the same calls) |
| IL2091 | 6 | a generic argument without `DynamicallyAccessedMembers` (DI helpers) |
| IL2067 | 1 | a `Type` flowing into a parameter that needs annotations |

Largest packages: `Naravel.Queue` 21, `Naravel.Queue.Redis` 18, `Naravel.Queue.Database` 15, `Naravel.Queue.File` 14, then `Naravel.Routing`, `Naravel.Queue.Kafka` 8 each, `Naravel.Queue.RabbitMQ` and `Naravel.Broadcasting` 6 each. About nine in ten are JSON serialisation without a `JsonTypeInfo`, so the fix is the same pattern as `Naravel.Events.Queue`'s source-generated `JsonTypeInfo<TEvent>` overloads (AUDIT D-26): an overload or a registered `JsonSerializerContext` for every payload type, per package.

## What this means today

- Applications that publish with trimming or Native AOT must still verify the exact packages, providers and code paths they use. The historical inventory above is not a current warning count: `src/` is now analyzer-clean, but the expanded smoke run still reports Dapper warnings and does not prove every reachable dependency or runtime path. JIT deployments are not affected.
- The queue envelope (`QueuedMessage`) and the job payloads are the largest area: payloads are serialised by alias through the job type registry, which is reflection based by design (OD-07).
- The expanded smoke now passes its 14 runtime checks, but ILC reports 28 distinct warnings from Dapper. Review the Dapper/database path and other reachable dependencies, continue widening the sample where useful, and set `IsTrimmable` or `IsAotCompatible` only package by package after the relevant warnings and runtime paths are understood.

## Progress

Measured after these two changes (run 40861138651): **120** distinct warnings (from 126); `Naravel.Database` and `Naravel.Encryption` no longer appear in the inventory.

- **Naravel.Encryption:** the two reflection-based overloads, `Encrypt<T>(T)` and `Decrypt<T>(string)`, now carry `RequiresUnreferencedCode` and `RequiresDynamicCode`. Trimmed or AOT applications use the `JsonTypeInfo` overloads, which already existed; the warning now appears at the call site instead of inside the package.
- **Naravel.Database:** JSON scalars (string, bool, numbers) are serialised with source-generated metadata. A non-scalar value for the JSON contains or JSON update methods (MySQL and PostgreSQL) uses the default resolver: unchanged with reflection, and `NotSupportedException` under Native AOT instead of a silent failure.
- **Naravel.Cache.Redis:** `RedisCacheStore` takes optional `JsonSerializerOptions` (`serializerOptions`; through DI, a keyed `JsonSerializerOptions` under `RedisCacheServiceCollectionExtensions.SerializerOptionsKey`). Pass a source-generated resolver for trimming or AOT; an unknown type throws `NotSupportedException`. Without options nothing changes.
- **Naravel.Pagination, Pagination.EntityFrameworkCore, Notifications, Filesystem:** cursor key types use a source-generated context (other types: documented fail-loud fallback), `Expression.Field` takes a `FieldInfo`, DI type parameters carry `DynamicallyAccessedMembers`, and Filesystem binds its options with the configuration binding generator.

Measured after stage 5 (workflow run 40866511798): **107** distinct warnings (from 126), left in 11 packages: `Naravel.Queue` 21, `Queue.Redis` 18, `Queue.Database` 15, `Queue.File` 14, `Routing` 8, `Queue.Kafka` 8, `Queue.RabbitMQ` 6, `Broadcasting` 6, `Events.Queue` 5, `Notifications.Queue` 4, `Foundation` 2 (the generic `Configure<TOptions>(configuration)` binding, which a library cannot compile-time bind).

Stages 6 and 7 and Routing (measured in workflow run 41005059186, on the head of PR #173): **84** distinct warnings (from 126), left in **7** packages: `Naravel.Queue` 21, `Queue.Redis` 18, `Queue.Database` 15, `Queue.File` 14, `Queue.Kafka` 8, `Queue.RabbitMQ` 6, `Foundation` 2. By code: IL2026 41, IL3050 40, IL2091 3.

- **Naravel.Events.Queue, Naravel.Broadcasting, Naravel.Notifications.Queue:** reflection-based overloads carry `RequiresUnreferencedCode` and `RequiresDynamicCode`; the `JsonTypeInfo` overloads are the trim-safe path. Presence records use a source-generated context, and `PresenceMember.FromJson(userId, JsonElement)` builds a member from already parsed JSON.
- **Naravel.Routing:** the reflection-based route mapping and URL generation are annotated; the middleware `Type` parameters and type parameter of `MiddlewareOptions.Alias`, `NaravelRoute.Middleware` and `WithoutMiddleware` carry `DynamicallyAccessedMembers(PublicConstructors)` so the constructors the pipeline needs are preserved.

Foundation stage (measured in the evidence run of PR #175): **82** distinct warnings, left in **6** packages, all of them the queue family (`Naravel.Queue` 21, `Queue.Redis` 18, `Queue.Database` 15, `Queue.File` 14, `Queue.Kafka` 8, `Queue.RabbitMQ` 6). IL2026 40, IL3050 39, IL2091 3.

- **Naravel.Foundation and the modules that use `AddNaravelManager`:** `AddNaravelManager<TManager, TDriver, TOptions>(services)` registers the manager without touching configuration. The `(services, configuration)` overload still exists for application code, binds through reflection, and carries `RequiresUnreferencedCode` and `RequiresDynamicCode`. Mail, Queue, Redis, Cache and Database call `Configure<ConcreteOptions>(section)` with the configuration binding generator and then the new overload; Broadcasting binds its three configurable values (`Default`, `Stores`, `PresenceEntryTtl`) by hand because `BroadcastingOptions.Channels` is code-only and makes the generator reject the type.

What is left is one design question, not many small fixes: queue job payloads are serialised by alias through the job type registry, which is reflection based by design (OD-07), and the queue envelopes (batch state, failed jobs) go through the same options. The expanded sample proves selected source-generated queue paths and the SQLite query-builder path, but not every queue driver, batch/failed-job payload, provider or public API. Its 28 ILC warnings are all attributed to Dapper; they must be understood before any AOT-compatibility claim for the database path. Packages that are at zero in the `src/` analyzer inventory still declare neither `IsTrimmable` nor `IsAotCompatible`: the static analyzers and this bounded sample are not enough to certify all dependencies and execution paths.
