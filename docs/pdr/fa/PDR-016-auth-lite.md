# PDR-016 — میان‌افزارهای مسیر برای احراز هویت (`auth`، `guest`، `can`، `verified`)

**داوری با اولویت .NET بومی:** بله، .NET ساده این کار را می‌کند. احراز هویت و مجوزدهی ASP.NET Core از قبل به‌صورت metadata به endpoint وصل می‌شوند: `RequireAuthorization()` و `RequireAuthorization("policy")` روی endpoint، و `AllowAnonymous()` و `MapGroup(...).RequireAuthorization()` برای گروه‌ها. `Naravel.Routing` همین builder بومی را از قبل در دسترس می‌گذارد (`NaravelRoute.Configure(b => b.RequireAuthorization("policy"))`) و alias‌ی `guest` را هم دارد. **نتیجه‌ی پیشنهادی: `DECLINED`، با دستورالعمل مستند و جدول.**

وضعیت: **تأییدشده ۲۰۲۶-۱۰-۲۵.**

## زمینه

نام‌های میان‌افزار مسیر در Laravel: `auth` (اگر کاربر وارد نشده باشد redirect یا 401)، `auth:sanctum` (یک guard)، `guest` (اگر وارد شده باشد redirect)، `can:update,post` (بررسی policy)، `verified` (ایمیل تأییدشده) و `password.confirm`. این‌ها aliasهای رشته‌ای HTTP kernel هستند و به‌صورت `Route::middleware('auth')` به کار می‌روند.

آنچه ASP.NET Core به‌صورت داخلی می‌دهد:

- **احراز هویت:** `AddAuthentication().AddCookie()/AddJwtBearer()` و `UseAuthentication()`؛ Identity (`AddIdentity`/`AddIdentityApiEndpoints`) کاربر، نقش، تأیید ایمیل و endpointهای توکن را اضافه می‌کند. درخواست ناموفق را scheme احراز هویت **challenge** (401 یا redirect به مسیر ورود) یا **forbid** (403) می‌کند، و scheme می‌داند برای هر نوع client چگونه جواب دهد (redirect برای cookie، هدر `WWW-Authenticate` برای bearer).
- **مجوزدهی:** `AddAuthorization(o => o.AddPolicy("CanEdit", p => p.RequireClaim(...)))`، `IAuthorizationService` برای بررسی مبتنی بر منبع (`can:update,post`)، و metadata‌ی `[Authorize]` و `RequireAuthorization` که `UseAuthorization()` ارزیابی می‌کند.
- **فقط ناشناس (`guest`):** چیز داخلی ندارد، ولی یک خط است: یک requirement که برای کاربر احراز‌شده شکست بخورد.
- **`verified`:** policy‌ای که claim‌ی تنظیم‌شده توسط کد ورود شما را می‌طلبد (Identity هم می‌تواند هنگام ورود ایمیل تأییدشده را اجباری کند، `RequireConfirmedEmail`).

نکات تعیین‌کننده از `Naravel.Routing`:

- میان‌افزار مسیر داخل **یک** میان‌افزار ASP.NET یعنی `NaravelRoutingMiddleware` اجرا می‌شود که بعد از routing قرار دارد. `UseAuthorization()` ASP.NET هم بعد از routing اجرا می‌شود و metadata‌ی endpoint را می‌خواند. اگر alias‌ی Naravel به نام `auth` خودش `context.User` را بررسی می‌کرد، یک **مکانیزم مجوزدهی دومِ موازی** می‌شد: به شیوه‌ی خودش 401 می‌داد (نه از طریق challenge‌ی scheme پیکربندی‌شده، یعنی بدون redirect‌ی cookie و بدون `WWW-Authenticate`)، بسته به ترتیبی که برنامه انتخاب کرده بود قبل یا بعد از `UseAuthorization()` اجرا می‌شد، و در OpenAPI یا metadata‌ی endpoint دیده نمی‌شد. aliasی که **فقط metadata اضافه کند** (`auth` = `RequireAuthorization()`) درست است ولی دقیقاً همان `Configure(b => b.RequireAuthorization())` است و شکل رشته‌ای آن پنهان می‌کند کدام policy اجرا می‌شود.
- alias داخلی `guest` وجود دارد و می‌ماند: برای کاربر احراز‌شده 403 برمی‌گرداند و redirect نمی‌کند (قبلاً مستند شده است).

## دستورالعمل (مستنداتی که جای بسته را می‌گیرد)

```csharp
builder.Services.AddAuthentication().AddCookie();                       // یا AddJwtBearer(...)
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("verified", p => p.RequireClaim("email_verified", "true"));
    o.AddPolicy("can-edit-post", p => p.RequireAssertion(ctx => ctx.User.IsInRole("editor")));
});
app.UseAuthentication(); app.UseAuthorization(); app.UseNaravelRouting();

app.MapNaravel(r =>
{
    r.Get("/dashboard", Dashboard).Configure(b => b.RequireAuthorization());                 // auth
    r.Get("/billing", Billing).Configure(b => b.RequireAuthorization("verified"));           // auth + verified
    r.Post("/posts/{post}", Update).Configure(b => b.RequireAuthorization("can-edit-post")); // can:update
    r.Get("/login", Login).Middleware("guest");                                               // guest (داخلی)
});
```

برای بررسی مبتنی بر منبع (`can:update,post`) داخل handler، جایی که منبع قبلاً بارگذاری شده، `IAuthorizationService.AuthorizeAsync(user, post, "policy")` را صدا بزنید. گروه‌های مسیر از `MapGroup("/admin").RequireAuthorization()` بومی استفاده می‌کنند.

## تصمیم پیشنهادی

1. aliasهای `auth`، `can`، `verified` و بسته‌ی `Naravel.Auth` اضافه نمی‌شوند. `DECLINED` با همین PDR به‌عنوان دلیل ثبت می‌شود. alias موجود `guest` نگه داشته و مستند می‌شود.
2. دستورالعمل در `docs/en|fa/auth.md` (EN و FA با هم) مستند می‌شود: ترتیب ثبت (`UseAuthentication`، `UseAuthorization` و بعد `UseNaravelRouting`)، جدولی از نام‌های Laravel (`auth`، `auth:guard`، `guest`، `can`، `verified`، `password.confirm`) به .NET، رفتار 401 در برابر 403 در برابر redirect برای scheme انتخاب‌شده، و یادداشتی که هش کردن گذرواژه در دستورالعمل hashing است.
3. ردیف ماتریس parity به *Native*، changelog و backlog به‌روز می‌شوند (alias‌ی `auth` فقط-metadata به‌عنوان ایده ثبت می‌شود).
4. کد، آزمون یا benchmark اضافه نمی‌شود: آزمونِ `RequireAuthorization` کد مایکروسافت را آزمون می‌کرد.

## گزینه‌های بررسی‌شده

1. **aliasهایی که metadata به endpoint اضافه می‌کنند** (`.Middleware("auth")`، `.Middleware("can:edit-post")` ← `RequireAuthorization(...)`). از نظر فنی درست و کوچک. به‌عنوان پیش‌فرض رد شد: یک فراخوان بومی را به رشته تبدیل می‌کند، پنهان می‌کند کدام policy اجرا می‌شود، و نسخه‌ی نادرست زیر را تشویق می‌کند. اگر مالک املای Laravel را بخواهد بعداً قابل افزودن است.
2. **aliasهایی که `context.User` را بررسی و 401/403 برمی‌گردانند.** رد شد: مسیر مجوزدهی دوم با کدهای وضعیت خودش، که challenge‌ی scheme پیکربندی‌شده و ارزیاب policy را دور می‌زند و در OpenAPI دیده نمی‌شود.
3. **سیستم guard** (`auth:web`، `auth:api`). رد شد: schemeهای احراز هویت ASP.NET Core همان guardها هستند (`AddCookie`، `AddJwtBearer`؛ `RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "..." })`).

## تصمیم‌ها برای مالک

- **D1:** `DECLINED` با دستورالعمل (توصیه‌شده)، یا aliasهای فقط-metadata از گزینه‌ی ۱.
- **D2:** `guest` همان‌طور که هست بماند (توصیه‌شده).

## تأیید

مالک D1 و D2 را در ۲۰۲۶-۱۰-۲۵ همان‌طور که توصیه شده بود تأیید کرد، پس از آنکه در بررسی نخست تأیید صریح تصمیم‌ها خواسته شد. D1 `DECLINED` با دستورالعمل در `docs/en|fa/auth.md`: `RequireAuthorization` از طریق `NaravelRoute.Configure` مسیر بومی است، و alias‌ی `auth` که خودش `context.User` را بررسی کند مسیر مجوزدهی دومی با کدهای وضعیت خودش می‌شد، بیرون از challenge‌ی scheme و نادیدنی برای OpenAPI. D2 alias داخلی `guest` همان‌طور که هست می‌ماند.
