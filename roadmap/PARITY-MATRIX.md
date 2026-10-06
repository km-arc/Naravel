# Laravel → Naravel parity matrix

This is a stage-scoped map, not a commitment to reproduce PHP APIs mechanically. Prefer .NET-native functionality where
it is sufficient; each stage's PDR makes the feature-by-feature adoption decision. Member-level inventories should be
checked against the referenced Laravel 13 API page when implementing, not duplicated here.

## R00 — Queue correctness and project verification

- Laravel surface: `Illuminate\Contracts\Queue\ShouldQueue`, `Illuminate\Bus\PendingChain`, chain continuation and job
  serialization contracts.
- Naravel: `Naravel.Queue` serializer, dispatcher, `ChainLink`, `QueueWorkerService`.
- Value: deterministic type failures and predictable connection selection through every chained dispatch.
- Native baseline: `System.Text.Json` and .NET runtime type checks; preserve Naravel's typed `IJob` contract.
- API reference: [Laravel API 13.x](https://api.laravel.com/docs/13.x/index.html).

## R01 — Driver integration testing and CI matrix

- Laravel surface: queue and cache connection/provider behavior as exercised by real backing services.
- Naravel: Queue Redis/Database/RabbitMQ/Kafka and Cache Redis/Memcached provider contracts, env-gated integration tests, Redis atomic scripts, Kafka ordered commits, and CI service matrix.
- Value: detect serialization, acknowledgement, expiry, topology, and OS-specific failures that fakes cannot reveal; document broker-specific ordering limits.
- Native baseline: use provider-supported clients and existing CI; container dependencies require owner approval.
- API reference: [Illuminate Queue](https://api.laravel.com/docs/13.x/Illuminate/Contracts/Queue/Queue.html).

## R02 — Filesystem API parity (completed)

- Laravel surface: `Illuminate\Filesystem\Filesystem`, `Illuminate\Contracts\Filesystem\Filesystem`, storage disk operations.
- Naravel: `Naravel.Filesystem` manager, local and S3 drivers, `IStorageDriver`.
- Value: named disks, Laravel-like storage ergonomics, and portable driver selection; local filesystem APIs remain .NET-native.
- Native baseline: `System.IO`, `IFileProvider`, and AWS SDK where appropriate.
- API reference: [Illuminate Filesystem](https://api.laravel.com/docs/13.x/Illuminate/Filesystem/Filesystem.html).

## R03 — Shared Redis connections

- Laravel surface: Redis manager / connection resolution and reusable named connections.
- Naravel: proposed Redis connection module shared by Cache and Queue providers.
- Value: consistent named connection configuration, reuse, and lifecycle management without duplicate pools.
- Native baseline: provider client APIs remain underneath; do not create a wrapper that hides useful .NET client capabilities.
- API reference: [Illuminate Redis](https://api.laravel.com/docs/13.x/Illuminate/Redis/RedisManager.html).

## R04 — Cache, locks, rate limiting

- Laravel surface: `Illuminate\Contracts\Cache\Repository`, cache tags, atomic locks, `Illuminate\Cache\RateLimiter`.
- Naravel: Cache/tagging/scoping/locks are implemented (PDR-007); RateLimiter is open.
- Value: simple cache-aside, named stores, scoped keys, invalidation, atomic coordination, and rate-limit state.
- Native baseline: `IMemoryCache`, distributed cache abstractions, and `System.Threading.RateLimiting`; retain only the cohesive
  behavior .NET does not provide with the required provider semantics.
- API reference: [Illuminate Cache](https://api.laravel.com/docs/13.x/Illuminate/Cache/RateLimiter.html).

## R05 — HTTP middleware and routing completion

- Laravel surface: `Illuminate\Routing\Router`, route registration, middleware, signed URLs, throttle and maintenance behavior.
- Naravel: Routing Stage 4a exists; ready-made middleware and controller resource follow-ups are open.
- Value: concise route/middleware composition on top of ASP.NET Core.
- Native baseline: ASP.NET Core routing/middleware, Data Protection, and `System.Threading.RateLimiting`.
- API reference: [Illuminate Routing](https://api.laravel.com/docs/13.x/Illuminate/Routing/Router.html).

## R06 — Events

- Laravel surface: `Illuminate\Contracts\Events\Dispatcher`, listener/subscriber registration and dispatch.
- Naravel: future Events module, with queued listener integration evaluated against Naravel.Queue.
- Value: decoupled application events and explicit listener lifecycle.
- Native baseline: .NET delegates, DI, and hosted services; do not reproduce PHP discovery/reflection mechanics by default.
- API reference: [Illuminate Events](https://api.laravel.com/docs/13.x/Illuminate/Events/Dispatcher.html).

## R07 — Queue completion A

- Laravel surface: `Illuminate\Contracts\Queue\Job`, failed job provider, queue middleware, batches and worker controls.
- Naravel: failed-job storage and retry, reclaim attempts and per-job timeouts, persistent Database/Redis batches,
  worker controls, RabbitMQ.Client 7 async operations, queue metrics/tracing and a dispatcher fake.
- Value: reliable failure recovery, batch coordination and consistent provider behavior.
- Native baseline: hosted services, cancellation, health checks and provider-native acknowledgement.
- API reference: [Illuminate Queue Worker](https://api.laravel.com/docs/13.x/Illuminate/Queue/Worker.html).

## R08 — Queue completion B

- Laravel surface: unique jobs, debounce/overlap prevention and rate-limited jobs.
- Naravel: future Queue features integrated with Cache/locks.
- Value: prevent duplicate or conflicting work using explicit .NET contracts.
- Native baseline: distributed coordination must use an approved Cache/Redis abstraction; do not promise cross-process uniqueness from memory-only locks.
- API reference: [Illuminate Queue Middleware](https://api.laravel.com/docs/13.x/Illuminate/Queue/Middleware/WithoutOverlapping.html).

## R09 — Mail

- Laravel surface: `Illuminate\Contracts\Mail\Mailer`, mailables and transport configuration.
- Naravel: future Mail module after Events and Queue prerequisites.
- Value: typed messages, testable transport selection, optional queued delivery.
- Native baseline: `System.Net.Mail` is limited; evaluate modern provider libraries only through the PDR/dependency gate.
- API reference: [Illuminate Mail](https://api.laravel.com/docs/13.x/Illuminate/Contracts/Mail/Mailer.html).

## R10 — Notifications

- Laravel surface: `Illuminate\Contracts\Notifications\Dispatcher`, notification channels and notifiable routing.
- Naravel: future Notifications module built on approved Events/Mail/Queue boundaries.
- Value: multi-channel delivery from one typed notification.
- Native baseline: DI and channel/provider abstractions; avoid magic model traits.
- API reference: [Illuminate Notifications](https://api.laravel.com/docs/13.x/Illuminate/Notifications/ChannelManager.html).

## R11 — Session

- Laravel surface: `Illuminate\Contracts\Session\Session`, session store and HTTP middleware.
- Naravel: future Session module and ASP.NET Core integration.
- Value: framework-neutral session access and consistent middleware integration where ASP.NET defaults are insufficient.
- Native baseline: ASP.NET Core `ISession` and session middleware first.
- API reference: [Illuminate Session](https://api.laravel.com/docs/13.x/Illuminate/Session/Store.html).

## R12 — Auth-lite and deferred middleware

- Laravel surface: guards, user resolution, `auth`, `can`, `verified`, password confirmation.
- Naravel: future minimal auth integration, depending on Session and Routing.
- Value: concise composition over ASP.NET Core Identity/authentication/authorization, not a replacement identity system.
- Native baseline: ASP.NET Core Authentication, Authorization and Identity.
- API reference: [Illuminate Auth](https://api.laravel.com/docs/13.x/Illuminate/Auth/AuthManager.html).

## R13 — Console and Scheduling

- Laravel surface: Artisan commands and `Illuminate\Console\Scheduling\Schedule`.
- Naravel: future command/scheduling module; route:list and maintenance commands depend on this decision.
- Value: typed command registration and hosted scheduling only if .NET hosting does not already meet the need.
- Native baseline: `System.CommandLine`, Generic Host, hosted services, and deployment schedulers.
- API reference: [Illuminate Console Scheduling](https://api.laravel.com/docs/13.x/Illuminate/Console/Scheduling/Schedule.html).

## R14 — Broadcasting

- Laravel surface: `Illuminate\Contracts\Broadcasting\Factory` and broadcaster channels.
- Naravel: future Broadcasting module after Events/Queue.
- Value: typed real-time event delivery behind provider-specific transports.
- Native baseline: ASP.NET Core SignalR.
- API reference: [Illuminate Broadcasting](https://api.laravel.com/docs/13.x/Illuminate/Contracts/Broadcasting/Factory.html).

## R15 — Hashing

- Laravel surface: `Illuminate\Contracts\Hashing\Hasher`, password hash/verify.
- Naravel: future small typed service around approved .NET password hashing functionality.
- Value: consistent secure password verification/configuration without duplicating cryptography.
- Native baseline: `Microsoft.AspNetCore.Identity.IPasswordHasher<TUser>` and .NET cryptographic APIs.
- API reference: [Illuminate Hashing](https://api.laravel.com/docs/13.x/Illuminate/Contracts/Hashing/Hasher.html).

## R16 — Pagination

- Laravel surface: `Illuminate\Contracts\Pagination\Paginator`, `LengthAwarePaginator` and cursor pagination.
- Naravel: future typed pagination metadata/query helpers.
- Value: stable API response metadata and safe cursor-based large result traversal.
- Native baseline: LINQ, EF Core query operators, and ASP.NET Core response conventions.
- API reference: [Illuminate Pagination](https://api.laravel.com/docs/13.x/Illuminate/Contracts/Pagination/Paginator.html).

## R17 — Release hardening

- Laravel surface: no single class; validate parity claims across the implemented public surface.
- Naravel: package/API review, dependency audit, docs, samples, CI and compatibility evidence.
- Value: reliable release and honest claims rather than broad unchecked parity.
- Native baseline: NuGet, .NET analyzers, CI matrix and dependency audit tooling.
- API reference: review the Laravel API pages named by completed stages only.

## R18 — Encryption

- Laravel surface: `Illuminate\Encryption\Encrypter`, `Crypt`, `ShouldBeEncrypted` queue jobs.
- Naravel: future thin facade over ASP.NET Core Data Protection plus an optional Queue adapter.
- Value: one-line encrypt/decrypt and encrypted job payloads without custom cryptography.
- Native baseline: `IDataProtector` / `IDataProtectionProvider`; never implement cryptographic primitives.
- API reference: [Illuminate Encryption](https://api.laravel.com/docs/13.x/Illuminate/Encryption/Encrypter.html).

## Coverage — all Laravel 13 `Illuminate\*` namespaces (REVIEW-01)

Disposition only; each real adoption still needs its PDR. `Native` = .NET already solves it, nothing to build.

| Namespace | Disposition | Where |
|---|---|---|
| Auth | Adapt thin over ASP.NET Core Identity/Authz | R12 |
| Broadcasting | Adapt over SignalR | R14 |
| Bus (chains, batches) | Part of Queue | R00, R07 |
| Cache | Done (+RateLimiter open) | R04 |
| Concurrency | Native (`Task.WhenAll`, `Parallel`) | - |
| Config | Native (`IConfiguration`, `IOptionsMonitor`) | PDR-004 |
| Console | Evaluate vs `System.CommandLine`/Generic Host | R13 |
| Container | Native (`Microsoft.Extensions.DependencyInjection`) | PDR-002 |
| Contracts | Per-module interfaces | - |
| Cookie | Native (ASP.NET Core) | - |
| Database (Eloquent, migrations) | Native (EF Core); out of scope | BACKLOG |
| Encryption | Thin facade over Data Protection (OD-03) | R18 |
| Events | Adapt | R06 |
| Filesystem | Done | R02 |
| Foundation | Manager done; Application/Kernel = Native | PDR-002 |
| Hashing | Likely Native (`IPasswordHasher`); PDR may DECLINE | R15 |
| Http (request/response) | Native ASP.NET Core; **HTTP client**: Native `HttpClientFactory` | - |
| Image | Unassessed | BACKLOG |
| JsonSchema | Native (`System.Text.Json.Schema`) | - |
| Log | Native (`ILogger`) | - |
| Mail | Evaluate vs MailKit/FluentEmail | R09 |
| Notifications | Adapt | R10 |
| Pagination | Adapt (cursor value) | R16 |
| Pipeline | Native (delegates/middleware); revisit if Routing needs shared engine | - |
| Process | Native (`System.Diagnostics.Process`) | - |
| Queue | Done core; completion open | R00, R07, R08 |
| Redis | Adapt shared connections | R03 |
| Routing | 4a done; 4b open | R05 |
| Session | Likely thin over `ISession`; PDR may DECLINE | R11 |
| Support (Str, Collection, helpers) | Native; no port | BACKLOG |
| Testing (`*::fake`) | Fake per module (DoD S3) | every stage; Queue in R07 |
| Translation | Native (`IStringLocalizer`) | - |
| Validation / Form Requests | Not built (OD-04): document FluentValidation/DataAnnotations | BACKLOG |
| View | Native (Razor/Minimal API) | - |
