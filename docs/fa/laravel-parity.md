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
| `illuminate/queue` | `Naravel.Queue` (+ Memory/File/Redis/Database/RabbitMQ/Kafka) | Adapted | PDR-006؛ alias صریح و محافظت deserialization در R00؛ قرارداد مشترک providerها، تست‌های env-gated، انتقال اتمیک Redis و commit مرتب offsetهای Kafka در R01؛ retry job ناموفق، batch پایدار، timeout/کنترل worker، API async در RabbitMQ.Client 7، metric و Fake در R07 |
| `illuminate/cache` | `Naravel.Cache` (+ Redis، Memcached) | Adapted | PDR-007؛ managerهای Foundation، providerهای Memory/Redis/Memcached، tag version، scope و lock توکنی؛ قرارداد مشترک و تست زندهٔ env-gated برای Redis/Memcached در R01؛ RateLimiter پنجرهٔ ثابت مبتنی بر Cache در R04/PDR-007a |
| `illuminate/filesystem` (`Storage::disk`) | `Naravel.Filesystem` (Local، S3) | Adapted | مهاجرت به Foundation طبق PDR-008 تأییدشده پیاده‌سازی شده؛ محافظت traversal حفظ شده؛ شواهد راستی‌آزمایی در ردیف R02 جدول [ROADMAP.md](../../ROADMAP.md) است |
| Events (`Illuminate\Contracts\Events\Dispatcher`) | `Naravel.Events` + adapter اختیاری `Naravel.Events.Queue` | Adapted | PDR-011؛ listenerهای DI تایپ‌شده، subscription در scope، توقف انتشار، adapter صف صریح، Fake و telemetry |
| Mail / Session / Notifications / Broadcasting | ماژول‌های آینده | Open | PDRهای R09/R10/R18 تأیید شده‌اند؛ پیاده‌سازی طبق مراحل زمان‌بندی شده است |
| روتینگ (`Route::`، گروه، مسیر نام‌دار، `where`، binding، resource) | `Naravel.Routing` (لایه روی ASP.NET Core) | Adapted | PDR-009؛ هستهٔ روتینگ و middlewareهای آماده تا R05 تحویل شده‌اند (وضعیت و راستی‌آزمایی در [ROADMAP.md](../../ROADMAP.md)). binding ضمنی Native است؛ `Route::view`، handler رشته‌ای و `route:cache` رد شدند |
| موتور middleware HTTP (alias، group، پارامتر، اولویت، terminable، `withoutMiddleware`) | `Naravel.Routing` | Adapted | PDR-009؛ middleware سراسری همان `app.Use*` بومی می‌ماند |
| middlewareهای آماده (throttle، signed، maintenance، TrimStrings ...) | `Naravel.Routing` | Adapted | PDR-009؛ در R05 داخل `Naravel.Routing` با aliasهای `throttle`، `signed`، `maintenance`، `cache.headers`، `trim`، `convert.empty` و `guest` پیاده شده است (محدودیت‌های سطح فرایند در [routing.md](routing.md) آمده). CORS / TrustProxies / TrustHosts / ValidatePostSize / EncryptCookies Native هستند |
