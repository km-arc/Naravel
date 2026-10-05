# جدول parity با لاراول

وضعیت: **Adopted** (همان‌طور پورت شد) · **Adapted** (همان هدف، شکل idiomatic دات‌نتی) · **Native** (خود .NET حل می‌کند؛ چیزی پورت نشد) · **Rejected** (پورت نشد، با دلیل) · **Open** (تصمیم‌نگرفته؛ قبل از کار PDR لازم است).

| مفهوم لاراول | معادل Naravel / .NET | وضعیت | یادداشت / PDR |
|---|---|---|---|
| `Illuminate\Container` (bind/singleton/resolve) | `Microsoft.Extensions.DependencyInjection` | Native | جایگزین نشد. PDR-002 |
| `Illuminate\Support\Manager::driver()` | `Manager<TDriver,TOptions>.Driver/DriverAsync` | Adapted | async-first، ساخت یک‌باره. PDR-002 |
| `Manager::extend()` | `Manager.Extend` + `IDriverRegistry` | Adopted | در runtime کار می‌کند؛ Keyed DI نمی‌تواند. PDR-002 |
| `forgetDrivers()` / `forgetDriver()` | `ForgetAll` / `Forget` | Adopted | نمونهٔ قدیمی همراه manager dispose می‌شود. PDR-004 |
| فوروارد مجیک `Manager::__call` | ثبت مستقیم درایور پیش‌فرض در DI | Rejected | PDR-003 |
| قرارداد نام‌گذاری `createXxxDriver()` | factoryهای registry | Adapted | بدون reflection روی نام متد |
| `getDefaultDriver()` | `DefaultDriverName` (از options) | Adapted | هر بار خوانده می‌شود |
| `config/*.php` + `env()` | `appsettings*.json`، env vars، `IConfiguration` | Native | providerهای لایه‌ای داخلی |
| `config('x.default')` / آرایه‌های `stores` | `ManagerOptions.Default/Stores` با `IOptionsMonitor` | Adapted | تایپ‌دار؛ live reload. PDR-004 |
| `php artisan config:cache` | — | Rejected | لازم نیست؛ در .NET هزینهٔ boot به‌ازای هر درخواست نداریم |
| Hot reload کانفیگ | `IOptionsMonitor` + invalidate با fingerprint | Adapted | لاراول ندارد. PDR-004 |
| Service providers | متدهای extension DI (`AddNaravelX`) | Adapted | به‌ازای هر ماژول؛ اگر ماژولی به deferred provider نیاز داشت بازبینی شود |
| Facadeها (`Cache::get`) | — | Open | قبل از هر کاری PDR جدا لازم است |
| `illuminate/queue` | `Naravel.Queue` (+ Memory/File/Redis/Database/RabbitMQ/Kafka) | Adapted | PDR-006؛ alias صریح و محافظت deserialization در R00؛ قرارداد مشترک providerها، تست‌های env-gated، انتقال اتمیک Redis و commit مرتب offsetهای Kafka در R01 |
| `illuminate/cache` | `Naravel.Cache` (+ Redis، Memcached) | Adapted | PDR-007؛ managerهای Foundation، providerهای Memory/Redis/Memcached، tag version، scope و lock توکنی؛ قرارداد مشترک و تست زندهٔ env-gated برای Redis/Memcached در R01 |
| `illuminate/filesystem` (`Storage::disk`) | `Naravel.Filesystem` (Local، S3) | Adapted | مهاجرت به Foundation طبق PDR-008 تأییدشده پیاده‌سازی شده؛ محافظت traversal حفظ شده؛ ۲۰ تست متمرکز، در مجموعهٔ ۱۷۲ تستی solution در ۲۰۲۶-۱۰-۰۴ موفق بوده‌اند |
| Mail / Session / Events / Notifications / Broadcasting | ماژول‌های آینده | Open | ترتیب پیشنهادی: Mail ← Session ← Events ← Notifications ← Broadcasting |
| روتینگ (`Route::`، گروه، مسیر نام‌دار، `where`، binding، resource) | `Naravel.Routing` (لایه روی ASP.NET Core) | Adapted | PDR-009؛ مرحلهٔ ۴a راستی‌آزمایی شد (۶۴ از ۶۴ تست موفق، ۲۰۲۶-۱۰-۰۴). binding ضمنی Native است؛ `Route::view`، handler رشته‌ای و `route:cache` رد شدند |
| موتور middleware HTTP (alias، group، پارامتر، اولویت، terminable، `withoutMiddleware`) | `Naravel.Routing` | Adapted | PDR-009؛ middleware سراسری همان `app.Use*` بومی می‌ماند |
| middlewareهای آماده (throttle، signed، maintenance، TrimStrings ...) | `Naravel.Routing` | Open | مرحلهٔ ۴b در همین بسته برنامه‌ریزی شده ولی شروع نشده و این پیاده‌سازی‌ها هنوز موجود نیستند. CORS / TrustProxies / TrustHosts / ValidatePostSize / EncryptCookies Native هستند |
