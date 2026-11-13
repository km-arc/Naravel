# Naravel.Foundation

[![NuGet](https://img.shields.io/nuget/v/Naravel.Foundation.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Foundation)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Foundation.svg)](https://www.nuget.org/packages/Naravel.Foundation)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

The shared base of Naravel's driver-based modules: named drivers built once from configuration, replaceable while the application runs, and disposed by one owner.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Foundation`. Applications get it with the module they use; install it yourself when you write a driver-based module of your own |
| **Targets** | .NET 10 |
| **Needs** | `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options` and `Microsoft.Extensions.Options.ConfigurationExtensions`, and no other Naravel module |
| **Used by** | `Naravel.Cache`, `Naravel.Database`, `Naravel.Filesystem`, `Naravel.Mail`, `Naravel.Queue`, `Naravel.Redis` and `Naravel.Broadcasting` |

## What you get

- **Named drivers.** `Manager<TDriver, TOptions>` returns the driver registered under a name with `Driver(name)`, or with `DriverAsync(name, cancellationToken)` when its factory is asynchronous. It builds each driver once, even when callers arrive together, and caches it; a creation that fails is not cached, so the next call tries again.
- **A default and settings from configuration.** `ManagerOptions` carries `Default`, the name that `Driver()` uses, and `Stores`, one configuration section per driver, which a factory reads with `GetStore(name)`. A changed `Default` applies on the next call; a changed `Stores` retires the cached drivers, so the next call builds new ones.
- **Drivers added while the application runs.** `Extend(name, factory)` registers a synchronous or asynchronous factory on a manager that is already built and replaces an existing name; `Forget(name)` and `ForgetAll()` retire cached instances.
- **Registration.** `AddNaravelManager` registers the manager, its driver registry and a direct `TDriver` service for the default driver; `AddNaravelDriver` registers one built-in driver and `ConfigureNaravelDrivers` several. The overload that takes an `IConfiguration` binds the options by reflection, so for trimmed or Native AOT applications bind them yourself and call the overload without it.
- **One owner for disposal.** The manager disposes the drivers its factories created. A driver that fails to dispose does not stop the others; the failures come back together in one `AggregateException`.
- **Errors that name the problem.** `DriverNotRegisteredException` carries `DriverName`, `DriverType` and `AvailableDrivers`, and a blank name, a missing default and a missing `Stores` section each have a fixed message.

## Quick start

In a web project (`dotnet new web`) with this package added, replace `Program.cs`:

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

Run it and open `/hello`: it answers `Hello, Ada!`. Set `"Greeter": { "Default": "polite" }` in `appsettings.json` and `manager.Driver()` without a name returns the same driver. `Greeter` is the example's own module, not a shipped one.

## Related packages

| Package | Manager built on Foundation | Resolves by name |
|---|---|---|
| [`Naravel.Cache`](https://www.nuget.org/packages/Naravel.Cache) | `CacheManager`, `LockManager` | `ICacheStore`, `ICacheLock` |
| [`Naravel.Database`](https://www.nuget.org/packages/Naravel.Database) | `DatabaseManager` | `IDatabaseConnection` |
| [`Naravel.Filesystem`](https://www.nuget.org/packages/Naravel.Filesystem) | `StorageManager` | `IStorageDriver` |
| [`Naravel.Mail`](https://www.nuget.org/packages/Naravel.Mail) | `MailManager` | `IMailTransport` |
| [`Naravel.Queue`](https://www.nuget.org/packages/Naravel.Queue) | `QueueManager` | `IQueueDriver` |
| [`Naravel.Redis`](https://www.nuget.org/packages/Naravel.Redis) | `RedisManager` | `IRedisConnection` |
| [`Naravel.Broadcasting`](https://www.nuget.org/packages/Naravel.Broadcasting) | `BroadcastManager` | `IBroadcastDriver` |

## Learn more

- Foundation: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/foundation.md), with the errors, the lifetime rules and how to test a module built on it
- Writing a module: [module authoring guide](https://github.com/km-arc/Naravel/blob/main/docs/en/module-authoring-guide.md), [architecture](https://github.com/km-arc/Naravel/blob/main/docs/en/architecture.md) and [trimming and AOT](https://github.com/km-arc/Naravel/blob/main/docs/en/trimming-aot.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
