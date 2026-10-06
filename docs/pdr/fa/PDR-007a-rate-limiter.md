# PDR-007a — محدودسازی نرخ مبتنی بر Cache

**داوری با اولویت .NET بومی:** `System.Threading.RateLimiting` انتخاب درست برای محدودیت‌های درون‌فرایندی است. این API سهمیهٔ مشترک میان چند نمونهٔ برنامه فراهم نمی‌کند؛ Naravel فقط باید همین رفتار مبتنی بر Cache را اضافه کند، نه اینکه تمام API بومی را دوباره بپیچد.

وضعیت: **تأییدشده در ۲۰۲۶-۱۰-۰۶.**

## زمینه

R04 کار Cache موجود در PDR-007 را با RateLimiter کامل می‌کند. ASP.NET Core و .NET محدودکننده‌های محلی مناسبی دارند، اما state حافظه‌ای آن‌ها فقط در همان process است. برنامه‌ای که به سهمیهٔ مشترک میان چند نمونه نیاز دارد، در غیر این صورت باید هماهنگی و انقضای وابسته به provider را خودش بسازد.

سطح Laravel: `Illuminate\Cache\RateLimiter` شامل `attempt`، `tooManyAttempts`، `remaining`، `availableIn` و `clear`. مبنای بومی: `System.Threading.RateLimiting`، middleware محدودسازی نرخ ASP.NET Core و store/lockهای Cache. شکاف مفید، facade کوچک و async برای counter پنجره‌ای مشترک است.

## تصمیم پیشنهادی

یک `IRateLimiter` مبتنی بر Cache به بستهٔ موجود Cache اضافه شود؛ manager، بستهٔ provider یا محدودکنندهٔ محلی دیگری ساخته نشود. policyهای محلی به `System.Threading.RateLimiting` و middleware مربوط به ASP.NET Core واگذار شوند. محدودکنندهٔ مبتنی بر Cache از store انتخاب‌شده و قابلیت lock آن برای سری‌سازی به‌روزرسانی bucket استفاده کند. اگر store انتخاب‌شده تضمین lock لازم را نداشته باشد، ثبت یا استفاده باید با خطای روشن متوقف شود و هرگز ادعای نادرست پشتیبانی چندفرایندی نکند.

نسخهٔ نخست فقط policyهای نام‌دار پنجرهٔ ثابت را پشتیبانی کند. عملیات اتمیک `AttemptAsync` بررسی سقف و ثبت تلاش مجاز را زیر همان lock انجام می‌دهد و تصمیم typed شامل `Allowed`، `Remaining` و `RetryAfter` برمی‌گرداند. فراخواننده کلید subject را صریح می‌دهد؛ هویت از HTTP context استخراج نمی‌شود. `ClearAsync` bucket جاری subject را حذف می‌کند. کلید bucket زیر prefix تنظیم‌شدهٔ Cache namespace می‌شود و نمایش پایدار و غیرقابل‌بازگشت کلید subject به کار می‌رود تا شناسهٔ خام در کلیدهای backend افشا نشود. مانند سایر الگوریتم‌های پنجرهٔ ثابت، یک subject ممکن است در بازهٔ کوتاهی پیرامون مرز دو پنجرهٔ مجاور تا دو برابر سقف اسمی درخواست مجاز داشته باشد؛ این رفتار باید مستند شود و نباید تضمین پنجرهٔ لغزان داده شود.

`ICacheStore.IncrementAsync(key, by, ttl)` اضافه شود. TTL فقط هنگام ایجاد اولیهٔ counter اعمال می‌شود و incrementهای بعدی نباید مرز پنجره را جابه‌جا کنند. Memory ایجاد/increment/انقضا را با lock همان کلید محافظت می‌کند. Redis یک عملیات Lua با `INCRBY`، `PTTL` و `PEXPIRE` فقط در صورت نداشتن expiry اجرا می‌کند. Memcached ابتدا `AddAsync(key, initialValue, ttl)` اتمیک را اجرا می‌کند و اگر کلید از قبل وجود داشت از increment اتمیک آن استفاده می‌کند. خود limiter نیز هنگام بررسی سقف، به‌روزرسانی counter و نگهداری metadata شروع پنجره از `ICacheLock` store انتخاب‌شده استفاده می‌کند.

پیاده‌سازی Redis-atomic اختصاصی limiter به تعویق می‌افتد. پیاده‌سازی نخست باید از قراردادهای موجود و بین‌providerی Cache/lock استفاده کند؛ fast path مختص Redis اضافه نشود. مسیر داغ انتخاب‌شده پیش از merge benchmark شود. این تصمیم قرارداد atomicity سطح provider در `ICacheStore.IncrementAsync` موجود را تغییر نمی‌دهد.

طول عمر bucket با expiration مربوط به Cache برابر طول پنجره است و مرز زمانی واحدی دارد: نخستین درخواست پذیرفته‌شده پنجره را آغاز می‌کند و bucket در پایان همان پنجره منقضی می‌شود. درخواست ردشده زمان انقضا را تمدید نمی‌کند. درستی توزیع‌شده مشروط به تضمین lock در provider انتخاب‌شده و backend مشترک است؛ Memory صریحاً فقط در همان process کار می‌کند. پنجرهٔ لغزان، token bucket، کشف پویای policy و facade سراسری سازگار با Laravel در این مرحله پیشنهاد نمی‌شود.

حالت معمول پیشنهادی (دو خط):

```csharp
services.AddNaravelRateLimiter("redis");
var decision = await limiter.AttemptAsync("login", subjectKey, 5, TimeSpan.FromMinutes(1), cancellationToken);
```

نام دقیق registration و متدها پیشنهادی‌اند؛ مسیر معمول حداکثر سه خط کد کاربر داشته باشد. policyهای نام‌دار مبتنی بر تنظیمات فقط وقتی اضافه شوند که با قراردادهای Foundation سازگار باشند و مسیر ساده را دشوار نکنند.

## قراردادها و کیفیت

- همهٔ عملیات backend ناهمگام و دارای `CancellationToken` باشند؛ sync-over-async و reflection در مسیر داغ ممنوع است.
- از `ICacheStore` و `ICacheLock` موجود استفاده شود. بدون PDR جدا وابستگی تازه یا تغییر مالکیت providerهای Cache اضافه نشود.
- یک fake در namespace `Naravel.Cache.Testing` برای تلاش‌های قطعی، مشاهدهٔ وضعیت و reset تست‌های برنامه فراهم شود.
- `Meter` برای تعداد تلاش‌های مجاز و ردشده و مدت عملیات، و `ActivitySource` برای span منتشر شود. بُعدهای metric محدود به policy و نتیجه باشند؛ کلید subject هرگز tag نشود.
- مستند شود که محدودیت Cache فقط وقتی توزیع‌شده است که Cache و lock پیکربندی‌شده مشترک باشند و atomicity اعلام‌شده را رعایت کنند. برای نیاز صرفاً درون‌فرایندی، استفاده از rate limiting بومی .NET توصیه شود.

## راستی‌آزمایی لازم پس از تأیید

تست‌ها مرز پنجرهٔ ثابت (از جمله burst مستندشدهٔ حداکثر دوبرابری میان پنجره‌های مجاور)، تلاش‌های هم‌زمان در سقف، انقضا، clear، cancellation، محدودبودن Memory به process و رد store فاقد lock مناسب را پوشش دهند. برای هر Cache driver پشتیبانی‌شده contract test و برای مسیر داغ benchmark اضافه شود؛ benchmark پیش از merge بازبینی شود. مستندات Cache انگلیسی و فارسی و هر دو parity table به‌روزرسانی شوند. مجموعهٔ وابستگی‌های Cache بدون تغییر بماند.

## گزینه‌های بررسی‌شده

۱. **فقط `System.Threading.RateLimiting`:** برای محدودیت محلی توصیه می‌شود، اما سهمیهٔ مشترک میان نمونه‌ها را پوشش نمی‌دهد.
۲. **ساخت API محلی دیگری دور limiter بومی .NET:** رد؛ راه‌حل بومی کامل را تکرار می‌کند و ارزش کمی می‌افزاید.
۳. **پیاده‌سازی counter ویژهٔ Redis در بستهٔ core:** رد؛ Cache را به یک provider گره می‌زند و قراردادهای موجود Cache/lock را دور می‌زند.
۴. **ارائهٔ check و increment جداگانه به‌عنوان عملیات اصلی:** رد؛ چند فراخوانندهٔ هم‌زمان ممکن است همگی از check عبور کنند. عملیات معمول باید بررسی و ثبت را زیر یک lock انجام دهد.

## تأیید

مالک این پیشنهاد را در ۲۰۲۶-۱۰-۰۶ تأیید کرد. دامنهٔ پنجرهٔ ثابت مبتنی بر Cache lock، تضمین‌های provider لازم برای استفادهٔ توزیع‌شده و quickstart پیشنهادی برای پیاده‌سازی تأیید شده‌اند. نکات تکمیلی مصوب مالک: در مرز پنجره‌های مجاور burst تا دو برابر سقف اسمی ممکن است؛ مسیر انتخاب‌شده پیش از merge benchmark شود؛ fast path اختصاصی Redis-atomic برای limiter به تعویق افتاده و از قراردادهای موجود Cache/lock استفاده شود.
