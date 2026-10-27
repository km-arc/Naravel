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

## Complete example: a small module

A `Greeter` module with two drivers, built the way every Naravel module is built. `ShoutingGreeter` is a third `IGreeter` you write. The checklist is in [module-authoring-guide.md](module-authoring-guide.md).

```csharp
// 1. Contract and options.
public interface IGreeter { string Greet(string name); }

public sealed class GreeterOptions : ManagerOptions { }          // Default + Stores come from the base class

// 2. The manager: all resolving, caching, Extend, reload and disposal come from Foundation.
public sealed class GreeterManager(IServiceProvider sp, IDriverRegistry<IGreeter> registry, IOptionsMonitor<GreeterOptions> options)
    : Manager<IGreeter, GreeterOptions>(sp, registry, options);

// 3. Two drivers. Each reads only its own store section.
public sealed class PoliteGreeter(string title) : IGreeter
{
    public string Greet(string name) => $"Good day, {title} {name}.";
}
public sealed class CasualGreeter : IGreeter
{
    public string Greet(string name) => $"Hey {name}!";
}

// 4. Registration (the AddNaravelXxx wrapper a module ships).
public static IServiceCollection AddNaravelGreeter(this IServiceCollection services, IConfiguration configuration)
{
    services.AddNaravelManager<GreeterManager, IGreeter, GreeterOptions>(configuration.GetSection("Greeter"));
    services.AddNaravelDriver<IGreeter>("casual", _ => new CasualGreeter());
    services.AddNaravelDriver<IGreeter>("polite", sp =>
    {
        var store = sp.GetRequiredService<IOptionsMonitor<GreeterOptions>>().CurrentValue.GetStore("polite");
        return new PoliteGreeter(store["Title"] ?? "Dr.");
    });
    return services;
}
```

```json
{ "Greeter": { "Default": "polite", "Stores": { "polite": { "Title": "Prof." }, "casual": { } } } }
```

```csharp
// 5. Use it.
app.MapGet("/hello/{name}", (string name, GreeterManager greeters) => greeters.Driver().Greet(name));          // default: polite
app.MapGet("/hey/{name}",   (string name, GreeterManager greeters) => greeters.Driver("casual").Greet(name));   // by name

// 6. Replace or add a driver at run time (Laravel: Manager::extend). Thread-safe; re-registering a name retires the cached instance.
greeters.Extend("shouting", _ => new ShoutingGreeter());

// 7. Test it: the same Extend, no configuration file.
[Fact]
public void Extend_adds_a_driver_after_the_host_is_built()
{
    using var provider = new ServiceCollection().AddNaravelGreeter(configuration).BuildServiceProvider();
    var greeters = provider.GetRequiredService<GreeterManager>();

    greeters.Extend("shouting", _ => new ShoutingGreeter());

    greeters.Driver("shouting").Greet("Ali").Should().Be("ALI!");
}
```

## Dependencies
`Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`,
`Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Configuration.Abstractions`. No module dependencies.
