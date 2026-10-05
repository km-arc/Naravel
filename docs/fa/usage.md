# نحوهٔ استفاده (سمت اپلیکیشن)

Foundation معمولاً از طریق یک ماژول مصرف می‌شود. `CacheManager` در زیر یک ماژول نمونه است که روی Foundation ساخته شده.

## ۱. کانفیگ
```json
{
  "Cache": {
    "Default": "redis",
    "Stores": {
      "redis": { "Host": "localhost", "Port": 6379 },
      "file":  { "Path": "storage/framework/cache" }
    }
  }
}
```
نام storeها case-insensitive است. بخش هر store را factory همان درایور می‌خواند.

## ۲. ثبت
```csharp
services.AddNaravelManager<CacheManager, ICacheDriver, CacheOptions>(configuration.GetSection("Cache"));
services.AddNaravelDriver<ICacheDriver>("file",  sp => new FileCacheDriver(sp.GetRequiredService<IOptionsMonitor<CacheOptions>>().CurrentValue.GetStore("file")));
services.AddNaravelDriver<ICacheDriver>("redis", async (sp, ct) => await RedisCacheDriver.ConnectAsync(/* ... */, ct));
```
قبل از build شدن provider انجام شود. ماژول‌ها معمولاً یک wrapper مثل `AddNaravelCache()` برای ثبت درایورهای داخلی دارند.

## ۳. مصرف — یکی را انتخاب کنید
| نیاز | Inject | فراخوانی |
|---|---|---|
| «فقط پیش‌فرض کانفیگ» (۹۹٪ کدها) | `ICacheDriver` | `cache.Get(...)` |
| انتخاب درایور در runtime / پیروی از تغییر default / درایور فقط-async | `CacheManager` | `manager.Driver("redis")`، `await manager.DriverAsync("redis", ct)` |

`ICacheDriver` تزریق‌شده **یک‌بار** resolve می‌شود (default در اولین تزریق) و از تغییرات بعدی `Default` پیروی نمی‌کند؛ برای آن Manager را استفاده کنید.

## ۴. توسعه در runtime (معادل `Cache::extend`)
```csharp
manager.Extend("tenant-cache", sp => new TenantCacheDriver(sp.GetRequiredService<ITenantAccessor>()));
manager.Driver("tenant-cache");
```
بعد از build شدن host کار می‌کند و thread-safe است. ثبت دوبارهٔ یک نام، factory را عوض و نمونهٔ cache‌شده را بازنشسته می‌کند.

## ۵. Hot reload
با `reloadOnChange: true` روی `appsettings.json` (پیش‌فرض `WebApplication.CreateBuilder`)، ویرایش مقادیر یک store باعث می‌شود
`manager.Driver(name)` بعدی درایور جدید بسازد. تغییر فقط `Default` پیش‌فرض را عوض می‌کند. ویرایش‌های بی‌ربط کاری نمی‌کنند.
وابستگی خارج از Stores دارید؟ هنگام تغییر آن `manager.Forget(name)` را صدا بزنید.

## خطاهای محتمل
| Exception | معنی |
|---|---|
| `DriverNotRegisteredException` | نام ناشناخته؛ پیام فهرست درایورهای موجود را می‌دهد. |
| `InvalidOperationException` «No default driver…» | `Default` خالی است. |
| `InvalidOperationException` «…asynchronous factory…» | `Driver()` روی درایور فقط-async که هنوز ساخته نشده؛ از `DriverAsync` استفاده کنید. |
| `InvalidOperationException` «Store 'x' is not configured» | `GetStore("x")` روی store ناموجود. |
| `ObjectDisposedException` | Manager قبلاً dispose شده. |
