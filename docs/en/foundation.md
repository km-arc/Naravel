# Naravel.Foundation

`Naravel.Foundation` is the shared driver-manager foundation for Naravel modules. Install it directly when authoring a driver-based module; applications normally receive it as a dependency of the module they use.

## Quickstart

Define a driver contract, options, and a manager. Register the manager and a named driver before building the service provider:

```csharp
public interface IGreeter { string Greet(string name); }
public sealed class GreeterOptions : ManagerOptions { }
public sealed class GreeterManager(IServiceProvider sp, IDriverRegistry<IGreeter> registry, IOptionsMonitor<GreeterOptions> options)
    : Manager<IGreeter, GreeterOptions>(sp, registry, options);
public sealed class PoliteGreeter : IGreeter { public string Greet(string name) => $"Hello, {name}!"; }
services.AddNaravelManager<GreeterManager, IGreeter, GreeterOptions>(configuration.GetSection("Greeter"));
services.AddNaravelDriver<IGreeter>("polite", _ => new PoliteGreeter());
var greeter = provider.GetRequiredService<GreeterManager>().Driver("polite");
var greeting = greeter.Greet("Ada");
```

Set `Greeter:Default` to `polite` in configuration to use `Driver()` without a name.

## What the package provides

- `Manager<TDriver, TOptions>` resolves a driver by name, creates it once under concurrent access, caches it, and supports runtime registration through `Extend`.
- `IDriverRegistry<TDriver>` is the thread-safe, case-insensitive registry for synchronous and asynchronous factories.
- `ManagerOptions` supplies the `Default` driver name and named `Stores` configuration sections.
- `AddNaravelManager` registers the manager, registry, and direct default-driver service. `AddNaravelDriver` and `ConfigureNaravelDrivers` register built-in factories.
- `IOptionsMonitor<TOptions>` changes invalidate cached drivers only when the `Stores` configuration changes.

## Choosing the registration overload

`AddNaravelManager<TManager, TDriver, TOptions>(services, configuration)` binds the configuration section for you. That overload uses reflection-based configuration binding and is annotated for trimming and dynamic-code warnings.

For trimmed or Native AOT applications, bind the concrete options type yourself with `services.Configure<TOptions>(section)`, enable the configuration binding generator in the project that performs the binding, and call the overload without the `IConfiguration` argument:

```csharp
services.Configure<GreeterOptions>(configuration.GetSection("Greeter"));
services.AddNaravelManager<GreeterManager, IGreeter, GreeterOptions>();
```

Use `AddNaravelDriver` for a single factory, or `ConfigureNaravelDrivers` when a module needs to register several factories together. The provider-aware overload receives the root `IServiceProvider` when the registry is first created; do not resolve scoped services from it.

## Resolution, concurrency, and lifetime

`Driver(name)` returns the cached driver for that name. `DriverAsync(name, cancellationToken)` supports both synchronous and asynchronous factories. Concurrent callers share one creation; cancelling a caller cancels only that caller's wait, not the shared factory operation. Failed creations are not cached.

The synchronous `Driver` method does not block on an asynchronous-only factory. Use `DriverAsync` until that driver has been created.

`Forget(name)` and `ForgetAll()` retire cached instances; retired drivers remain alive until the manager is disposed so existing users are not disrupted. The manager disposes the factory-created drivers it owns. If the default driver is resolved through the direct `TDriver` service registered by `AddNaravelManager`, dependency injection owns and disposes that instance and the manager skips it. Factories should return instances the manager may own; do not return an unrelated container-owned singleton.

## Dependencies and guidance

The package depends on `Microsoft.Extensions.*` abstractions and has no dependency on another Naravel module. See the [architecture guide](architecture.md), [module authoring guide](module-authoring-guide.md), and [trimming/AOT guide](trimming-aot.md) for the full contracts and examples.

Persian documentation: [راهنمای فارسی Foundation](https://github.com/km-arc/Naravel/blob/main/docs/fa/foundation.md).
