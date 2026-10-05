# Overview: why Naravel and Naravel.Foundation exist

## Naravel
Naravel aims for Laravel's concise, discoverable APIs without giving up .NET's speed: the same concepts (Cache, Queue,
Mail, Filesystem, Session, Events, Notifications ...), organised in familiar modules and implemented with .NET's
strong typing, `async/await`, dependency injection and native high-performance libraries.

**The parity rule:** a Laravel feature is implemented only if it brings value in .NET. Otherwise the native .NET
mechanism is used and the reasoning is recorded in a PDR (see `docs/pdr/en`).

## The problem Foundation solves
Almost every Laravel module has the same shape: *a contract, several interchangeable drivers, and a configuration
value that picks the default*. Laravel implements this once in `Illuminate\Support\Manager` and every module
(`CacheManager`, `QueueManager`, `MailManager`, ...) extends it. Without an equivalent, each Naravel module would
re-implement, and re-debug, the same logic: resolving a driver by name, creating it once, caching it, letting
users register custom drivers, reacting to configuration changes.

`Naravel.Foundation` is that shared logic, written once (DRY):

| Concern | Laravel | Naravel.Foundation |
|---|---|---|
| Resolve a driver by name, create once, cache | `Manager::driver()` | `Manager<TDriver,TOptions>.Driver / DriverAsync` |
| Register custom drivers at runtime | `Manager::extend()` | `Manager.Extend`, `IDriverRegistry<TDriver>` |
| Forget cached drivers | `forgetDrivers()` | `Forget`, `ForgetAll` |
| Default driver from config | `getDefaultDriver()` + `config('x.default')` | `ManagerOptions.Default` via `IOptionsMonitor` |
| Config shape | `config/x.php` (`default`, `stores`) | `ManagerOptions` (`Default`, `Stores`) |

## What Foundation is *not*
- It is **not** a replacement for the DI container. Laravel's `Illuminate\Container` maps to
  `Microsoft.Extensions.DependencyInjection`, which we keep as-is (PDR-002).
- It does **not** port `Manager::__call` magic forwarding; .NET's DI makes it unnecessary (PDR-003).

## Who should use it
Authors of Naravel modules (and of third-party Naravel drivers). Application developers normally never touch
Foundation directly; they use module APIs such as `ICacheDriver` or `CacheManager`.
