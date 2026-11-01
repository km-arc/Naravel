# PDR-024 — store مشترک برای middleware مسیر `throttle`

**حکم native-first:** `System.Threading.RateLimiting` برای یک process انتخاب درست است و پیش‌فرض می‌ماند. این limiter نمی‌تواند سهمیه را بین processها مشترک کند و هیچ جزء native در .NET هم این کار را نمی‌کند. بخش مشترک را Naravel از قبل دارد (`IRateLimiter` مبتنی بر Cache، PDR-007a)؛ آنچه کم است اتصال آن به middleware مسیر است.

وضعیت: **پیشنهادی (پیش‌نویس، 2026-11-01). تأیید نشده و زمان‌بندی نشده.** `NEXT` اکنون R17 است؛ در صورت تأیید، این کار پس از R17 یک stage می‌شود و شناسهٔ آن را مالک تعیین می‌کند.

## زمینه

`throttle` (PDR-009) روی `PartitionedRateLimiter` ساخته شده و برای هر partition یک limiter پنجرهٔ ثابت در `ThrottleLimiterRegistry` و در حافظهٔ process نگه می‌دارد. partition برابر `policy|endpoint|subject` است؛ subject شناسهٔ کاربر احراز‌شده است و برای ناشناس‌ها IP درخواست. PDR-009 store مشترک را به «PDR جداگانه (پس از `Naravel.Cache`)» واگذار کرده بود؛ این همان PDR است.

| وضعیت | امروز چه می‌شود |
|---|---|
| N instance پشت load balancer | هر کدام جدا می‌شمارد: فراخوان تا N برابر سقف در هر پنجره می‌فرستد و از یک سرور 429 و از سرور دیگر 200 می‌گیرد |
| restart، crash، scale-in یا scale-out یک instance | شمارنده‌های آن خالی شروع می‌شوند؛ فراخوان‌ها پنجرهٔ تازه می‌گیرند |
| فراخوان ناشناس پشت proxy بدون forwarded headers | همه IP خود proxy را دارند و یک bucket مشترک می‌شوند (خطای معکوس؛ تنظیم deployment و قابلیت native ASP.NET Core است و در این تصمیم نیست) |

این موضوع در `docs/en|fa/routing.md` («throttle پشت بیش از یک سرور») مستند شده و رفتار فعلی با یک تست ثبت شده است (`Throttle_counts_per_application_instance_and_starts_empty_after_a_restart`). رفتار in-process برای یک instance یا برای کند کردن سوءاستفاده ایرادی ندارد؛ محدودیتِ طراحی است.

چه چیزی آماده است: `IRateLimiter.AttemptAsync(limiter, subjectKey, maxAttempts, window, ct)` مقدارهای `Allowed`، `Remaining` و `RetryAfter` را برمی‌گرداند که برای `X-RateLimit-*` و `Retry-After` کافی است. روی Redis مشترک، یک سهمیه برای همهٔ instanceها می‌شود. مثال login در `cache.md` همین را در handler به کار برده است. هزینه: هر attempt یک lock می‌گیرد، دو کلید می‌خواند، increment می‌کند، در اولین attempt هر پنجره زمان شروع را می‌نویسد و lock را آزاد می‌کند؛ یعنی پنج تا شش فراخوانی store برای هر درخواست. PDR-007a مسیر سریعِ فقط‌-Redis را عمداً عقب انداخت؛ برای middlewareای که روی هر درخواست یک route اجرا می‌شود باید دوباره اندازه‌گیری شود.

## گزینه‌ها

| | گزینه | Routing به چه وابسته می‌شود | کد کاربر |
|---|---|---|---|
| **A (پیشنهادی)** | Routing یک seam کوچک `IThrottleStore` می‌گیرد. پیاده‌سازی پیش‌فرض همین limiter in-process فعلی است، بدون تغییر. یک adapter کوچک روی `IRateLimiter` در بستهٔ جدید (نام موقت `Naravel.Routing.Cache`) می‌آید که به Routing و Cache وابسته است | چیز تازه‌ای | `services.AddNaravelSharedThrottle("redis");` (یک خط کنار `AddNaravelRouting`)؛ policyها و `throttle:login` همان‌طور می‌مانند |
| B | Routing به `Naravel.Cache` ارجاع می‌دهد و گزینهٔ `ThrottleStore` یک store نام‌دار را انتخاب می‌کند | `Naravel.Cache` و از طریق آن Foundation و بستهٔ memory cache | `o.ConfigureThrottleStore("redis")` |
| C | چیزی ساخته نمی‌شود؛ محدودیت‌ها و recipeها مستند می‌مانند (`IRouteMiddleware` خودتان روی `IRateLimiter` یا محدودسازی در proxy/gateway) | چیزی | مثل امروز |

چرا A: Routing فقط به framework reference ASP.NET Core نیاز دارد (PDR-009)، کسی که فقط throttle in-process می‌خواهد Cache را نمی‌کشد، و جهت وابستگی (Routing.Cache ← Routing، Cache) با قانون سخت ۳ می‌خواند. B کد کمتری دارد ولی Cache را به همهٔ کاربران Routing اضافه می‌کند. C هزینه‌ای ندارد و همان وضعیتی است که مستندات می‌گویند؛ هر تیم باید همان middleware ۱۵ خطی را بنویسد، با مدیریت 429 و هدرها.

## تصمیم (در صورت تأیید: گزینهٔ A)

- `IThrottleStore` (عمومی، در Routing): `ValueTask<ThrottleDecision> AttemptAsync(string partitionKey, int permitLimit, TimeSpan window, CancellationToken ct)`. `ThrottleDecision` شامل `Allowed`، `Remaining` و `RetryAfter` است. کلید partition همان رشتهٔ `policy|endpoint|subject` امروز است.
- `ThrottleMiddleware` از store استفاده می‌کند؛ هدرها و پاسخ 429 برای هر دو store بدون تغییر می‌مانند. store درون‌فرایندی `PartitionedRateLimiter` و رفتار فعلی را عیناً نگه می‌دارد؛ تست‌های موجود باید بدون ویرایش بگذرند.
- `Naravel.Routing.Cache` تابع `AddNaravelSharedThrottle(storeName)` را اضافه می‌کند، یعنی یک `IThrottleStore` روی `IRateLimiter`. وابستگی شخص‌ثالث تازه‌ای ندارد.
- معنا: پنجرهٔ ثابت، اولین درخواست پذیرفته‌شده پنجره را شروع می‌کند، burst تا ۲ برابر در مرز پنجره که مستند است باقی می‌ماند. sliding window و token bucket در این PDR نیست.
- **سیاست خطا (تصمیم مالک).** وقتی store خطا می‌دهد (backend از دسترس خارج است، انتظار ۵ ثانیه‌ای lock با `TimeoutException` تمام می‌شود): *fail open* (پذیرش، ثبت warning، شمارش `naravel.routing.throttle.store_failures`) یا *fail closed* (پاسخ 503 با `Retry-After`). پیشنهاد: تنظیم برای هر policy و پیش‌فرض **fail open**، چون throttle از برنامه محافظت می‌کند و قطع Redis نباید همهٔ routeهای throttle‌شده را از کار بیندازد؛ endpointهای credential صراحتاً fail closed را انتخاب می‌کنند. Laravel خطا را پرتاب می‌کند که به 500 تبدیل می‌شود؛ این پیشنهاد نیست.
- Telemetry: مسیر مشترک از meterهای allowed/rejected در limiter مربوط به Cache استفاده می‌کند؛ Routing فقط شمارندهٔ خطای بالا را اضافه می‌کند. dimensionها محدود (policy و outcome) و هرگز subject نیستند.
- Fake: `RateLimiterFake` (در Naravel.Cache.Testing) از قبل جای limiter مشترک را می‌گیرد؛ store درون‌فرایندی fake لازم ندارد.

## پرسش‌های باز برای مالک

1. گزینهٔ A، B یا C.
2. سیاست خطا: پیش‌فرض fail open، fail closed، یا بدون پیش‌فرض (فراخوان باید برای هر policy انتخاب کند).
3. هزینه. اگر benchmark نشان دهد پنج تا شش فراخوانی Redis برای هر درخواست در middleware کند است، یک پیاده‌سازی تک‌فراخوانی روی Redis برای `IThrottleStore` (یک Lua script) تصمیم جداگانه و فقط‌-Redis است که تعویق PDR-007a را دوباره باز می‌کند. گزینهٔ A را با یا بدون این مسیر سریع تأیید کنید.
4. state حالت maintenance هم به همین دلیل process-local است (`docs/en/routing.md`، بخش محدودیت‌ها). اینجا پوشش داده نشده؛ اگر PDR جدا می‌خواهید بگویید.

## اینجا تصمیم گرفته نمی‌شود

الگوریتم‌های sliding window و token bucket، limitهای پویا برای هر کاربر، پرچم maintenance بین instanceها، و هر تغییر در نحوهٔ تشخیص آدرس کلاینت (forwarded headers همچنان native ASP.NET Core است).

## راستی‌آزمایی لازم پس از تأیید

تست‌ها: store درون‌فرایندی تست‌های فعلی throttle را بدون تغییر می‌گذراند؛ یک contract برای store روی store درون‌فرایندی، روی adapter با memory cache و، در job provider که با env فعال می‌شود، روی Redis با دو host برنامه که باید یک سهمیه را مشترک بگیرند (الگوی دو-hostِ تست موجود با انتظار معکوس)؛ پس از restart سهمیه روی Redis حفظ شود؛ fail open و fail closed روی storeای که خطا می‌دهد؛ هدرها و 429 برای هر دو store یکسان؛ cancellation. benchmark مسیر درون‌فرایندی (نباید بدتر شود) و مسیر مشترک روی Redis که پیش از merge بازبینی می‌شود. مستندات EN/FA، `CHANGELOG.md` و فایل‌های public API. build، تست و benchmark فقط با CI راستی‌آزمایی می‌شوند.

## جایگزین‌های بررسی‌شده

1. **جایگزین کردن limiter درون‌فرایندی با limiter مشترک.** رد شد: همهٔ کاربران Routing را مجبور به پرداخت round tripهای Redis و وابستگی سخت می‌کند، برای کاری که یک instance در حافظه بهتر انجام می‌دهد.
2. **قابل‌تعویض کردن خود `ThrottleLimiterRegistry` با `Manager<TDriver,TOptions>`.** رد شد: Routing driver-based نیست (PDR-009)؛ یک interface کوچک کافی است.
3. **`PartitionedRateLimiter` سفارشی روی Redis.** رد شد: leaseها و timerهای replenishment در limiter native حالت محلی را فرض می‌کنند؛ limiter مربوط به Cache از قبل شکل درست و تست دارد.
