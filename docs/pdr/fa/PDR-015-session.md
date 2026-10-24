# PDR-015 — Session (`ISession`، flash data، old input)

**داوری با اولویت .NET بومی:** بله، .NET ساده از قبل این را انجام می‌دهد. `services.AddDistributedMemoryCache(); services.AddSession(); app.UseSession();` سه خط است و یک `ISession` با کلید cookie می‌دهد که روی هر `IDistributedCache` (حافظه، Redis با `Microsoft.Extensions.Caching.StackExchangeRedis`، SQL Server) سوار است. flash data همان `TempData` است و old input همان model binding به‌علاوه‌ی `ModelState`. **نتیجه‌ی پیشنهادی: `DECLINED`، با دستورالعمل مستند.**

وضعیت: **پیشنهادی، منتظر تأیید مالک.**

## زمینه

session در Laravel (`Illuminate\Session\Store` و middleware به نام `StartSession`) این‌ها را دارد: store مبتنی بر driver (`file`، `cookie`، `database`، `redis`، `array`)، `get`/`put`/`forget`/`pull`، **flash data** (`flash('status', 'Saved')` که فقط در درخواست بعدی در دسترس است، `reflash`، `keep`)، **old input** (`withInput()` و بعد `old('email')` پس از redirect اعتبارسنجی)، `regenerate`/`invalidate` و توکن CSRF.

آنچه ASP.NET Core می‌دهد:

- **`ISession`** (`Microsoft.AspNetCore.Session`): `Get`/`Set` بایت با extensionهای `GetString`/`SetString`/`GetInt32`/`SetInt32`، `Remove`، `Clear`، `Id`، `LoadAsync`/`CommitAsync`، شناسه‌ی session در cookie، idle timeout و سیاست cookie. store پشتش `IDistributedCache` است.
- **storeها:** `AddDistributedMemoryCache` (یک نمونه)، `AddStackExchangeRedisCache` (مشترک)، `AddDistributedSqlServerCache`. عوض کردن store یک خط ثبت است.
- **flash data:** `TempData` (`ITempDataDictionary`) با provider مبتنی بر cookie و session، مقدارها یک بار خوانده می‌شوند و با درخواست نگه داشته می‌شوند (`Keep`، `Peek`). همراه MVC و Razor Pages می‌آید.
- **old input:** tag helperهای MVC/Razor و `ModelState` مقدارهای ارسال‌شده را پس از اعتبارسنجی ناموفق دوباره نشان می‌دهند؛ Blazor حالت کامپوننت را نگه می‌دارد.
- **CSRF:** سرویس‌های anti-forgery (`AddAntiforgery`).
- **session fixation (`regenerate`):** شناسه‌ی session جدید با پاک کردن session و حذف cookie آن صادر می‌شود، و cookieهای احراز هویت بلیت خودشان را دارند.

جایی که `ISession` واقعاً چیزی کم دارد: flash data و old input برای handlerهای minimal API **در دسترس نیستند** (در مجموعه‌ی قابلیت‌های MVC/Razor هستند) و برنامه‌ای که بخواهد «همان پیکربندی store در Cache به sessionها هم برسد» باید `IDistributedCache` جدا ثبت کند. هیچ‌کدام شکاف بزرگی نیست: یک extension بیست‌خطی روی `ISession` flash data یک‌درخواستی را به minimal APIها می‌دهد، و هدف خود Naravel (سرویس‌های JSON و minimal API، طبق OD-08) به‌ندرت redirectهای سمت سرور با old input رندر می‌کند.

## دستورالعمل (مستنداتی که جای بسته را می‌گیرد)

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnectionString);   // یا AddDistributedMemoryCache()
builder.Services.AddSession(o => { o.IdleTimeout = TimeSpan.FromMinutes(30); o.Cookie.HttpOnly = true; o.Cookie.IsEssential = true; });
app.UseSession();   // پیش از UseNaravelRouting / endpointهایی که session را می‌خوانند
```

flash data برای minimal APIها، اگر لازم شد، چند خط روی `ISession` است (مقدار را زیر کلید `flash:` بنویسید، در درخواست بعدی به کلید `flash:old` منتقلش کنید و بعد از خوانده شدن حذفش کنید)؛ مستندات آن را به‌صورت قطعه‌کد نشان می‌دهند، نه API پشتیبانی‌شده.

## تصمیم پیشنهادی

1. بسته‌ی `Naravel.Session` اضافه نمی‌شود. `DECLINED` با همین PDR به‌عنوان دلیل ثبت می‌شود.
2. دستورالعمل در `docs/en|fa/session.md` مستند می‌شود (EN و FA با هم): ثبت بالا، انتخاب‌های store، جدول Laravel به .NET (`flash` ← `TempData`، `old()` ← `ModelState`، driverهای session ← پیاده‌سازی‌های `IDistributedCache`، `regenerate` ← شناسه‌ی تازه، CSRF ← anti-forgery)، تنظیمات cookie که مهم‌اند (`HttpOnly`، `SameSite`، `IsEssential` با رضایت) و قاعده‌ی چندنمونه (cache توزیع‌شده‌ی مشترک، نه حافظه).
3. ردیف ماتریس parity به *Native* و changelog و backlog به‌روز می‌شوند (adapter‌ی `IDistributedCache` روی `Naravel.Cache` آنجا به‌عنوان ایده ثبت می‌شود).
4. کد، آزمون یا benchmark اضافه نمی‌شود: آزمون `ISession` کد مایکروسافت را آزمون می‌کرد.
5. پایین‌دست: مرحله‌ی auth دیگر به بسته‌ی session در Naravel وابسته نیست.

## گزینه‌های بررسی‌شده

1. **بسته‌ی `Naravel.Session` با کمک‌کننده‌های `Flash()` و `Old()`.** رد شد: فقط جایی کار می‌کند که `TempData` در MVC از قبل هست، و برنامه‌ی minimal API با قطعه‌کدی کوتاه flash data می‌گیرد. قاعده‌ی صفر را می‌شکند.
2. **adapter‌ی `IDistributedCache` روی `ICacheStore`** (تا `Cache:Stores` sessionها را هم پیکربندی کند). از نظر ارزش رد نشد ولی به backlog رفت: adapter کوچکی است با پرسش معنایی خودش (`IDistributedCache` انقضای لغزان دارد و `ICacheStore` ندارد) و کسی نخواسته است. بعداً می‌تواند مرحله‌ی یک‌تسکی شود.
3. **store و cookie session خودمان.** رد شد: کد حساس امنیتی که پروژه نمی‌نویسد.

## تصمیم‌ها برای مالک

- **D1:** `DECLINED` با دستورالعمل (توصیه‌شده)، یا بسته‌ی نازک `Flash()`/`Old()` (گزینه‌ی ۱).
- **D2:** adapter‌ی `IDistributedCache` فقط در backlog ثبت شود (توصیه‌شده)، یا همین حالا مرحله شود.

## تأیید

هنوز تأیید نشده. اگر مطابق پیشنهاد تأیید شود، مرحله پس از وجود مستندات دستورالعمل EN/FA به‌صورت `DECLINED` بسته می‌شود.
