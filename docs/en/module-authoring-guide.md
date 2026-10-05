# Module authoring guide (build a module on Foundation)

Follow this checklist for every driver-based module (Cache, Queue, Mail, Filesystem, Session, Broadcasting ...).

## 0. Before code
- Read `AGENTS.md` and `docs/pdr/en/PDR-001..006` and `PDR-009`.
- Write a **new PDR** for the module: Laravel feature → native .NET option → value added? → decision → rejected alternatives.
- Get owner approval for large decisions.

## 1. Project (monorepo)
Everything lives in this repository and one solution.
- `src/Naravel.<Module>/Naravel.<Module>.csproj` references `Naravel.Foundation` only (plus what the module strictly needs). A driver with a heavy dependency (Redis, RabbitMQ, EF Core ...) gets its own project `src/Naravel.<Module>.<Provider>`.
- Tests in `tests/Naravel.<Module>.Tests` (xUnit + FluentAssertions 7.x).
- Add **every** new project to `Naravel.slnx`.
- Put new package versions in `Directory.Packages.props` only; `.csproj` files use `<PackageReference Include="X" />` without `Version`, and do not repeat target framework or NuGet metadata (those come from `Directory.Build.props`, including the shared lock-step version).
- Do not create per-module repositories, solutions or CI workflows.

## 2. Contract, options, manager
```csharp
public interface ICacheDriver { ValueTask<string?> GetAsync(string key, CancellationToken ct = default); /* ... */ }

public sealed class CacheOptions : ManagerOptions { /* module-wide settings, e.g. Prefix */ }

public sealed class CacheManager(IServiceProvider sp, IDriverRegistry<ICacheDriver> registry, IOptionsMonitor<CacheOptions> options)
    : Manager<ICacheDriver, CacheOptions>(sp, registry, options);
```
Override `DefaultDriverName` only for a module-specific fallback. Do **not** re-implement resolution, caching, `Extend`, or invalidation.

## 3. Built-in drivers
Each driver reads **its own** store section and binds its own typed options:
```csharp
services.AddNaravelDriver<ICacheDriver>("file", sp =>
{
    var store = sp.GetRequiredService<IOptionsMonitor<CacheOptions>>().CurrentValue.GetStore("file");
    return new FileCacheDriver(store["Path"] ?? "storage/cache");
});
```
Rules:
- **Ownership:** the manager disposes what factories return. Create instances with `new` / `ActivatorUtilities.CreateInstance`; do not return container-owned singletons.
- **Idempotent `Dispose`/`DisposeAsync`** (the DI container may also dispose the default driver).
- Prefer `IAsyncDisposable` for I/O drivers. Use async factories for anything that connects over the network.
- If a driver depends on configuration *outside* its store section (like Laravel's `connection => 'cache'` pointing at another config section), call `manager.Forget(name)` when that config changes.

## 4. Registration extension
```csharp
public static IServiceCollection AddNaravelCache(this IServiceCollection s, IConfiguration c)
{
    s.AddNaravelManager<CacheManager, ICacheDriver, CacheOptions>(c);
    s.AddNaravelDriver<ICacheDriver>("file", /* ... */);
    return s;
}
```

## 5. Tests (mandatory)
- Each built-in driver has its own tests.
- Manager wiring: default resolution, explicit name, runtime `Extend`, config reload rebuilding a driver.
- Do not re-test Foundation behaviour (once-only creation, cancellation, disposal); that lives in Foundation's tests.

## 6. Docs (mandatory, both languages)
`docs/en/<module>.md` and `docs/fa/<module>.md`, a row group in `laravel-parity.md` (both languages), PDR(s) in both languages, XML docs
on every public member: purpose, **Laravel equivalent**, **why it exists / why not native**, **what was not ported**.

## 7. Definition of done
`dotnet test` green · docs EN+FA · PDR approved · parity table updated · no dependency from Foundation to your module.
