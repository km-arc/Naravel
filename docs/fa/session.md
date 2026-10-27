# Session

**Naravel بستهٔ session ندارد.** ASP.NET Core آنچه session در Laravel می‌کند را از قبل انجام می‌دهد، پس چیزی پورت نمی‌شود ([PDR-015](../pdr/fa/PDR-015-session.md)، وضعیت `DECLINED`). این صفحه همان دستورالعملی است که جای بسته را می‌گیرد. نسخهٔ انگلیسی: [docs/en/session.md](../en/session.md).

## شروع سریع

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnectionString);   // یا AddDistributedMemoryCache()
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromMinutes(30);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;     // فقط با مبنای قانونی: cookieهای ضروری از بررسی رضایت رد می‌شوند
});
app.UseSession();                    // پیش از UseNaravelRouting و هر endpointی که session را می‌خواند

app.MapGet("/cart", (HttpContext c) => c.Session.GetInt32("items") ?? 0);
```

`ISession` بایت ذخیره می‌کند و کمک‌کننده‌های `GetString`/`SetString`/`GetInt32`/`SetInt32` دارد. store آن هر `IDistributedCache` است، پس عوض کردن store یک خط ثبت است.

## Laravel به .NET

| Laravel | .NET |
|---|---|
| `session()->get/put/forget/pull` | `Session.GetString/SetString/Remove` (`pull` یک خواندن و یک `Remove` است) |
| `session()->flush()` | `Session.Clear()` |
| driverهای session (`array`، `redis`، `database`) | `IDistributedCache`‌ای که ثبت می‌کنید: `AddDistributedMemoryCache`، `AddStackExchangeRedisCache`، `AddDistributedSqlServerCache` |
| `flash`، `reflash`، `keep` | `TempData` (MVC و Razor Pages)، یا دستورالعمل minimal API پایین |
| `old('email')`، `withInput()` | `ModelState` و tag helperها (MVC و Razor Pages)؛ Blazor حالت کامپوننت را نگه می‌دارد |
| `session()->regenerate()` / `invalidate()` | `Session.Clear()` و ورود دوباره؛ cookie احراز هویت بلیت خودش را دارد |
| `session()->token()`، `@csrf` | `AddAntiforgery` |

## مثال کامل

یک شمارندهٔ سبد خرید، یک پیام یک‌بارمصرف و یک بازنشانی، با معادل Laravel هر خط.

```csharp
// Program.cs
builder.Services.AddDistributedMemoryCache();                  // use AddStackExchangeRedisCache(...) with more than one instance
builder.Services.AddSession(o => o.IdleTimeout = TimeSpan.FromMinutes(30));
var app = builder.Build();
app.UseSession();                                              // before UseNaravelRouting and the endpoints

app.MapPost("/cart/add", (HttpContext c) =>
{
    var items = (c.Session.GetInt32("items") ?? 0) + 1;        // session()->get('items', 0)
    c.Session.SetInt32("items", items);                        // session()->put
    return Results.Ok(new { items });
});

app.MapGet("/cart", (HttpContext c) => Results.Ok(new { items = c.Session.GetInt32("items") ?? 0 }));

app.MapGet("/notice", (HttpContext c) =>                       // session()->pull('notice')
{
    var notice = c.Session.GetString("notice");
    c.Session.Remove("notice");
    return Results.Ok(new { notice });
});

app.MapPost("/cart/clear", (HttpContext c) =>
{
    c.Session.Clear();                                         // session()->flush()
    return Results.NoContent();
});
```

کوکی را در هر درخواست برگردانید (مرورگر خودکار این کار را می‌کند؛ با `curl` از `-c jar -b jar` استفاده کنید).

## flash data برای minimal APIها

`TempData` به MVC یا Razor Pages نیاز دارد. minimal API با چند خط flash data یک‌درخواستی می‌گیرد. این قطعه‌کدی برای کپی است، نه API پشتیبانی‌شده، و این مخزن آن را کامپایل نمی‌کند.

```csharp
app.UseSession();
app.Use(async (ctx, next) =>
{
    var s = ctx.Session;
    await s.LoadAsync(ctx.RequestAborted);
    foreach (var k in s.Keys.Where(k => k.StartsWith("flash:current:")).ToList()) s.Remove(k);   // flash درخواست قبلی حالا رفته است
    foreach (var k in s.Keys.Where(k => k.StartsWith("flash:next:")).ToList())                   // آنچه دفعهٔ قبل flash شده بود ارتقا می‌یابد
    {
        s.SetString("flash:current:" + k["flash:next:".Length..], s.GetString(k)!);
        s.Remove(k);
    }
    await next();
});

// نوشتن: ctx.Session.SetString("flash:next:status", "Saved");   خواندن: ctx.Session.GetString("flash:current:status")
```

## قاعده‌هایی که مهم‌اند

- **بیش از یک نمونه:** store مشترک (Redis، SQL Server) به کار ببرید. با `AddDistributedMemoryCache` هر نمونه sessionهای خودش را دارد و کاربر بین نمونه‌ها پرت می‌شود.
- **Cookie:** `HttpOnly` بماند؛ `SameSite` را آگاهانه تنظیم کنید؛ `IsEssential` فقط وقتی session کاملاً ضروری است.
- **راز در session نگذارید** مگر store مورد اعتماد باشد؛ شناسه در cookie است ولی داده در store.

## آنچه پوشش نمی‌دهد

- **store‌ی session روی `Naravel.Cache`.** `IDistributedCache` انقضای لغزان دارد و `ICacheStore` ندارد، پس adapter پرسش طراحی خودش را دارد. در backlog است؛ هنوز کسی نخواسته است.
- **رمزنگاری cookie.** Data Protection به‌طور پیش‌فرض cookie‌ی session را محافظت می‌کند؛ اشتراک کلیدها بین نمونه‌ها در [docs/fa/encryption.md](encryption.md) آمده است.
