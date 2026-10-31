# Trimming and Native AOT status

**Naravel is not trim-safe or AOT-compatible yet, and no package declares `IsTrimmable` or `IsAotCompatible`.** This page is the measured inventory, not a promise.

## How it is measured

`.github/workflows/evidence.yml` builds every project under `src/` with `IsAotCompatible`, `EnableTrimAnalyzer`, `EnableAotAnalyzer` and `EnableSingleFileAnalyzer` set at **restore and build** time (the ILLink analyzer pack is only restored when the properties are set at restore), with warnings kept as warnings. It is report-only and runs on demand (`workflow_dispatch`) and on pull requests that touch it or the benchmarks.

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

- Applications that publish with trimming or Native AOT must expect warnings and possible runtime failures from the packages above. JIT deployments are not affected.
- The queue envelope (`QueuedMessage`) and the job payloads are the largest area: payloads are serialised by alias through the job type registry, which is reflection based by design (OD-07).
- Plan: fix or annotate package by package, smallest first (`Naravel.Database`, `Naravel.Encryption`, `Naravel.Cache.Redis`, `Naravel.Pagination`), then set `IsAotCompatible` on a package only when its row in the next inventory is zero. The workflow becomes a gate per package at that point.

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

What is left is one design question, not many small fixes: queue job payloads are serialised by alias through the job type registry, which is reflection based by design (OD-07), and the queue envelopes (batch state, failed jobs) go through the same options. Packages that are at zero in this inventory still declare neither `IsTrimmable` nor `IsAotCompatible`: the analyzers only see what they can see statically, and no sample application has been published with `PublishAot` and run yet.
