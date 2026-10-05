# Architecture

## Components (all in namespace `Naravel.Foundation`)
```
IDriverRegistry<TDriver>  ── source of truth: name -> factory (sync or async), open at runtime, thread-safe
DriverRegistry<TDriver>   ── default implementation (ConcurrentDictionary, case-insensitive), raises Registered(name)
IManagerOptions           ── Default + Stores (dictionary of raw IConfigurationSection)
ManagerOptions            ── ready-to-bind base class for module options
Manager<TDriver,TOptions> ── resolve + create-once + cache + invalidate + dispose
ServiceCollectionExtensions ── AddNaravelManager / AddNaravelDriver / ConfigureNaravelDrivers
DriverNotRegisteredException, ManagerOptionsExtensions.GetStore
```

## Request flow: `manager.DriverAsync("redis")`
1. Resolve the name (`null` → `DefaultDriverName`, read fresh from `IOptionsMonitor.CurrentValue`).
2. Fail fast with `DriverNotRegisteredException` if the registry has no factory for it.
3. Under a short lock, get or insert a cache `Entry` holding a `TaskCompletionSource`. Only the caller that inserts the
   entry starts creation, so **concurrent callers cause exactly one factory call**.
4. Creation runs `registry.CreateAsync(name, provider, lifetimeToken)` outside the lock.
5. Callers await the shared task with `WaitAsync(ct)`: cancelling a caller stops *its* wait, not the shared creation.
6. On failure the entry is evicted (failures are never cached); on success the driver stays cached.

The sync path (`Driver`) uses the same cache. It runs sync factories inline and **throws** for async-only factories
instead of blocking (no sync-over-async deadlocks). If an async driver was already created, `Driver` returns it.

## Invalidation
- **Factory replaced** (`Extend` / `registry.Register`): the cached instance under that name is retired.
- **Configuration changed**: `IOptionsMonitor.OnChange` fires on *any* configuration reload, even unrelated keys.
  The manager compares a fingerprint of `Stores` and only retires all drivers when it truly changed. A changed
  `Default` needs no rebuild because it is read on every call. (PDR-004)
- **Manual**: `Forget(name)`, `ForgetAll()`.

## Lifetime & disposal
Retired drivers are **not** disposed immediately (a request may still hold them). They are kept in a retired list and
disposed, together with cached drivers, when the manager is disposed. Drivers are de-duplicated by reference and disposed
once; errors are aggregated into `AggregateException` after all drivers were attempted. A creation that finishes after
disposal disposes its result and fails the caller with `ObjectDisposedException`.

## Thread safety
All public members are thread-safe. State is guarded by one short-lived lock; no factory ever runs under the lock.

## Dependencies
`Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`,
`Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Configuration.Abstractions`. No module dependencies.
