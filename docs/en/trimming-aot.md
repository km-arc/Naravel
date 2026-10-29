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
- **Naravel.Cache.Redis** is next and needs an API decision: `RedisCacheStore` serialises any `T`, so AOT support means letting the application pass a `JsonSerializerOptions` with its own resolver.
