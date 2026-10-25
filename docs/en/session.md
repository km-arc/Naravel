# Session

**Naravel has no session package.** ASP.NET Core already does what Laravel's session does, so nothing is ported ([PDR-015](../pdr/en/PDR-015-session.md), status `DECLINED`). This page is the recipe that replaces it. Persian version: [docs/fa/session.md](../fa/session.md).

## Quickstart

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnectionString);   // or AddDistributedMemoryCache()
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromMinutes(30);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;     // only with a legal basis: essential cookies skip the consent check
});
app.UseSession();                    // before UseNaravelRouting and any endpoint that reads the session

app.MapGet("/cart", (HttpContext c) => c.Session.GetInt32("items") ?? 0);
```

`ISession` stores bytes with `GetString`/`SetString`/`GetInt32`/`SetInt32` helpers. Its store is any `IDistributedCache`, so changing the store is one registration line.

## Laravel to .NET

| Laravel | .NET |
|---|---|
| `session()->get/put/forget/pull` | `Session.GetString/SetString/Remove` (`pull` is a read and a `Remove`) |
| `session()->flush()` | `Session.Clear()` |
| Session drivers (`array`, `redis`, `database`) | The `IDistributedCache` you register: `AddDistributedMemoryCache`, `AddStackExchangeRedisCache`, `AddDistributedSqlServerCache` |
| `flash`, `reflash`, `keep` | `TempData` (MVC and Razor Pages), or the minimal-API recipe below |
| `old('email')`, `withInput()` | `ModelState` and the tag helpers (MVC and Razor Pages); Blazor keeps component state |
| `session()->regenerate()` / `invalidate()` | `Session.Clear()` and a new sign-in; the authentication cookie has its own ticket |
| `session()->token()`, `@csrf` | `AddAntiforgery` |

## Flash data for minimal APIs

`TempData` needs MVC or Razor Pages. A minimal API can get one-request flash data in a few lines. It is a snippet to copy, not a supported API, and it is not compiled by this repository.

```csharp
app.UseSession();
app.Use(async (ctx, next) =>
{
    var s = ctx.Session;
    await s.LoadAsync(ctx.RequestAborted);
    foreach (var k in s.Keys.Where(k => k.StartsWith("flash:current:")).ToList()) s.Remove(k);   // last request's flash is gone now
    foreach (var k in s.Keys.Where(k => k.StartsWith("flash:next:")).ToList())                   // promote what was flashed last time
    {
        s.SetString("flash:current:" + k["flash:next:".Length..], s.GetString(k)!);
        s.Remove(k);
    }
    await next();
});

// write: ctx.Session.SetString("flash:next:status", "Saved");   read: ctx.Session.GetString("flash:current:status")
```

## Rules that matter

- **More than one instance:** use a shared store (Redis, SQL Server). With `AddDistributedMemoryCache` each instance has its own sessions and a user bounces between them.
- **Cookie:** keep `HttpOnly`; set `SameSite` deliberately; `IsEssential` only when the session is strictly required.
- **Never put secrets in a session** unless the store is trusted; the id lives in a cookie but the data lives in the store.

## What this does not cover

- **A session store on `Naravel.Cache`.** `IDistributedCache` has sliding expiry and `ICacheStore` does not, so an adapter has its own design question. It is in the backlog; nobody has asked for it yet.
- **Cookie encryption.** Data Protection protects the session cookie by default; sharing keys across instances is covered in [docs/en/encryption.md](encryption.md).
