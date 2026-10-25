# میان‌افزار احراز هویت (`auth`، `guest`، `can`، `verified`)

**Naravel بستهٔ auth ندارد و aliasهای `auth`، `can` و `verified` را هم ندارد.** احراز هویت و مجوزدهی ASP.NET Core از قبل به‌صورت metadata به route وصل می‌شوند و `Naravel.Routing` همان builder را در دسترس می‌گذارد ([PDR-016](../pdr/fa/PDR-016-auth-lite.md)، وضعیت `DECLINED`). alias داخلی `guest` می‌ماند. این صفحه دستورالعمل است. نسخهٔ انگلیسی: [docs/en/auth.md](../en/auth.md).

## شروع سریع

```csharp
builder.Services.AddAuthentication().AddCookie();                       // یا AddJwtBearer(...)
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("verified", p => p.RequireClaim("email_verified", "true"));
    o.AddPolicy("can-edit-post", p => p.RequireAssertion(ctx => ctx.User.IsInRole("editor")));
});
app.UseAuthentication(); app.UseAuthorization(); app.UseNaravelRouting();   // همین ترتیب

app.MapNaravel(r =>
{
    r.Get("/dashboard", Dashboard).Configure(b => b.RequireAuthorization());                 // auth
    r.Get("/billing", Billing).Configure(b => b.RequireAuthorization("verified"));           // auth + verified
    r.Post("/posts/{post}", Update).Configure(b => b.RequireAuthorization("can-edit-post")); // can:update
    r.Get("/login", Login).Middleware("guest");                                               // guest (داخلی)
});
```

## Laravel به .NET

| Laravel | .NET |
|---|---|
| `auth` | `RequireAuthorization()` |
| `auth:sanctum`، `auth:api` (یک guard) | یک scheme احراز هویت (`AddCookie`، `AddJwtBearer`) و `RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "..." })` |
| `guest` | `.Middleware("guest")` (داخلی، به کاربر واردشده 403 می‌دهد، بدون redirect) |
| `can:update,post` | یک policy نام‌دار با `RequireAuthorization("policy")`؛ برای منبع بارگذاری‌شده داخل handler `IAuthorizationService.AuthorizeAsync(user, post, "policy")` را صدا بزنید |
| `verified` | policy‌ای که claim‌ی تنظیم‌شده توسط کد ورود شما را بطلبد؛ Identity هم می‌تواند هنگام ورود ایمیل تأییدشده را اجباری کند (`RequireConfirmedEmail`) |
| `password.confirm` | policy‌ای که claim‌ی ورود اخیر را بطلبد، که endpoint تأیید شما تنظیم می‌کند |
| گروه‌های مسیر با میان‌افزار | `MapGroup("/admin").RequireAuthorization()` |

## 401 یا 403 یا redirect

scheme احراز هویت پیکربندی‌شده تصمیم می‌گیرد، نه Naravel: scheme‌ی cookie به مسیر ورود redirect می‌کند (challenge) یا به مسیر دسترسی-ممنوع (forbid)، و scheme‌ی bearer با `WWW-Authenticate` پاسخ 401 یا 403 می‌دهد. به همین دلیل alias‌ی `auth` وجود ندارد: aliasی که خودش `context.User` را بررسی کند مسیر مجوزدهی دومی با کدهای وضعیت خودش می‌شد، بیرون از challenge‌ی scheme و ارزیاب policy، و در OpenAPI دیده نمی‌شد.

## آنچه پوشش نمی‌دهد

- **Identity.** کاربر، نقش، تأیید ایمیل و endpointهای توکن همان ASP.NET Core Identity است (`AddIdentity`، `AddIdentityApiEndpoints`).
- **هش کردن گذرواژه** در [docs/fa/hashing.md](hashing.md) و **session** در [docs/fa/session.md](session.md) است.
- **alias‌ی `auth` فقط-metadata** (املای Laravel برای `RequireAuthorization`) به‌عنوان ایده در backlog است، نه برنامه‌ریزی‌شده.
