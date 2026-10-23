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
| `illuminate/filesystem` (`Storage::disk`) | `Naravel.Filesystem` (Local, S3) | Adapted | Foundation migration implemented under approved PDR-008; local traversal protection retained; verification evidence is in the R02 row of [ROADMAP.md](../../ROADMAP.md) |
| Events (`Illuminate\Contracts\Events\Dispatcher`) | `Naravel.Events` + optional `Naravel.Events.Queue` | Adapted | PDR-011; typed DI listeners, scoped subscriptions, stop propagation, explicit queue adapter, Fake and telemetry |
| Mail (`Illuminate\Contracts\Mail\Mailer`, mailables) | `Naravel.Mail` (core: Mailable, `log`/`array` transports, Fake; `smtp` via `Naravel.Mail.Smtp` (MailKit), queue adapter `Naravel.Mail.Queue`) | Adapted | PDR-013 and PDR-013a; typed Mailable over named transports on Foundation, no template engine, queue payload is message data only; see [docs/en/mail.md](mail.md) |
| Query builder (`DB::table`, `Illuminate\Database\Query\Builder`) | `Naravel.Database` (+ `.Sqlite`, `.MySql`, `.Postgres`, `.SqlServer`): own SQL compiler with SQLite, MySQL, PostgreSQL and SQL Server grammars, `IDatabase`, Dapper for typed rows | Adapted (where family, joins, grouping, ordering, aggregates, streaming, writes, unions, subqueries, CTEs, raw expressions, row locks and debugging helpers on all four databases) | PDR-022; parameters only, frozen identifier grammar, `IConnectionScope` seam for later transactions, no SqlKata; one shared contract suite runs on every database in CI; see [docs/en/database.md](database.md) and [roadmap/QUERY-BUILDER-COVERAGE.md](../../roadmap/QUERY-BUILDER-COVERAGE.md) |
| Hashing (`Illuminate\Contracts\Hashing\Hasher`, `Hash::make/check/needsRehash`) | `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>` (package `Microsoft.Extensions.Identity.Core`) | Native | PDR-019; no package. Recipe, work-factor note and the bcrypt/Argon2id gap are in [docs/en/hashing.md](hashing.md) |
| Pagination (`Illuminate\Contracts\Pagination\Paginator`, `LengthAwarePaginator`, `CursorPaginator`) | `Naravel.Pagination` (core, no EF dependency) + `Naravel.Pagination.EntityFrameworkCore` (`PaginateAsync`, `SimplePaginateAsync`, `CursorPaginateAsync`) | Adapted | PDR-020; Laravel's `data`/`links`/`meta` JSON, keyset cursors declared once and applied as both order and filter, unsigned strict cursors, page size capped at 100. Page number is passed in, not read from the request. See [docs/en/pagination.md](pagination.md) |
| Session / Notifications / Broadcasting | future modules | Open | PDRs approved for R10/R18; implementation remains staged |
| Routing (`Route::`, groups, named routes, `where`, binding, resources) | `Naravel.Routing` (layer over ASP.NET Core) | Adapted | PDR-009; core routing and the ready-made middleware are delivered through R05 (status and verification in [ROADMAP.md](../../ROADMAP.md)). Implicit binding is Native; `Route::view`, string actions, `route:cache` Rejected |
| HTTP middleware engine (aliases, groups, params, priority, terminable, `withoutMiddleware`) | `Naravel.Routing` | Adapted | PDR-009; global middleware stays native `app.Use*` |
| Ready-made middleware (throttle, signed, maintenance, TrimStrings ...) | `Naravel.Routing` | Adapted | PDR-009; implemented in `Naravel.Routing` by R05 as the aliases `throttle`, `signed`, `maintenance`, `cache.headers`, `trim`, `convert.empty` and `guest` (process-local limits are listed in [routing.md](routing.md)). CORS / TrustProxies / TrustHosts / ValidatePostSize / EncryptCookies are Native |
