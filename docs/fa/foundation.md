# Naravel.Foundation

`Naravel.Foundation` زیرساخت مشترک مدیریت driverها برای ماژول‌های Naravel است. اگر در حال ساخت یک ماژول مبتنی بر driver هستید، این بسته را مستقیم نصب کنید؛ برنامه‌های کاربردی معمولاً آن را به‌صورت وابستگی ماژولی که استفاده می‌کنند دریافت می‌کنند.

## شروع سریع

یک قرارداد driver، گزینه‌ها و manager تعریف کنید. سپس پیش از ساخت service provider، manager و یک driver نام‌دار را ثبت کنید:

```csharp
using System; using Microsoft.Extensions.Configuration; using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Options; using Naravel.Foundation;
public interface IGreeter { string Greet(string name); }
public sealed class GreeterOptions : ManagerOptions { }
public sealed class GreeterManager(IServiceProvider sp, IDriverRegistry<IGreeter> registry, IOptionsMonitor<GreeterOptions> options)
    : Manager<IGreeter, GreeterOptions>(sp, registry, options);
public sealed class PoliteGreeter : IGreeter { public string Greet(string name) => $"Hello, {name}!"; }
services.AddNaravelManager<GreeterManager, IGreeter, GreeterOptions>(configuration.GetSection("Greeter"));
services.AddNaravelDriver<IGreeter>("polite", _ => new PoliteGreeter());
var greeter = provider.GetRequiredService<GreeterManager>().Driver("polite");
var greeting = greeter.Greet("Ada");
```

برای استفاده از `Driver()` بدون نام، مقدار `Greeter:Default` را در تنظیمات روی `polite` قرار دهید.

## امکانات بسته

- `Manager<TDriver, TOptions>` driver را بر اساس نام resolve می‌کند، هنگام دسترسی هم‌زمان فقط یک نمونه می‌سازد، آن را cache می‌کند و ثبت زمان اجرا با `Extend` را پشتیبانی می‌کند.
- `IDriverRegistry<TDriver>` registry امن برای thread و بدون حساسیت به بزرگی حروف است و factoryهای sync و async را نگه می‌دارد.
- `ManagerOptions` مقدار پیش‌فرض `Default` و بخش‌های تنظیمات `Stores` را فراهم می‌کند.
- `AddNaravelManager`، manager، registry و سرویس مستقیم driver پیش‌فرض را ثبت می‌کند. `AddNaravelDriver` و `ConfigureNaravelDrivers` برای ثبت factoryهای داخلی هستند.
- تغییرات `IOptionsMonitor<TOptions>` فقط وقتی driverهای cacheشده را باطل می‌کند که تنظیمات `Stores` واقعاً تغییر کرده باشد.

## انتخاب overload ثبت

overload زیر بخش تنظیمات را برای شما bind می‌کند:

`AddNaravelManager<TManager, TDriver, TOptions>(services, configuration)`

این overload از binding مبتنی بر reflection استفاده می‌کند و برای trimming و dynamic code هشدارهای مربوط را دارد.

برای برنامه‌های trimmed یا Native AOT، گزینهٔ مشخص را خودتان با `services.Configure<TOptions>(section)` bind کنید، در پروژه‌ای که binding را انجام می‌دهد configuration binding generator را فعال کنید و سپس overload بدون `IConfiguration` را فراخوانی کنید:

```csharp
services.Configure<GreeterOptions>(configuration.GetSection("Greeter"));
services.AddNaravelManager<GreeterManager, IGreeter, GreeterOptions>();
```

برای یک factory از `AddNaravelDriver` استفاده کنید. برای ثبت چند factory می‌توانید `ConfigureNaravelDrivers` را به کار ببرید. overload دارای `IServiceProvider`، root provider را هنگام ساخته‌شدن اولیهٔ registry دریافت می‌کند؛ از resolve کردن سرویس‌های scoped از آن خودداری کنید.

## Resolve، هم‌زمانی و طول عمر

`Driver(name)` نمونهٔ cacheشدهٔ آن نام را برمی‌گرداند. `DriverAsync(name, cancellationToken)` از factoryهای sync و async پشتیبانی می‌کند. فراخوانی‌های هم‌زمان یک فرایند ساخت مشترک دارند؛ لغو یک caller فقط انتظار همان caller را لغو می‌کند، نه عملیات factory مشترک را. ساخت ناموفق cache نمی‌شود.

متد sync یعنی `Driver` روی factory فقط-async منتظر نمی‌ماند. تا پیش از ساخته‌شدن چنین driverی از `DriverAsync` استفاده کنید.

`Forget(name)` و `ForgetAll()` نمونه‌های cacheشده را بازنشسته می‌کنند؛ آزادسازی آن‌ها تا زمان dispose شدن manager به تعویق می‌افتد تا استفاده‌کنندگان فعلی مختل نشوند. Manager درایورهایی را که مالکشان است آزاد می‌کند. اگر driver پیش‌فرض از طریق سرویس مستقیم `TDriver` ثبت‌شده توسط `AddNaravelManager` resolve شود، مالکیت آزادسازی آن با dependency injection است و manager از آزادسازی همان نمونه صرف‌نظر می‌کند. factory باید نمونه‌ای برگرداند که manager مالک آن باشد، نه singleton نامرتبط متعلق به container.

## وابستگی‌ها و راهنماها

این بسته به abstractionهای `Microsoft.Extensions.*` وابسته است و به هیچ ماژول دیگری از Naravel وابستگی ندارد. برای قراردادها و مثال‌های کامل‌تر، [راهنمای معماری](architecture.md)، [راهنمای ساخت ماژول](module-authoring-guide.md) و [راهنمای trimming/AOT](trimming-aot.md) را ببینید.

English documentation: [Foundation guide](https://github.com/km-arc/Naravel/blob/main/docs/en/foundation.md).
