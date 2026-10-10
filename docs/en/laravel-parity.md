# Laravel parity table

Status: **Adopted** (ported as is) · **Adapted** (same goal, .NET-idiomatic form) · **Native** (.NET already solves it; nothing ported) · **Rejected** (not ported, with reason) · **Open** (undecided; needs a PDR before work).

| Laravel concept | Naravel / .NET equivalent | Status | Notes / PDR |
|---|---|---|---|
| `Illuminate\Container` (bind/singleton/resolve) | `Microsoft.Extensions.DependencyInjection` | Native | Not replaced. PDR-002 |
| `Illuminate\Support\Manager::driver()` | `Manager<TDriver,TOptions>.Driver/DriverAsync` | Adapted | Async-first, once-only creation. PDR-002 |
| `Manager::extend()` | `Manager.Extend` + `IDriverRegistry` | Adopted | Works at runtime; Keyed DI cannot. PDR-002 |
| `forgetDrivers()` / `forgetDriver()` | `ForgetAll` / `Forget` | Adopted | Old instance disposed with manager. PDR-004 |
| `Manager::__call` forwarding | Default driver registered directly in DI | Rejected | PDR-003 |
| `createXxxDriver()` naming convention | Registry factories | Adapted | No reflection on method names |
| `getDefaultDriver()` | `DefaultDriverName` (from options) | Adapted | Read per call |
| `config/*.php` + `env()` | `appsettings*.json`, env vars, `IConfiguration` | Native | Layered providers built in |
| `config('x.default')` / `stores` arrays | `ManagerOptions.Default/Stores` via `IOptionsMonitor` | Adapted | Typed; live reload. PDR-004 |
| `php artisan config:cache` | — | Rejected | Not needed; no per-request boot in .NET |
| Config hot reload | `IOptionsMonitor` + fingerprint invalidation | Adapted | Laravel has none. PDR-004 |
| Service providers | DI extension methods (`AddNaravelX`) | Adapted | Per-module; revisit when a module needs deferred providers |
| Facades (`Cache::get`) | — | Open | Needs its own PDR before any work |
| `illuminate/queue` | `Naravel.Queue` (+ Memory/File/Redis/Database/RabbitMQ/Kafka) | Adapted | PDR-006; explicit aliases and guarded deserialization in R00; shared provider contracts, env-gated tests, atomic Redis transitions and ordered Kafka commits in R01; failed-job retry, persistent batches, timeouts/worker controls, RabbitMQ.Client 7 async, metrics and Fake in R07 |
| `illuminate/cache` | `Naravel.Cache` (+ Redis, Memcached) | Adapted | PDR-007; Foundation managers, Memory/Redis/Memcached providers, tag versions, scopes and token locks; shared contracts and env-gated Redis/Memcached integration in R01; Cache-backed fixed-window RateLimiter in R04/PDR-007a |
| `illuminate/filesystem` (`Storage::disk`) | `Naravel.Filesystem` (Local, S3) | Adapted | Foundation migration implemented under approved PDR-008; local traversal protection retained; 20 focused tests passed, included in the 172-test solution run on 2026-10-04 |
| Events (`Illuminate\Contracts\Events\Dispatcher`) | `Naravel.Events` + optional `Naravel.Events.Queue` | Adapted | PDR-011; typed DI listeners, scoped subscriptions, stop propagation, explicit queue adapter, Fake and telemetry |
| Mail / Session / Notifications / Broadcasting | future modules | Open | PDRs approved for R09/R10/R18; implementation remains staged |
| Routing (`Route::`, groups, named routes, `where`, binding, resources) | `Naravel.Routing` (layer over ASP.NET Core) | Adapted | PDR-009; Stage 4a verified (64/64 tests passed, 2026-10-04). Implicit binding is Native; `Route::view`, string actions, `route:cache` Rejected |
| HTTP middleware engine (aliases, groups, params, priority, terminable, `withoutMiddleware`) | `Naravel.Routing` | Adapted | PDR-009; global middleware stays native `app.Use*` |
| Ready-made middleware (throttle, signed, maintenance, TrimStrings ...) | `Naravel.Routing` | Open | Stage 4b, planned inside the routing package, is not started. These implementations are not available yet. CORS / TrustProxies / TrustHosts / ValidatePostSize / EncryptCookies are Native |
