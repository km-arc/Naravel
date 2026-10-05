# Usage (application side)

Foundation is normally consumed through a module. `CacheManager` below is an illustrative module built on Foundation.

## 1. Configure
```json
{
  "Cache": {
    "Default": "redis",
    "Stores": {
      "redis": { "Host": "localhost", "Port": 6379 },
      "file":  { "Path": "storage/framework/cache" }
    }
  }
}
```
Store names are case-insensitive. Each store section is read by that driver's own factory.

## 2. Register
```csharp
services.AddNaravelManager<CacheManager, ICacheDriver, CacheOptions>(configuration.GetSection("Cache"));
services.AddNaravelDriver<ICacheDriver>("file",  sp => new FileCacheDriver(sp.GetRequiredService<IOptionsMonitor<CacheOptions>>().CurrentValue.GetStore("file")));
services.AddNaravelDriver<ICacheDriver>("redis", async (sp, ct) => await RedisCacheDriver.ConnectAsync(/* ... */, ct));
```
Do this before building the provider. Modules usually ship an `AddNaravelCache()` wrapper that registers built-ins.

## 3. Consume — pick one
| Need | Inject | Call |
|---|---|---|
| "Just use the configured default" (99% of code) | `ICacheDriver` | `cache.Get(...)` |
| Choose a driver at runtime / follow a changing default / async-only driver | `CacheManager` | `manager.Driver("redis")`, `await manager.DriverAsync("redis", ct)` |

The injected `ICacheDriver` is resolved **once** (the default at first injection). It does not follow later `Default`
changes; use the manager if you need that.

## 4. Extend at runtime (Laravel's `Cache::extend`)
```csharp
manager.Extend("tenant-cache", sp => new TenantCacheDriver(sp.GetRequiredService<ITenantAccessor>()));
manager.Driver("tenant-cache");
```
Works after the host is built and is thread-safe. Re-registering a name replaces the factory and retires the cached instance.

## 5. Hot reload
With `reloadOnChange: true` on `appsettings.json` (the default in `WebApplication.CreateBuilder`), editing a store's
values makes the next `manager.Driver(name)` build a new driver. Changing only `Default` switches the default instead.
Unrelated config edits do nothing. Need to react to non-Stores dependencies? Call `manager.Forget(name)`.

## Errors you may see
| Exception | Meaning |
|---|---|
| `DriverNotRegisteredException` | Unknown name; message lists available drivers. |
| `InvalidOperationException` "No default driver…" | `Default` is empty. |
| `InvalidOperationException` "…asynchronous factory…" | Called `Driver()` on an async-only driver not yet created; use `DriverAsync`. |
| `InvalidOperationException` "Store 'x' is not configured" | `GetStore("x")` on a missing store. |
| `ObjectDisposedException` | Manager already disposed. |
