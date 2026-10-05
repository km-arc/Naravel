# راهنمای ساخت ماژول (روی Foundation)

این چک‌لیست را برای هر ماژول درایور-محور (Cache، Queue، Mail، Filesystem، Session، Broadcasting ...) دنبال کنید.

## ۰. قبل از کد
- `AGENTS.md` و `docs/pdr/fa/PDR-001..006` و `PDR-009` را بخوانید.
- برای ماژول یک **PDR جدید** بنویسید: ویژگی لاراول ← گزینهٔ native دات‌نت ← آورده؟ ← تصمیم ← گزینه‌های ردشده.
- برای تصمیم‌های بزرگ تایید مالک بگیرید.

## ۱. پروژه (مونوریپو)
همه‌چیز در همین ریپو و یک solution است.
- `src/Naravel.<Module>/Naravel.<Module>.csproj` فقط به `Naravel.Foundation` ارجاع دارد (به‌علاوهٔ آنچه ماژول واقعاً نیاز دارد). درایوری با وابستگی سنگین (Redis، RabbitMQ، EF Core ...) پروژهٔ جدا می‌گیرد: `src/Naravel.<Module>.<Provider>`.
- تست‌ها در `tests/Naravel.<Module>.Tests` (xUnit + FluentAssertions 7.x).
- **هر** پروژهٔ جدید را به `Naravel.slnx` اضافه کنید.
- نسخهٔ پکیج جدید فقط در `Directory.Packages.props`؛ فایل‌های `.csproj` بدون `Version` و بدون تکرار target framework یا متادیتای NuGet (این‌ها از `Directory.Build.props` می‌آیند، از جمله نسخهٔ مشترک lock-step).
- ریپو، solution یا workflow CI جدا برای هر ماژول نسازید.

## ۲. قرارداد، options، manager
```csharp
public interface ICacheDriver { ValueTask<string?> GetAsync(string key, CancellationToken ct = default); /* ... */ }

public sealed class CacheOptions : ManagerOptions { /* تنظیمات کل ماژول مثل Prefix */ }

public sealed class CacheManager(IServiceProvider sp, IDriverRegistry<ICacheDriver> registry, IOptionsMonitor<CacheOptions> options)
    : Manager<ICacheDriver, CacheOptions>(sp, registry, options);
```
`DefaultDriverName` را فقط برای fallback مخصوص ماژول override کنید. منطق resolve، cache، `Extend` یا invalidate را **دوباره ننویسید**.

## ۳. درایورهای داخلی
هر درایور بخش store **خودش** را می‌خواند و options تایپ‌دار خودش را bind می‌کند:
```csharp
services.AddNaravelDriver<ICacheDriver>("file", sp =>
{
    var store = sp.GetRequiredService<IOptionsMonitor<CacheOptions>>().CurrentValue.GetStore("file");
    return new FileCacheDriver(store["Path"] ?? "storage/cache");
});
```
قوانین:
- **مالکیت:** Manager آنچه factoryها برمی‌گردانند را dispose می‌کند. نمونه‌ها را با `new` / `ActivatorUtilities.CreateInstance` بسازید؛ singleton متعلق به container را برنگردانید.
- **`Dispose`/`DisposeAsync` باید idempotent باشد** (DI ممکن است درایور پیش‌فرض را هم dispose کند).
- برای درایورهای I/O از `IAsyncDisposable` و برای هر چیزی که روی شبکه وصل می‌شود از factory async استفاده کنید.
- اگر درایور به کانفیگ *خارج* از بخش store خودش وابسته است (مثل `connection => 'cache'` لاراول)، هنگام تغییر آن `manager.Forget(name)` را صدا بزنید.

## ۴. extension ثبت
```csharp
public static IServiceCollection AddNaravelCache(this IServiceCollection s, IConfiguration c)
{
    s.AddNaravelManager<CacheManager, ICacheDriver, CacheOptions>(c);
    s.AddNaravelDriver<ICacheDriver>("file", /* ... */);
    return s;
}
```

## ۵. تست (اجباری)
- هر درایور داخلی تست‌های خودش را دارد.
- اتصال Manager: resolve پیش‌فرض، نام صریح، `Extend` در runtime، بازسازی درایور با reload کانفیگ.
- رفتار Foundation (ساخت یک‌باره، cancel، dispose) را دوباره تست نکنید؛ در تست‌های Foundation است.

## ۶. مستندات (اجباری، هر دو زبان)
`docs/en/<module>.md` و `docs/fa/<module>.md`، ردیف‌های `laravel-parity.md` (هر دو زبان)، PDR(ها) در هر دو زبان، و XML doc روی هر عضو عمومی:
هدف، **معادل لاراول**، **چرا وجود دارد / چرا native نه**، **چه چیزی پورت نشد**.

## ۷. تعریف «تمام‌شده»
`dotnet test` سبز · مستندات EN+FA · PDR تایید‌شده · جدول parity به‌روز · هیچ وابستگی از Foundation به ماژول شما.
