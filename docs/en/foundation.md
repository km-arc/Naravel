# Naravel.Foundation

`Naravel.Foundation` is the shared base that Naravel's driver-based modules (Cache, Queue, Filesystem, Mail and others) are built on. It resolves **named drivers** for a contract (for example "the `redis` cache store"), builds each one once, caches it, and lets you add or replace drivers while the application runs.

Applications normally never install it: it arrives as a dependency of the module you use. Install it directly when you are **writing a driver-based module** of your own.

## Quickstart

A driver-based module has four parts: a driver contract, its options, a manager, and one or more drivers. This is the smallest complete example. Create a web project (`dotnet new web`), add the package (`dotnet add package Naravel.Foundation`) and replace `Program.cs`:

```csharp
using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Naravel.Foundation;

var builder = WebApplication.CreateBuilder(args);

// 1. Register the manager, its driver registry and the configuration section it reads.
builder.Services.AddNaravelManager<GreeterManager, IGreeter, GreeterOptions>(
    builder.Configuration.GetSection("Greeter"));

// 2. Register a built-in driver under a name.
builder.Services.AddNaravelDriver<IGreeter>("polite", _ => new PoliteGreeter());

var app = builder.Build();
app.MapGet("/hello", (GreeterManager manager) => manager.Driver("polite").Greet("Ada"));
app.Run();

// 3. The contract, the options and the manager. The manager is only a constructor.
public interface IGreeter { string Greet(string name); }

public sealed class GreeterOptions : ManagerOptions { }

public sealed class GreeterManager(IServiceProvider sp, IDriverRegistry<IGreeter> registry, IOptionsMonitor<GreeterOptions> options)
    : Manager<IGreeter, GreeterOptions>(sp, registry, options) { }

public sealed class PoliteGreeter : IGreeter
{
    public string Greet(string name) => $"Hello, {name}!";
}
```

Run it and open `/hello`: it answers `Hello, Ada!`.

### Using a default driver

`manager.Driver()` without a name uses the `Default` option. Set it in `appsettings.json`:

```json
{ "Greeter": { "Default": "polite" } }
```

```csharp
app.MapGet("/hi", (GreeterManager manager) => manager.Driver().Greet("Ada"));   // uses "polite"
```

Without a default, `Driver()` throws `InvalidOperationException`: `No default driver is configured for IGreeter. Set the 'Default' option or pass a driver name explicitly.`

## What the package provides

| Piece | What it does |
|---|---|
| `Manager<TDriver, TOptions>` | Resolves a driver by name, creates it once even under concurrent access, caches it, and accepts new drivers at runtime through `Extend`. |
| `IDriverRegistry<TDriver>` | The thread-safe, case-insensitive registry of synchronous and asynchronous factories. |
| `ManagerOptions` | `Default` (the default driver's name) and `Stores` (named configuration sections, one per configured driver). |
| `AddNaravelManager` | Registers the manager, the registry and a direct `TDriver` service for the default driver. |
| `AddNaravelDriver`, `ConfigureNaravelDrivers` | Register built-in driver factories (one, or several together). |

When the `Stores` configuration changes, `IOptionsMonitor<TOptions>` invalidates the cached drivers; a change elsewhere in the section does not.

## Drivers that read their settings

A driver usually needs settings. Each configured driver has a section under `Stores:<name>`, and `GetStore(name)` returns it:

```json
{
  "Greeter": {
    "Default": "friendly",
    "Stores": {
      "friendly": { "Greeting": "Hi" }
    }
  }
}
```

```csharp
builder.Services.AddNaravelDriver<IGreeter>("friendly", sp =>
{
    var options = sp.GetRequiredService<IOptionsMonitor<GreeterOptions>>().CurrentValue;
    var store = options.GetStore("friendly");                 // the "Stores:friendly" section
    return new FriendlyGreeter(store["Greeting"] ?? "Hello");
});

public sealed class FriendlyGreeter(string greeting) : IGreeter
{
    public string Greet(string name) => $"{greeting}, {name}!";
}
```

If the section is missing, `GetStore` throws `InvalidOperationException`: `Store 'friendly' is not configured. Configured stores: (none).` (the message lists the stores that do exist).

## Adding a driver while the application runs

`Extend` registers a driver on a manager that is already built (Laravel's `Cache::extend`). Use the asynchronous form when creating the driver needs I/O:

```csharp
var manager = app.Services.GetRequiredService<GreeterManager>();

manager.Extend("shout", _ => new ShoutingGreeter());                 // synchronous factory

manager.Extend("remote", async (sp, ct) =>                           // asynchronous factory
{
    var greeter = new RemoteGreeter();
    await greeter.ConnectAsync(ct);
    return greeter;
});

var shout = manager.Driver("shout");                                 // sync factories work with Driver()
var remote = await manager.DriverAsync("remote", ct);                // async-only factories need DriverAsync()
```

Registering a name again replaces its factory and retires the cached instance: the next call builds one from the new factory, while code that already holds the old instance keeps using it until the manager is disposed.

## Choosing the registration overload

`AddNaravelManager<TManager, TDriver, TOptions>(services, configuration)` binds the configuration section for you. It uses reflection-based configuration binding, so it carries trimming and dynamic-code warnings.

For trimmed or Native AOT applications, bind the concrete options type yourself with `services.Configure<TOptions>(section)`, enable the configuration binding generator in the project that does the binding, and call the overload without the `IConfiguration` argument:

```csharp
services.Configure<GreeterOptions>(configuration.GetSection("Greeter"));
services.AddNaravelManager<GreeterManager, IGreeter, GreeterOptions>();
```

Use `AddNaravelDriver` for a single factory and `ConfigureNaravelDrivers` when a module registers several together. The overload that takes a provider receives the root `IServiceProvider` when the registry is first created, so do not resolve scoped services from it.

## Resolution, concurrency and lifetime

- `Driver(name)` returns the cached driver for that name. `DriverAsync(name, cancellationToken)` also supports asynchronous factories.
- Concurrent callers share one creation. Cancelling a caller cancels only that caller's wait, not the shared factory call. A failed creation is not cached, so the next call tries again.
- The synchronous `Driver` never blocks on an asynchronous-only factory. Until such a driver exists it throws `InvalidOperationException`: `Driver 'remote' is registered with an asynchronous factory and has not been created yet. Use DriverAsync to create it.` Once `DriverAsync` has created it, `Driver` returns it. If another thread replaces a synchronous factory with an asynchronous one while `Driver` is creating, `Driver` throws the same exception, and a `DriverAsync` caller that shared that creation starts over and gets the driver of the new factory. One narrow case is not covered: if a factory is replaced with a synchronous one while `DriverAsync` is still running the old asynchronous factory, a `Driver` call that arrives in the moment between the registration and the manager learning of it waits for that creation to finish.
- `Forget(name)` and `ForgetAll()` retire cached instances so the next call builds a new one. Retired drivers stay alive until the manager is disposed, so code that already holds one is not disrupted.
- The manager disposes the drivers its factories created. If the default driver was resolved through the direct `TDriver` service that `AddNaravelManager` registers, dependency injection owns and disposes that instance and the manager skips it. A factory should therefore return an instance the manager may own, never an unrelated container-owned singleton.
- A failure while the manager is disposed does not stop the other drivers from being disposed. That covers a driver that fails to dispose, a callback that an asynchronous factory registered on the cancellation token the manager passes to it, and the manager's own subscriptions to the registry and to option changes failing to detach. The manager disposes every driver it owns and then throws one `AggregateException` that holds each of those failures. A driver whose creation finishes after the manager was disposed is disposed quietly, and a failure of that disposal is not reported.

## Errors

| When | Exception and message |
|---|---|
| The name is blank | `ArgumentException`: `Driver name must not be blank.` |
| No name given and no `Default` set | `InvalidOperationException`: `No default driver is configured for IGreeter. Set the 'Default' option or pass a driver name explicitly.` |
| The name is not registered | `DriverNotRegisteredException`: `Driver 'x' is not registered for IGreeter. Available drivers: polite, shout.` It exposes `DriverName`, `DriverType` and `AvailableDrivers`. |
| `Driver()` on an async-only driver not yet created | `InvalidOperationException`; use `DriverAsync`. |
| `GetStore` on a missing section | `InvalidOperationException`: `Store 'x' is not configured. Configured stores: a, b.` |
| The manager was disposed | `ObjectDisposedException` |
| A driver fails to dispose, a callback on the factory token throws, or a subscription of the manager fails to detach, while the manager is disposed | `AggregateException`: `The manager did not dispose cleanly: one or more drivers, lifetime callbacks or subscriptions failed.` It is thrown after every owned driver was disposed, and `InnerExceptions` holds each failure. |

## Testing a module built on Foundation

Register a fake under a name, so the code under test resolves it like any other driver and no real backend is needed:

```csharp
manager.Extend("fake", _ => new FakeGreeter());
Assert.Equal("Hello, Ada!", manager.Driver("fake").Greet("Ada"));
```

## Laravel equivalent

| Laravel (`Illuminate\Support\Manager`) | Naravel.Foundation |
|---|---|
| `driver('redis')` | `manager.Driver("redis")`, `DriverAsync("redis", ct)` |
| `getDefaultDriver()` | `manager.DefaultDriverName` (from `Default`) |
| `extend('name', $callback)` | `manager.Extend("name", factory)` |
| `__call` forwarding methods to the driver | Not ported on purpose: call the driver you resolved. |

## Dependencies and further reading

The package depends on `Microsoft.Extensions.*` abstractions and on no other Naravel module. The [architecture guide](https://github.com/km-arc/Naravel/blob/main/docs/en/architecture.md), the [module authoring guide](https://github.com/km-arc/Naravel/blob/main/docs/en/module-authoring-guide.md) and the [trimming and AOT guide](https://github.com/km-arc/Naravel/blob/main/docs/en/trimming-aot.md) give the full contracts.

