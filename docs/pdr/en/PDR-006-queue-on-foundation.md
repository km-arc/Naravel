# PDR-006 — Migrate Naravel.Queue onto Naravel.Foundation

Status: **Accepted and implemented.** Owner approved the migration in principle (chat log referenced
in PROGRESS.md); this PDR records what was actually built, per AGENTS.md hard rule 1 ("PDR first" -
written alongside the implementation rather than strictly before it, since the owner had already
approved the direction and asked for stage-by-stage delivery, see PROGRESS.md "Working style").

## Context

`Naravel.Queue` was built before `Naravel.Foundation` existed. It had its own hand-rolled
`IQueueManager`/`QueueManager`/`IQueueDriverFactory`, duplicating exactly what Foundation's
`Manager<TDriver, TOptions>` + `IDriverRegistry<TDriver>` already solve (once-only driver creation,
runtime `Extend`, config hot reload, disposal) - a direct violation of AGENTS.md hard rule 2 ("never
re-implement driver resolution/caching/Extend"). Its configuration also used `"Connections"` as the
store-collection keyword, which PDR-005 superseded with a single `"Stores"` keyword for every module.

## Decision

Migrate `Naravel.Queue` onto `Manager<IQueueDriver, QueueOptions>` / `IDriverRegistry<IQueueDriver>`,
with these specifics:

1. **`QueueManager : Manager<IQueueDriver, QueueOptions>`**, sealed, adds one member:
   `Connection(name)` as an alias for `Driver(name)` - Laravel calls this method "connection" on its
   queue manager, and PDR-005 keeps that vocabulary at the C# API level even though the config
   keyword is `"Stores"`.
2. **`QueueOptions : ManagerOptions`** - an intentionally empty subclass (no Queue-specific top-level
   settings exist yet; keeping the type gives a documented extension point instead of modules
   sharing the literal `ManagerOptions` type).
3. **`IQueueDriverFactory` removed entirely.** Each driver package (`Naravel.Queue.Memory`, `.File`,
   `.Redis`, `.Database`, `.RabbitMQ`, `.Kafka`) now exposes `AddXxxDriver(IServiceCollection,
   IConfiguration, string sectionName = "NaravelQueue")`, implemented via a new shared helper:
   `QueueDriverRegistrationExtensions.AddQueueDriver(services, configuration, driverName, factory,
   sectionName)`. It scans `{sectionName}:Stores` once, at registration time, for every store whose
   own `"Driver"` key matches `driverName` (case-insensitive), and registers one
   `IDriverRegistry<IQueueDriver>` factory per matching store name via Foundation's
   `AddNaravelDriver<IQueueDriver>(name, factory)`.

   **This is the mechanism that answers the "store name vs. driver type" question** raised while
   planning PDR-005: Foundation itself needed no change - the indirection lives entirely in each
   driver package's registration helper, which already has eager access to the raw
   `IConfigurationSection` at startup (before the DI container is built), unlike a driver factory
   closure (which only receives `IServiceProvider` at creation time, not a name).

4. **Worker driver resolution moved inside the poll loop.** Previously `QueueWorkerService` resolved
   its connection's driver once, before the `while` loop, and reused that reference forever (worse:
   the old hand-rolled manager never evicted cached drivers at all, so this didn't matter much - but
   it meant a changed connection string was never picked up without a restart). Now
   `_manager.Connection(_options.Connection)` is called at the top of every loop iteration. Because
   `Manager.Driver()` is a cheap cache lookup except when an actual rebuild is needed, this is not a
   performance concern, and it means a config reload (PDR-004) now takes effect without restarting
   the worker. The resolved instance is still used for the *entire* pop→handle→ack/release/fail
   cycle of one message, never re-resolved mid-message: RabbitMQ's delivery tags and Kafka's
   consumer offsets are scoped to the specific channel/consumer instance that popped the message, so
   switching instances mid-message would break acknowledgement.
5. **Config shape**: `"NaravelQueue:Connections:name"` → `"NaravelQueue:Stores:name"` (PDR-005).

## Rejected alternatives

- **Keep `IQueueDriverFactory`, just have it delegate to `IDriverRegistry` internally.** Rejected:
  this would still duplicate Foundation's public surface for no benefit - every module should expose
  the *same* extension pattern (`AddXxxDriver(services, configuration, ...)` registering per-store
  factories), not a parallel one just for Queue.
- **Give `IDriverRegistry<TDriver>.Register` an overload that also passes the store's name/section to
  the factory.** Would have been a Foundation change requiring new tests there, delaying Queue,
  Cache and Filesystem. The registration-time scanning approach (point 3 above) achieves the same
  result with zero Foundation changes, so this was dropped as unnecessary.

## Consequences

- Breaking change (pre-1.0, approved): `AddMemoryDriver()`, `AddFileDriver()`, etc. now require an
  `IConfiguration` parameter. `appsettings.json`'s `"Connections"` key must become `"Stores"`.
  `IQueueManager` interface removed; code that referenced it directly must use the concrete
  `QueueManager` class instead (or `IQueueDriver` for driver-level code).
- New capabilities Queue gets "for free" from Foundation that the old hand-rolled manager never
  had: runtime `Extend`, config hot reload with fingerprint-based rebuild-only-when-changed, and
  proper disposal of drivers that implement `IDisposable`/`IAsyncDisposable` when the manager itself
  is disposed.
- New tests added specifically for this migration (`tests/Naravel.Queue.Tests/ManagerIntegrationTests.cs`):
  two stores sharing one driver type stay isolated, runtime `Extend`, a config reload that changes a
  store rebuilds its driver, and an unrelated reload does not.
- Two pre-existing, unrelated latent bugs were found and fixed while touching the test projects:
  `Naravel.Queue.Tests.csproj` and `Naravel.Foundation.Tests.csproj` used
  `ConfigurationBuilder.AddInMemoryCollection(...)` without referencing the
  `Microsoft.Extensions.Configuration.Memory` package that extension method lives in - added to both
  projects and to `Directory.Packages.props`.
