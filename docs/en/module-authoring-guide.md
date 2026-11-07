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
- Give the project its own `<Description>` (at least 40 characters, saying what the package is) and `<PackageTags>`, plus its package page `docs/nuget/<PackageId>.md` packed as `README.md` (see the `docs/nuget` row in `AGENTS.md`); everything else NuGet shows (license, URLs, copyright, release notes, icon) comes from `Directory.Build.props`. CI packs every project and fails a package with a missing or default description, missing tags or a missing or wrong README (`.github/scripts/check-package-metadata.py`, `check-package-readmes.py`), so a gap is caught before a version that cannot be edited is pushed.
- List the module where users look for it: a row in the README module table, a section in `docs/en/modules.md`, its install and registration rows in `docs/en/getting-started.md`, and a line in `docs/README.md`. Keep the README itself short; the detail belongs in the docs.
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
- **Ownership:** if the default `TDriver` service registered by `AddNaravelManager` is resolved, DI owns and disposes that default instance and the manager skips it. If it is not resolved, the manager disposes the default itself. The manager disposes all other factory-created drivers, including named and retired instances. Create instances with `new` / `ActivatorUtilities.CreateInstance`; never return an unrelated container-owned singleton from a factory.
- Keep `Dispose`/`DisposeAsync` idempotent where practical, especially when a driver can also be explicitly disposed by application code.
- Prefer `IAsyncDisposable` for I/O drivers. Use async factories for anything that connects over the network.
- If a driver depends on configuration *outside* its store section (like Laravel's `connection => 'cache'` pointing at another config section), call `manager.Forget(name)` when that config changes.

## 4. Registration extension
```csharp
public static IServiceCollection AddNaravelCache(this IServiceCollection s, IConfiguration c)
{
    s.Configure<CacheOptions>(c);                                    // concrete type: bound at compile time
    s.AddNaravelManager<CacheManager, ICacheDriver, CacheOptions>(); // manager, registry, default driver
    s.AddNaravelDriver<ICacheDriver>("file", /* ... */);
    return s;
}
```
Set `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>` in the module's project file so the `Configure` call is bound by the configuration binding generator instead of reflection (the trim and Native AOT path). `AddNaravelManager<...>(services, configuration)` is the shorter form for application code; it binds through reflection and is marked `RequiresUnreferencedCode` and `RequiresDynamicCode`.

## 5. Tests (mandatory)
- Each built-in driver has its own tests.
- Manager wiring: default resolution, explicit name, runtime `Extend`, config reload rebuilding a driver.
- Do not re-test Foundation behaviour (once-only creation, cancellation, disposal); that lives in Foundation's tests.

## 6. Docs (mandatory, English)
`docs/en/<module>.md`, a row group in `laravel-parity.md`, English PDR(s), XML docs
on every public member: purpose, **Laravel equivalent**, **why it exists / why not native**, **what was not ported**.

## 7. Definition of done
`dotnet test` green · English docs · PDR approved · parity table updated · no dependency from Foundation to your module.
