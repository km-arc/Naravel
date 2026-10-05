# Cache

`Naravel.Cache` storeهای نام‌دار، scope بر اساس user/tenant، invalidation با نسخهٔ tag و lockهای دارای token مالکیت را فراهم می‌کند. این ماژول روی `Naravel.Foundation` ساخته شده و providerهای Redis و Memcached بسته‌های opt-in هستند.

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

برای store پیش‌فرض و application-scoped، `ICacheStore` را تزریق کنید:

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

عملیات دیگر شامل `ExistsAsync`، `RemoveAsync`، `PullAsync`، `IncrementAsync`، `DecrementAsync` و `FlushAsync` هستند. دامنهٔ `FlushAsync` به backend بستگی دارد؛ محدودیت‌ها را ببینید.

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

## رفتار و محدودیت providerها

| Provider | نگهداری مقدار | رفتار lock | رفتار flush |
|---|---|---|---|
| Memory | مقدار typed درون process | token lock محلی process | کلیدهای trackشدهٔ همان store را حذف می‌کند |
| Redis | JSON با `System.Text.Json` | توزیع‌شده؛ release با token اتمیک است | فقط کلیدهای زیر prefix تنظیم‌شده را scan/delete می‌کند |
| Memcached | مقدار typed با transcoder تنظیم‌شدهٔ Enyim؛ `ttl: null` یعنی بدون انقضا | acquisition توزیع‌شده؛ release کاملاً اتمیک نیست | **کل cluster مشترک Memcached را flush می‌کند**، نه فقط prefix همین store را |

برای Memcached یا وقتی فقط بخشی از کلیدها باید invalidate شوند، به‌جای `FlushAsync` از tag استفاده کنید. increment/decrement در Memcached فقط روی مقدار عددی موجود عمل می‌کنند. کلاینت فعلی Enyim این counterها را فقط به‌صورت sync ارائه می‌دهد؛ provider آن‌ها را روی thread pool اجرا می‌کند و cancellation بعد از شروع فرمان نمی‌تواند آن را متوقف کند.

وضعیت Memory بین چند نمونهٔ برنامه مشترک نیست. برای cache و lock چندسروری از Redis استفاده کنید.

`tests/Naravel.Cache.Tests` قرارداد مشترک store را برای Memory، Redis و Memcached اجرا می‌کند. برای اجرای
تست زندهٔ provider موردنظر، `NARAVEL_TEST_REDIS=host:port` یا `NARAVEL_TEST_MEMCACHED=host:port` را
تنظیم کنید؛ در غیر این صورت xUnit تست را Skip می‌کند. CI هر دو را با service container اجرا می‌کند.
