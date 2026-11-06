# Cache

`Naravel.Cache` storeهای نام‌دار، scope بر اساس user/tenant، invalidation با نسخهٔ tag و lockهای دارای token مالکیت را فراهم می‌کند. این ماژول روی `Naravel.Foundation` ساخته شده و providerهای Redis و Memcached بسته‌های opt-in هستند.

## شروع سریع

بسته را اضافه کنید، ثبتش کنید و `ICacheStore` را تزریق کنید. بدون هیچ تنظیمی یک store درون‌حافظه‌ای دارید که برای امتحان هر چیزِ این صفحه کافی است.

```sh
dotnet add package Naravel.Cache
```

```csharp
using Naravel.Cache;
using Naravel.Cache.Abstractions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelCache(builder.Configuration);

var app = builder.Build();

app.MapGet("/time", (ICacheStore cache) =>
    cache.RememberAsync("server-time", TimeSpan.FromSeconds(30),
        () => Task.FromResult(DateTime.UtcNow.ToString("T"))));

app.Run();
```

`/time` را چند بار بارگذاری کنید: مقدار ۳۰ ثانیه ثابت می‌ماند و بعد factory دوباره اجرا می‌شود.

برای اشتراک یک cache میان چند نمونه از برنامه، provider مربوط به Redis را اضافه کنید و آن را store پیش‌فرض کنید (`"Default": "redis"` در [تنظیمات](#تنظیمات)):

```csharp
builder.Services.AddNaravelCache(builder.Configuration).AddNaravelRedisCache(builder.Configuration);
builder.Services.AddNaravelRateLimiter("redis");   // اختیاری: محدودکنندهٔ ورود که پایین‌تر می‌آید، با شمارش در همان store
```

## ثبت providerها

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddNaravelCache(builder.Configuration)
    .AddNaravelRedisCache(builder.Configuration)
    .AddNaravelMemcachedCache(builder.Configuration);

var app = builder.Build();
app.Run();
```

بستهٔ core را پیش از providerهای backend ثبت کنید و فقط providerهایی را اضافه کنید که برنامه واقعاً استفاده می‌کند. `AddNaravelCache` به‌صورت پیش‌فرض بخش `Cache` را می‌خواند؛ برای نام متفاوت، نام بخش را به‌عنوان آرگومان دوم بدهید.

## تنظیمات

```json
{
  "Cache": {
    "Default": "memory",
    "AppPrefix": "orders-api",
    "Stores": {
      "memory": {
        "Driver": "memory",
        "Prefix": "orders-api:memory"
      },
      "redis": {
        "Driver": "redis",
        "ConnectionString": "localhost:6379",
        "Database": "0",
        "Prefix": "orders-api:redis"
      },
      "memcached": {
        "Driver": "memcached",
        "Prefix": "orders-api:memcached",
        "Servers": {
          "primary": { "Address": "localhost", "Port": "11211" }
        }
      }
    }
  }
}
```

برای اطلاعات محرمانه از environment variable یا secret provider استفاده کنید. نام storeها به بزرگی و کوچکی حروف حساس نیست. مقدار پیش‌فرض `Default` برابر `memory` و `AppPrefix` برابر `app` است. provider نام storeهای تنظیم‌شده را هنگام startup ثبت می‌کند. تغییر مقدارهای یک store موجود با Foundation reload می‌شود؛ افزودن store جدید پس از startup به ثبت مجدد یا راه‌اندازی مجدد host نیاز دارد.

`Prefix` پیش از ارسال کلید خام به backend اعمال می‌شود. flush در Redis به همان prefix محدود است. Memcached حذف بر اساس prefix ندارد؛ محدودیت‌ها پایین‌تر آمده‌اند.

اگر `Prefix` را ننویسید، نام هر store حافظه‌ای prefix پیش‌فرض آن می‌شود؛ بنابراین storeهای نام‌دار با وجود اشتراک cache فرایند از هم جدا می‌مانند. برای کارایی connection pool، تمام storeهای Memcached از یک client مشترک استفاده می‌کنند و باید فهرست server یکسانی داشته باشند. تغییر فهرست server به راه‌اندازی مجدد host نیاز دارد؛ سایر تنظیمات store طبق رفتار reload در Foundation اعمال می‌شوند.

## عملیات پایه

برای store پیش‌فرض و application-scoped، `ICacheStore` را تزریق کنید. این سرویس یک نمای `ScopedCacheStore` روی درایور پیش‌فرضِ تحت مالکیت `CacheManager` است، نه خود نمونهٔ درایور؛ بنابراین `CacheManager` درایور زیرین را آزاد می‌کند. تزریق مستقیم `ICacheLock` خود درایور قفل پیش‌فرض را از ثبت مالکیت‌محور Foundation دریافت می‌کند؛ در این حالت DI آن نمونه را آزاد می‌کند و `LockManager` از آزادسازی دوباره صرف‌نظر می‌کند:

```csharp
using Naravel.Cache.Abstractions;

public sealed class SettingsService(ICacheStore cache)
{
    public Task SetAsync(string key, string value, CancellationToken cancellationToken) =>
        cache.SetAsync(key, value, TimeSpan.FromMinutes(10), cancellationToken);

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var (found, value) = await cache.TryGetAsync<string>(key, cancellationToken);
        return found ? value : null;
    }
}
```

`RememberAsync` هنگام miss مقدار را می‌سازد و ذخیره می‌کند:

```csharp
var settings = await cache.RememberAsync(
    "site-settings",
    TimeSpan.FromMinutes(10),
    ct => settingsRepository.LoadAsync(ct),
    cancellationToken);
```

همهٔ عملیات و خروجی هر کدام:

| متد | خروجی | کاری که می‌کند |
|---|---|---|
| `TryGetAsync<T>(key, ct)` | `(bool Found, T? Value)` | مقدار را می‌خواند. `Found` کلید ناموجود را از `default` ذخیره‌شده جدا می‌کند. |
| `SetAsync<T>(key, value, ttl, ct)` | `Task` | مقدار را ذخیره می‌کند. `ttl: null` یعنی بدون انقضا. |
| `ExistsAsync(key, ct)` | `bool` | آیا مقدار زنده‌ای ذخیره شده است. |
| `RemoveAsync(key, ct)` | `bool` | کلید را حذف می‌کند؛ اگر وجود داشت `true`. |
| `PullAsync<T>(key, ct)` | `T?` | با یک فراخوانی می‌خواند و حذف می‌کند؛ اگر کلید نبود `default`. |
| `RememberAsync<T>(key, ttl, factory, ct)` | `T` | مقدار ذخیره‌شده را برمی‌گرداند، وگرنه `factory` را اجرا می‌کند، نتیجه را ذخیره می‌کند و برمی‌گرداند. |
| `RememberForeverAsync<T>(key, factory, ct)` | `T` | همان، بدون انقضا. |
| `IncrementAsync(key, by, ct)` و `IncrementAsync(key, by, ttl, ct)` | `long` | `by` را به یک شمارنده اضافه می‌کند و مقدار جدید را برمی‌گرداند. overload دارای `ttl` فقط وقتی کلید را می‌سازد انقضا می‌گذارد. |
| `DecrementAsync(key, by, ct)` | `long` | `by` را کم می‌کند و مقدار جدید را برمی‌گرداند. |
| `FlushAsync(ct)` | `Task` | store را خالی می‌کند. دامنه به provider بستگی دارد؛ [محدودیت‌ها](#رفتار-و-محدودیت-providerها) را ببینید. |

`PullAsync`، `RememberAsync` و `RememberForeverAsync` متدهای extension در namespace `Naravel.Cache` هستند؛ پس کنار `using Naravel.Cache.Abstractions;` بنویسید `using Naravel.Cache;`. `RememberAsync` factory را با یا بدون `CancellationToken` می‌پذیرد: `() => ...` یا `ct => ...`.

```csharp
await cache.SetAsync("greeting", "hello", TimeSpan.FromMinutes(10), ct);

var (found, value) = await cache.TryGetAsync<string>("greeting", ct);   // found == true و value == "hello"
var removed = await cache.RemoveAsync("greeting", ct);                  // true: وجود داشت
var removedAgain = await cache.RemoveAsync("greeting", ct);             // false: دیگر نیست

var visits = await cache.IncrementAsync("visits:home", 1, TimeSpan.FromHours(1), ct);   // 1، بعد 2، بعد 3 ...
var stock = await cache.IncrementAsync("stock:42", 10, ct);                              // 10
stock = await cache.DecrementAsync("stock:42", 3, ct);                                   // 7

var rates = await cache.RememberForeverAsync("exchange-rates", () => LoadRatesAsync(ct), ct);   // یک بار بارگذاری می‌شود؛ LoadRatesAsync متد خودتان است

await cache.SetAsync("flash:message", "saved", TimeSpan.FromMinutes(1), ct);
var message = await cache.PullAsync<string>("flash:message", ct);       // "saved"؛ کلید حالا حذف شده است
```

پیش از تکیه بر این متدها سه نکته را بدانید:

- **`RememberAsync` قفل نمی‌گیرد.** اگر دو درخواست هم‌زمان روی یک کلید miss کنند، هر دو factory را اجرا می‌کنند و نوشتن آخر برنده است. برای factoryِ سنگین یا دارای اثر جانبی، فراخوانی را داخل یک [lock](#lock) بگذارید.
- **شمارنده‌ای که باید منقضی شود، overload دارای `ttl` را می‌خواهد.** در store حافظه‌ای، `IncrementAsync(key, by, ct)` بدون ttl شمارنده را بدون انقضا نگه می‌دارد.
- **TTL مثبت بدهید، یا `null` برای بدون انقضا.** `CacheFake` برای TTL صفر یا منفی `ArgumentOutOfRangeException` پرتاب می‌کند، تا تست اشتباه را زود بگیرد.


## انتخاب store

برای انتخاب store در زمان اجرا، `CacheManager` را تزریق کنید:

```csharp
using Naravel.Cache;

public sealed class ReportCache(CacheManager cache)
{
    public ICacheStore RedisStore() => cache.Store("redis");
}
```

manager رفتار Foundation را به‌ارث می‌برد: `Extend` زمان اجرا، reload تنظیمات، cache درایورها، نام‌های case-insensitive و آزادسازی منابع. factoryها باید نمونه‌هایی برگردانند که manager مالکیت و اجازهٔ dispose آن‌ها را دارد.

می‌توان پس از startup یک cache store سفارشی ثبت کرد:

```csharp
cacheManager.Extend("custom", _ => new MyCacheStore("custom"));
var custom = cacheManager.Store("custom");
```

اگر store سفارشی lock هم پشتیبانی می‌کند، باید جداگانه با `LockManager.Extend` ثبت شود؛ قرارداد cache و lock عمداً جدا هستند.

storeای که با factory ناهمگام (async) ثبت شده است (مثلاً یک store سفارشی که پیش از اولین استفاده باید وصل شود) را `Store()` نمی‌تواند بسازد، چون همگام (sync) است. آن را یک بار با `DriverAsync` بسازید، مثلاً هنگام startup؛ از آن به بعد `Store` آن را برمی‌گرداند:

```csharp
cacheManager.Extend("remote", async (sp, ct) =>
{
    var store = new MyRemoteCacheStore("remote");
    await store.ConnectAsync(ct);
    return store;
});

await cacheManager.DriverAsync("remote", cancellationToken);   // store را می‌سازد و cache می‌کند
var remote = cacheManager.Store("remote");                     // حالا کار می‌کند؛ پیش از DriverAsync پرتاب می‌شود
```

اگر `Store("remote")` را پیش از `DriverAsync` صدا بزنید، `InvalidOperationException` پرتاب می‌شود: `Driver 'remote' is registered with an asynchronous factory and has not been created yet. Use DriverAsync to create it.` متد `DriverAsync` store خام را بدون prefix برنامه برمی‌گرداند؛ برای نمایی که کد شما باید با آن بخواند و بنویسد از `Store`، `ForUser` یا `Scope` استفاده کنید. storeهای داخلی Memory، Redis و Memcached همگام هستند و به هیچ‌کدام از این‌ها نیاز ندارند.

## Scope و tag

کلیدها را با شناسهٔ صریح user یا tenant scope کنید؛ identity از وضعیت ambient وب خوانده نمی‌شود:

```csharp
var userCache = cacheManager.ForUser(userId);
await userCache.SetAsync("cart-count", 3, TimeSpan.FromMinutes(5), cancellationToken);

var tenantCache = cacheManager.Scope($"tenant:{tenantId}");
await tenantCache.Tags("orders", "recent").SetAsync("last-order", order, ttl, cancellationToken);
await tenantCache.Tags("orders").FlushAsync(cancellationToken);
```

Tagging از counterهای نسخه در cache اصلی استفاده می‌کند. flush یک tag نسخه‌اش را افزایش می‌دهد؛ در نتیجه مقدارهای قبلی بدون scan کلیدها غیرقابل‌دسترسی می‌شوند. مقدارهای قدیمی ممکن است تا زمان انقضای TTL یا eviction در backend باقی بمانند. `CacheManager.FlushTagAsync(tag, storeName)` tag را در scope برنامه flush می‌کند.

## Lock

برای انتخاب مستقل provider مربوط به lock، `LockManager` را تزریق کنید:

```csharp
using Naravel.Cache;

public sealed class OrderProcessor(LockManager locks)
{
    public Task<bool> ProcessAsync(string orderId, CancellationToken cancellationToken)
    {
        var cacheLock = locks.Driver("redis");
        return cacheLock.BlockAsync(
            $"process-order:{orderId}",
            ttl: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            callback: async ct => await ProcessOrderAsync(orderId, ct),
            cancellationToken);
    }
}
```

هر acquisition موفق token یکتایی برمی‌گرداند و فقط همان token می‌تواند lock را آزاد کند. Redis از `SET NX` و compare-and-delete اتمیک استفاده می‌کند. lock حافظه‌ای فقط در همان process معتبر است. acquisition در Memcached اتمیک است، اما release فاصلهٔ زمانی کوچکی بین خواندن و حذف دارد؛ برای critical section توزیع‌شده از Redis استفاده کنید.

رفتار `BlockAsync` را بدانید تا دو مدت را درست انتخاب کنید:

- تلاش می‌کند lock را بگیرد و تا وقتی دست کس دیگری است، تقریباً هر ۱۵۰ میلی‌ثانیه دوباره تلاش می‌کند تا `wait` تمام شود. سپس `false` برمی‌گرداند و callback اجرا نمی‌شود. `TryRunAsync(name, ttl, callback)` همین است بدون انتظار: یک تلاش، و اگر lock دست دیگری بود `false`.
- وقتی `true` برمی‌گرداند، callback اجرا شده است. lock پس از آن آزاد می‌شود، حتی اگر callback exception پرتاب کند؛ آنگاه exception به کد شما می‌رسد.
- `ttl` عمر lock است و تمدید نمی‌شود. اگر callback بیشتر از `ttl` طول بکشد، lock منقضی می‌شود و دیگری می‌تواند آن را بگیرد. `ttl` را بزرگ‌تر از کندترین اجرای قابل‌انتظار بگذارید.
- `ReleaseAsync(name, token)` وقتی token مال مالک فعلی نباشد `false` برمی‌گرداند، مثلاً پس از انقضای lock و گرفتن آن توسط دیگری. exception پرتاب نمی‌کند.


## محدودسازی نرخ

limiter پنجرهٔ ثابت را به یک store نام‌دار وصل کنید:

```csharp
builder.Services.AddNaravelRateLimiter("redis");
var decision = await limiter.AttemptAsync("login", subjectKey, 5, TimeSpan.FromMinutes(1), cancellationToken);
```

`IRateLimiter` را در محل استفاده تزریق کنید. هر policy و subject bucket جدا دارد و کلید subject پیش از
قرارگرفتن در کلید cache hash می‌شود. نخستین تلاش مجاز پنجرهٔ ثابت را آغاز می‌کند؛ تلاش ردشده counter را
افزایش نمی‌دهد و پنجره را تمدید نمی‌کند. `RateLimitDecision` شامل `Allowed`، `Remaining` و `RetryAfter`
است و `ClearAsync` bucket یک policy/subject را پاک می‌کند. برای محدودیت‌های درون‌فرایندی از
`System.Threading.RateLimiting` استفاده کنید؛ محدودسازی مبتنی بر Cache فقط وقتی مناسب است که همهٔ
instanceها backend مشترک داشته باشند و lock آن هماهنگی لازم را فراهم کند. lock حافظه‌ای فقط در همان
process کار می‌کند؛ release در Memcached race خواندن/حذف دارد، پس برای سهمیهٔ مشترک حساس Redis مناسب‌تر است.

مثال کامل: محدودکردن endpoint ورود به ۵ تلاش در دقیقه برای هر IP و حساب. `IAuthService` و `LoginRequest` نمایندهٔ کد خودتان هستند.

```csharp
using Naravel.Cache.RateLimiting;

// Program.cs: a shared Redis store, so every instance counts the same attempts.
builder.Services.AddNaravelCache(builder.Configuration).AddNaravelRedisCache(builder.Configuration);
builder.Services.AddNaravelRateLimiter("redis");

app.MapPost("/login", async (LoginRequest body, HttpContext http, IRateLimiter limiter, IAuthService auth, CancellationToken ct) =>
{
    var subject = $"{http.Connection.RemoteIpAddress}:{body.Email.ToLowerInvariant()}";   // one bucket per IP and account

    var decision = await limiter.AttemptAsync("login", subject, 5, TimeSpan.FromMinutes(1), ct);
    if (!decision.Allowed)
    {
        http.Response.Headers.RetryAfter = ((int)Math.Ceiling(decision.RetryAfter.TotalSeconds)).ToString();
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    if (!await auth.CheckAsync(body.Email, body.Password, ct))
        return Results.Unauthorized();                       // the failed attempt stays counted

    await limiter.ClearAsync("login", subject, ct);          // RateLimiter::clear after a successful sign-in
    return Results.Ok();
});
```

`Remaining` تعداد تلاش‌های باقی‌ماندهٔ پنجره را می‌دهد؛ مثلاً برای ارسال هدر `X-RateLimit-Remaining`.

`ICacheStore.IncrementAsync(key, by, ttl)` counter را اتمیک افزایش می‌دهد و TTL را فقط هنگام ایجاد کلید
می‌گذارد. Redis افزایش و ثبت expiry اولیه را در یک عملیات Lua انجام می‌دهد؛ Memory از lock همان کلید و
Memcached از add اتمیک و سپس increment اتمیک (با همان TTL، تا کلیدی که میان این دو فراخوانی منقضی می‌شود دوباره با انقضا ساخته شود و شمارندهٔ دائمی نشود) استفاده می‌کند. **محلی در برابر توزیع‌شده:** Redis یک سقف واحد برای همهٔ replicaها می‌دهد؛ Memcached هم مشترک است ولی lock آن race دارد و حذف کلید توسط eviction پنجره را ریست می‌کند؛ Memory و middleware مسیر `throttle` با store پیش‌فرضش per-process هستند، پس با N replica سقف مؤثر N برابر می‌شود؛ `Naravel.Routing.Cache` throttle را در storeای که نام ببرید می‌شمارد (با شمارندهٔ یک‌فراخوانیِ خودش، بدون lock و بدون `IRateLimiter`). اگر lock ظرف ۵ ثانیه گرفته نشود `TimeoutException` پرتاب می‌شود (نه اجازه نه رد؛ fail-open یا fail-closed را فراخواننده تعیین کند)؛ قطع بودن backend استثنای همان backend را نشان می‌دهد، و اگر کار خودتان پس از یک تلاشِ مجاز شکست بخورد، counter و lock برگردانده نمی‌شوند. incrementهای بعدی مرز پنجرهٔ ثابت را جابه‌جا نمی‌کنند.

پنجرهٔ ثابت ممکن است در بازه‌ای کوتاه پیرامون مرز دو پنجرهٔ مجاور تا **۲ برابر سقف تنظیم‌شده** درخواست
بپذیرد؛ مثلاً یک سقف کامل درست پیش از انقضا و سقف کامل بعدی پس از reset. این الگوریتم تضمین پنجرهٔ
لغزان نمی‌دهد؛ برای محدودیت محلی با آن رفتار، از پنجرهٔ لغزان `System.Threading.RateLimiting` استفاده کنید.

limiter بررسی و به‌روزرسانی را با Cache lock انتخاب‌شده سری می‌کند. افزایش counter و ثبت expiry اولیه در
سطح store در Redis اتمیک است، اما fast path اختصاصی Redis-atomic برای limiter به تعویق افتاده است؛ خود
limiter از همان قرارداد Cache/lock مشترک با providerهای دیگر استفاده می‌کند.

limiter از meter با نام `Naravel.Cache` counterهای `naravel.cache.ratelimiter.allowed` و
`naravel.cache.ratelimiter.rejected`، histogram مدت عملیات و `ActivitySource` span منتشر می‌کند.
tagهای metric فقط نام limiter و outcome هستند و subject key را شامل نمی‌شوند.

benchmark نرخ عملیات روی Memory با نام `MemoryRateLimiterAttempt` در
`benchmarks/Naravel.Benchmarks/QueueAndCacheBenchmarks.cs` قرار دارد. برای اجرای آن در Release:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*MemoryRateLimiterAttempt*' --job short
```

## خطاها

فراخوانی نادرست یک متد، زود و با exception استاندارد .NET شکست می‌خورد. پیام‌های زیر همان‌هایی هستند که کد پرتاب می‌کند.

| چه وقت | exception و پیام |
|---|---|
| `key`، نام lock یا scope خالی | `ArgumentException` (برای `null` همان `ArgumentNullException`) |
| `ForUser(null)` | `ArgumentNullException` |
| `Tags()` بدون هیچ tag غیرخالی | `ArgumentException`: `At least one non-empty cache tag is required.` |
| `IncrementAsync(key, by, ttl)` با `by` یا `ttl` صفر یا منفی | `ArgumentOutOfRangeException` |
| `BlockAsync` با `ttl` صفر یا منفی یا `wait` منفی (`AcquireAsync` در lock حافظه‌ای هم `ttl` صفر یا منفی را رد می‌کند) | `ArgumentOutOfRangeException` |
| `AttemptAsync` با `maxAttempts` یا `window` صفر یا منفی | `ArgumentOutOfRangeException` |
| `AttemptAsync` در ۵ ثانیه نتواند lock را بگیرد | `TimeoutException`: `Could not acquire the cache rate-limit lock within the configured wait period.` |
| نام storeای که ثبت نشده | `DriverNotRegisteredException`: `Driver 'x' is not registered for ICacheStore. Available drivers: …` |
| `Store()` روی store فقط-async که هنوز ساخته نشده | `InvalidOperationException`؛ اول `DriverAsync` را صدا بزنید ([انتخاب store](#انتخاب-store)). |
| لغو شدن token | `OperationCanceledException` |
| backend از دسترس خارج است | exception خودِ client آن backend، بدون تغییر (در Redis، exception مربوط به StackExchange.Redis). |

خطاهای خود Foundation (نبودن store پیش‌فرض، manager دورانداخته‌شده) در [راهنمای Foundation](foundation.md#خطاها) آمده است.

assertionهای `CacheFake` با این پیام‌ها `InvalidOperationException` پرتاب می‌کنند:

| assertion | پیام |
|---|---|
| `AssertHas("k")` روی کلید ناموجود یا منقضی | `Expected cache key 'k' to exist, but it does not.` |
| `AssertHas<int>("k")` وقتی مقدار string است | `Cache key 'k' holds String, not Int32.` |
| `AssertMissing("k")` روی کلید ذخیره‌شده | `Expected cache key 'k' to be missing, but it exists.` |
| `AssertEmpty()` در حالی که دو کلید مانده | `Expected an empty cache, but found 2 key(s): a, b.` |

## رفتار و محدودیت providerها

| Provider | نگهداری مقدار | رفتار lock | رفتار flush |
|---|---|---|---|
| Memory | مقدار typed درون process | token lock محلی process | کلیدهای trackشدهٔ همان store را حذف می‌کند |
| Redis | JSON با `System.Text.Json` | توزیع‌شده؛ release با token اتمیک است | فقط کلیدهای زیر prefix تنظیم‌شده را scan/delete می‌کند |
| Memcached | مقدار typed با transcoder تنظیم‌شدهٔ Enyim؛ `ttl: null` یعنی بدون انقضا | acquisition توزیع‌شده؛ release کاملاً اتمیک نیست | **کل cluster مشترک Memcached را flush می‌کند**، نه فقط prefix همین store را |

برای Memcached یا وقتی فقط بخشی از کلیدها باید invalidate شوند، به‌جای `FlushAsync` از tag استفاده کنید. increment/decrement معمول Memcached فقط روی مقدار عددی موجود عمل می‌کنند؛ overload دارای TTL برای ساخت اولیه از add اتمیک استفاده می‌کند. کلاینت فعلی Enyim counterهای موجود را فقط به‌صورت sync ارائه می‌دهد؛ provider آن‌ها را روی thread pool اجرا می‌کند و cancellation بعد از شروع فرمان نمی‌تواند آن را متوقف کند.

وضعیت Memory بین چند نمونهٔ برنامه مشترک نیست. برای cache و lock چندسروری از Redis استفاده کنید.

`tests/Naravel.Cache.Tests` قرارداد مشترک store را برای Memory، Redis و Memcached اجرا می‌کند و increment اتمیک با TTL اولیه را می‌سنجد. تست‌های limiter مرز پنجره، clear، جدایی storeهای نام‌دار، cancellation، Fake و خروجی Meter/Activity را پوشش می‌دهند. برای اجرای
تست زندهٔ provider موردنظر، `NARAVEL_TEST_REDIS=host:port` یا `NARAVEL_TEST_MEMCACHED=host:port` را
تنظیم کنید؛ در غیر این صورت xUnit تست را Skip می‌کند. CI هر دو را با service container اجرا می‌کند.

## Fake برای تست

`Naravel.Cache.Testing.CacheFake` یک `ICacheStore` درون‌حافظه برای تست برنامه است. آن را جایی تزریق کنید که `ICacheStore` انتظار می‌رود و روی چیزهایی که cache شده assert بزنید؛ بدون Redis، Memcached و `IMemoryCache`.

```csharp
var clock = new ManualClock();                 // زیرکلاسی از TimeProvider
var cache = new CacheFake(timeProvider: clock);
await sut.RememberProfileAsync(42);

cache.AssertHas<Profile>("profile:42");
clock.Advance(TimeSpan.FromMinutes(11));       // انقضا از ساعت پیروی می‌کند، پس sleep لازم نیست
cache.AssertMissing("profile:42");
```

رفتارش مثل store حافظه است (مقدار typed، و TTL فقط در اولین نوشتن برای `IncrementAsync(key, by, ttl)`)، tag و lock ندارد و همان قرارداد مشترک store را پاس می‌کند. API بررسی آن `Keys`، `AssertHas`، `AssertMissing`، `AssertEmpty` و `Reset` است. نیمهٔ cache از AUDIT D-23 را می‌بندد.

نمونه‌های این راهنما (عملیات پایه، `RememberAsync`، scope و tag، قفل و محدودکننده ورود) روی store حافظه توسط `tests/Naravel.Cache.Tests/DocsExampleTests.cs` اجرا می‌شوند. تغییر یکی یعنی تغییر دیگری. نسخه‌های Redis و Memcached را تست‌های provider پوشش می‌دهند.
