# PDR-007 — پورت Cache

وضعیت: **پذیرفته‌شده در ۲۰۲۶-۱۰-۰۴؛ پیاده‌سازی و راستی‌آزمایی‌شده در ۲۰۲۶-۱۰-۰۵.** مالک PDR و طراحی .NET زیر را تأیید کرد. جزئیات پیاده‌سازی و محدودیت providerها در این سند و `docs/fa/cache.md` ثبت شده‌اند.

## زمینه

منبع تأییدشده [km-arc/LaravelCacheNet](https://github.com/km-arc/LaravelCacheNet) است. scope عمومی آن شامل مقدارهای cache، `Remember`، scope کردن کلید به کاربر/tenant، invalidation مبتنی بر tag و lock است. این منبع پیاده‌سازی Queue هم دارد، اما Naravel از قبل `Naravel.Queue` را دارد؛ آن کد نباید کپی شود.

`Naravel.Foundation` manager مشترک، registry برای storeهای نام‌دار، `Extend` زمان اجرا، باطل‌سازی هنگام reload تنظیمات و disposal درایورها را فراهم می‌کند. طبق PDR-005، کلید مجموعهٔ تنظیمات تمام ماژول‌ها `Stores` است.

دو ایراد منبع نباید به پورت منتقل شوند:
- مقدار پیش‌فرض `scanPattern = "*"` در `RedisCacheStore.FlushAsync` می‌تواند کلیدهای خارج از namespace مربوط به Cache را پاک کند.
- map نام‌ها در manager منبع case-sensitive است و با رفتار case-insensitive در Foundation و تنظیمات .NET سازگار نیست.

## تصمیم پذیرفته‌شده

### پکیج‌ها و مالکیت

- `Naravel.Cache` مالک قراردادهای cache و lock، options و managerها، پیاده‌سازی‌های Memory، `TaggedCacheStore` و `ScopedCacheStore` است.
- `Naravel.Cache.Redis` پیاده‌سازی‌های cache و lock مبتنی بر Redis را دارد و به وابستگی `StackExchange.Redis` که از قبل مرکزی مدیریت می‌شود ارجاع می‌دهد.
- `Naravel.Cache.Memcached` پیاده‌سازی‌های Memcached را دارد و به `EnyimMemcachedCore` ارجاع می‌دهد؛ این وابستگی اختیاری می‌ماند تا کاربران Cache یا Redis آن را بی‌دلیل دریافت نکنند.
- هر سه پروژهٔ runtime و پروژهٔ تست Cache به `Naravel.slnx` اضافه شوند؛ نسخهٔ هر بستهٔ جدید فقط در `Directory.Packages.props` ثبت شود.

موارد حفظ‌شده از پورت: `ICacheStore`، `ICacheLock`، `MemoryCacheStore`، `MemoryLock`، `RedisCacheStore`، `RedisLock`، `MemcachedCacheStore`، `MemcachedLock`، tagging و scoping. `IQueueStore`، `QueueManager`، تست‌های Queue و هر رفتار دیگری از Queue حذف شوند.

### Managerها و تنظیمات

- `CacheOptions : ManagerOptions` و `CacheManager : Manager<ICacheStore, CacheOptions>` از `Cache:Default` و `Cache:Stores` استفاده کنند.
- `LockManager : Manager<ICacheLock, CacheOptions>` یک manager جدا است که همان `CacheOptions`، `Default`، `Stores` و نام storeها را مصرف می‌کند. این تصمیم جدایی `CacheManager`/`LockManager` در API منبع را حفظ می‌کند، بدون اینکه بخش تنظیمات جداگانه‌ای برای lock اختراع شود.
- ثبت provider هر نام store را در registry مربوط به Cache و، اگر آن provider lock را پشتیبانی کند، در registry مربوط به Lock قرار دهد. درایور سفارشی cache لازم نیست `ICacheLock` را هم پیاده کند؛ درخواست lock از store بدون lock باید خطای واضح بدهد.
- شکل‌های کاربردی منبع (`Store(name)`، `ForUser(scope)`، `Tags(...)`، `RememberAsync` و lockهای نام‌دار) تا جایی حفظ شوند که با async و cancellation در .NET سازگار باشند. برای مصرف پیش‌فرض، تزریق مستقیم driver و برای انتخاب زمان اجرا، manager استفاده شود؛ مطابق Foundation/PDR-003.
- نام storeها case-insensitive باشند. ثبت store جدید هنگام startup است؛ تغییر مقدار store موجود از طریق Foundation reload می‌شود. کشف driver با reflection یا پیاده‌سازی مجدد manager/cache مجاز نیست.

نمونهٔ تنظیمات:

```json
{
  "Cache": {
    "Default": "memory",
    "Stores": {
      "memory": { "Driver": "memory", "Prefix": "naravel" },
      "redis": { "Driver": "redis", "ConnectionString": "localhost:6379", "Prefix": "naravel" },
      "memcached": { "Driver": "memcached", "Servers": "localhost:11211", "Prefix": "naravel" }
    }
  }
}
```

### ایمنی و رفتار

- scan و flush در Redis همیشه به prefix store تنظیم‌شده محدود شوند. هیچ scan pattern پیش‌فرض `"*"` مجاز نیست. تست باید ثابت کند flush یک store کلید نامرتبط Redis یا prefix مربوط به store دیگر را پاک نمی‌کند.
- از tag versioning استفاده شود، نه enumeration کلیدها؛ تا tagging روی Memory، Redis و Memcached کار کند. توضیح داده شود مقدارهای قدیمی بعد از تغییر نسخهٔ tag دیگر قابل‌دسترسی نیستند، اما ممکن است تا زمان انقضا/eviction در backend بمانند.
- scope کلید cache با شناسهٔ صریح user/tenant که فراخواننده می‌دهد انجام شود؛ هویت از وضعیت ambient وب استخراج نشود.
- cache و lock حافظه‌ای فقط در همان process مشترک‌اند. release lock در Memcached کاملاً atomic نیست؛ مطابق محدودیت منبع، برای critical sectionهای توزیع‌شده Redis پیشنهاد شود.
- مقدارهای Redis با `System.Text.Json` ذخیره شوند؛ Memory مقدار typed را در process نگه می‌دارد؛ Memcached مقدار typed را به transcoder تنظیم‌شدهٔ Enyim می‌سپارد. سازگاری serialization و expiration برای هر provider مستند شود.
- Memcached قابلیت flush بر اساس prefix ندارد؛ `FlushAsync` کل cluster تنظیم‌شده را پاک می‌کند. این دامنهٔ مخرب باید مستند شود و برای invalidation محدود از tag استفاده شود.
- storeهای نام‌دار Memory در صورت نداشتن prefix صریح، نام store را به‌عنوان prefix می‌گیرند تا کلیدهایشان در `IMemoryCache` مشترک روی هم نیفتد.
- کلید نسخهٔ tag تا نخستین flush نسخهٔ `0` دارد. خواندن و نوشتن آن را initialize نمی‌کند؛ این کار race استفادهٔ هم‌زمان اولیه را حذف می‌کند. flush نسخه را به‌شکل atomic افزایش می‌دهد.
- برای سادگی و کارایی connection pool، تمام storeهای Memcached از یک client در Enyim استفاده می‌کنند و باید فهرست server نرمال‌شدهٔ یکسانی داشته باشند. فهرست متفاوت هنگام ثبت رد می‌شود تا clusterها بی‌خبر با هم ادغام نشوند. تغییر topology سرورها به راه‌اندازی مجدد host نیاز دارد؛ مقادیر معمول تنظیمات store همچنان با Foundation reload می‌شوند.
- TTL تهی در Memcached به‌صورت expiration صفر (مقدار پروتکل برای بدون انقضا) ارسال می‌شود، نه اینکه بی‌سروصدا به ۳۰ روز محدود شود.
- کلاینت فعلی Enyim، increment/decrement اتمیک را فقط به‌صورت متد sync ارائه می‌دهد. provider می‌تواند برای حفظ قرارداد async store این فراخوانی‌ها را روی thread pool اجرا کند؛ مستند شود که cancellation جلوی شروع کار صف‌شده را می‌گیرد ولی عملیات counter که آغاز شده قابل‌قطع نیست.
- APIهای async مربوط به I/O، `CancellationToken` بپذیرند؛ sync-over-async اضافه نشود.

## پیاده‌سازی و راستی‌آزمایی

مجموعهٔ تست، `CacheManager`، `MemoryCacheStore`، `MemoryLock`، `ScopedCacheStore` و `TaggedCacheStore` را بدون تست‌های Queue پوشش می‌دهد. تست‌های یکپارچگی Foundation دو نام با نوع driver یکسان، `Extend` زمان اجرا، reload store موجود، ثابت‌ماندن driver هنگام reload نامرتبط، انتخاب پیش‌فرض و disposal را می‌سنجند. تست providerها ثبت تنظیمات و flush محدود به prefix در Redis و نیز تنظیمات، کلید و TTL در Memcached را پوشش می‌دهند. پروژهٔ Cache دارای ۲۷ مورد تست است. اتصال زنده به Redis/Memcached اجرا نشده است.

راهنماهای Cache به هر دو زبان، parity tableها، `AGENTS.md`، `PROGRESS.md`، READMEها و `CHANGELOG.md` APIهای پیاده‌شده و محدودیت providerها را توضیح می‌دهند. build و تست کل solution در ۲۰۲۶-۱۰-۰۵ موفق شد؛ اتصال زنده به Redis/Memcached همچنان راستی‌آزمایی نشده است.

## گزینه‌های بررسی‌شده

۱. **کپی پیاده‌سازی Queue منبع:** رد؛ `Naravel.Queue` را تکرار می‌کند و دو API رقیب برای job/queue می‌سازد.
۲. **یک manager برای Cache و lock:** رد؛ منبع تأییدشده دو قرارداد و manager جدا دارد؛ دو manager Foundation همین تفکیک را با options و نام store مشترک حفظ می‌کنند.
۳. **قرار دادن وابستگی Redis/Memcached در بستهٔ core:** رد؛ کاربر فقط provider موردنیازش را نصب کند.
۴. **flush کردن Redis با wildcard نامحدود:** رد؛ ممکن است دادهٔ نامرتبط برنامه حذف شود؛ تمام scanها باید prefix داشته باشند.
۵. **حل نام store به شکل case-sensitive:** رد؛ با قرارداد Foundation و تنظیمات .NET ناسازگار است.

## ثبت تأیید

مالک PDR را در ۲۰۲۶-۱۰-۰۴ تأیید کرد؛ شامل `CacheManager` و `LockManager` جدا با `Default`/`Stores` مشترک، provider مربوط به Memcached، رفتار serialization و الزام flush امن Redis با prefix.
