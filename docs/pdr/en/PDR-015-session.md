# PDR-015 — Session (`ISession`, flash data, old input)

**Native-first verdict:** yes, plain .NET already does this. `services.AddDistributedMemoryCache(); services.AddSession(); app.UseSession();` is three lines and gives a cookie-keyed `ISession` backed by any `IDistributedCache` (memory, Redis through `Microsoft.Extensions.Caching.StackExchangeRedis`, SQL Server). Flash data is `TempData`, and old input is model binding plus `ModelState`. **Proposed outcome: `DECLINED`, with a documented recipe.**

Status: **Approved 2026-10-25.**

## Context

Laravel's session (`Illuminate\Session\Store`, the `StartSession` middleware) offers: a driver-based store (`file`, `cookie`, `database`, `redis`, `array`), `get`/`put`/`forget`/`pull`, **flash data** (`flash('status', 'Saved')`, available on the next request only, `reflash`, `keep`), **old input** (`withInput()` then `old('email')` after a validation redirect), `regenerate`/`invalidate`, and a CSRF token.

What ASP.NET Core provides:

- **`ISession`** (`Microsoft.AspNetCore.Session`): `Get`/`Set` of bytes with `GetString`/`SetString`/`GetInt32`/`SetInt32` extensions, `Remove`, `Clear`, `Id`, `LoadAsync`/`CommitAsync`, a cookie-based session id, idle timeout, and a cookie policy. Its backing store is `IDistributedCache`.
- **Stores:** `AddDistributedMemoryCache` (single instance), `AddStackExchangeRedisCache` (shared), `AddDistributedSqlServerCache`. Switching store is one registration line.
- **Flash data:** `TempData` (`ITempDataDictionary`), with a cookie provider and a session provider, values readable once and kept on request (`Keep`, `Peek`). It ships with MVC and Razor Pages.
- **Old input:** MVC/Razor tag helpers and `ModelState` redisplay submitted values after a failed validation; Blazor keeps component state.
- **CSRF:** the anti-forgery services (`AddAntiforgery`).
- **Session fixation (`regenerate`):** a new session id is issued by clearing the session and deleting its cookie, and authentication cookies carry their own ticket.

Where `ISession` really lacks something: flash data and old input are **not** available for minimal-API handlers (they live in the MVC/Razor feature set), and an application that wants "the same Cache store configuration feeds sessions" has to register a separate `IDistributedCache`. Neither is a large gap: a 20-line `ISession` extension gives one-request flash data to minimal APIs, and Naravel's own target (JSON and minimal-API services, per OD-08) rarely renders server-side redirects with old input.

## Recipe (the documentation that replaces a package)

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnectionString);   // or AddDistributedMemoryCache()
builder.Services.AddSession(o => { o.IdleTimeout = TimeSpan.FromMinutes(30); o.Cookie.HttpOnly = true; o.Cookie.IsEssential = true; });
app.UseSession();   // before UseNaravelRouting / endpoints that read the session
```

Flash data for minimal APIs, if wanted, is a few lines over `ISession` (write the value under a `flash:` key, move it to a `flash:old` key on the next request, remove it after it is read); the docs show it as a snippet, not a supported API.

## Proposed decision

1. Do not add a `Naravel.Session` package. Record `DECLINED` with this PDR as the reason.
2. Document the recipe in `docs/en|fa/session.md` (EN and FA together): the registration above, store choices, the Laravel to .NET table (`flash` → `TempData`, `old()` → `ModelState`, session drivers → `IDistributedCache` implementations, `regenerate` → new id, CSRF → anti-forgery), the cookie settings that matter (`HttpOnly`, `SameSite`, `IsEssential` with consent), and the multi-instance rule (use a shared distributed cache, not memory).
3. Update the parity matrix row to *Native*, the changelog and the backlog (an `IDistributedCache` adapter over `Naravel.Cache` is recorded there as an idea).
4. No code, tests or benchmark are added: a test of `ISession` would test Microsoft's code.
5. Downstream: the auth stage no longer depends on a Naravel session package.

## Alternatives considered

1. **A `Naravel.Session` package with `Flash()` and `Old()` helpers.** Rejected: it works only where MVC `TempData` already exists, and a minimal-API app can get flash data with a short snippet. It fails rule zero.
2. **An `IDistributedCache` adapter over `ICacheStore`** (so `Cache:Stores` also configures sessions). Not rejected on value, but deferred to the backlog: it is a small adapter with its own semantics question (`IDistributedCache` has sliding expiry, `ICacheStore` does not), and nobody has asked for it. It can become a one-task stage later.
3. **Our own session store and cookie.** Rejected: security-sensitive code the project does not write.

## Decisions for the owner

- **D1:** `DECLINED` with the recipe (recommended), or the thin `Flash()`/`Old()` package (alternative 1).
- **D2:** record the `IDistributedCache` adapter in the backlog only (recommended), or make it a stage now.

## Approval

The owner approved D1 and D2 as recommended on 2026-10-25, after the first review asked for the decisions to be confirmed explicitly. D1 `DECLINED` with the recipe in `docs/en|fa/session.md`: `ISession` over `IDistributedCache`, `TempData` and `ModelState` already cover flash data and old input for MVC and Razor Pages, and a few lines cover minimal APIs; a thin `Flash()`/`Old()` package would only work where `TempData` already exists. D2 the `IDistributedCache` adapter over `Naravel.Cache` is recorded in the backlog only: it raises its own question (sliding expiry) and nobody has asked for it.
