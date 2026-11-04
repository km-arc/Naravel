# Naravel.Foundation

`Naravel.Foundation` پایهٔ مشترک ماژول‌های مبتنی بر driver در Naravel است (Cache، Queue، Filesystem، Mail و بقیه). این بسته **driverهای نام‌دار** را برای یک قرارداد resolve می‌کند (مثلاً «store با نام `redis` در Cache»)، هر کدام را فقط یک بار می‌سازد، cache می‌کند و اجازه می‌دهد در حال اجرای برنامه driver تازه اضافه یا جایگزین شود.

برنامه‌ها معمولاً هرگز آن را مستقیم نصب نمی‌کنند: به‌عنوان وابستگی ماژولی که استفاده می‌کنید می‌آید. وقتی **ماژولی مبتنی بر driver برای خودتان می‌نویسید** آن را مستقیم نصب کنید.

## شروع سریع

یک ماژول مبتنی بر driver چهار بخش دارد: قرارداد driver، گزینه‌ها، manager و یک یا چند driver. این کوچک‌ترین مثال کامل است. یک پروژهٔ وب بسازید (`dotnet new web`)، بسته را اضافه کنید (`dotnet add package Naravel.Foundation`) و `Program.cs` را جایگزین کنید:

```csharp
using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Naravel.Foundation;

var builder = WebApplication.CreateBuilder(args);

// ۱. manager، registry مربوط به driverها و بخش تنظیماتی که می‌خواند را ثبت کنید.
builder.Services.AddNaravelManager<GreeterManager, IGreeter, GreeterOptions>(
    builder.Configuration.GetSection("Greeter"));

// ۲. یک driver داخلی را با یک نام ثبت کنید.
builder.Services.AddNaravelDriver<IGreeter>("polite", _ => new PoliteGreeter());

var app = builder.Build();
app.MapGet("/hello", (GreeterManager manager) => manager.Driver("polite").Greet("Ada"));
app.Run();

// ۳. قرارداد، گزینه‌ها و manager. manager فقط یک constructor است.
public interface IGreeter { string Greet(string name); }

public sealed class GreeterOptions : ManagerOptions { }

public sealed class GreeterManager(IServiceProvider sp, IDriverRegistry<IGreeter> registry, IOptionsMonitor<GreeterOptions> options)
    : Manager<IGreeter, GreeterOptions>(sp, registry, options) { }

public sealed class PoliteGreeter : IGreeter
{
    public string Greet(string name) => $"Hello, {name}!";
}
```

اجرایش کنید و `/hello` را باز کنید: جواب `Hello, Ada!` است.

### استفاده از driver پیش‌فرض

`manager.Driver()` بدون نام، از گزینهٔ `Default` استفاده می‌کند. آن را در `appsettings.json` بگذارید:

```json
{ "Greeter": { "Default": "polite" } }
```

```csharp
app.MapGet("/hi", (GreeterManager manager) => manager.Driver().Greet("Ada"));   // از "polite" استفاده می‌کند
```

بدون مقدار پیش‌فرض، `Driver()` یک `InvalidOperationException` پرتاب می‌کند: `No default driver is configured for IGreeter. Set the 'Default' option or pass a driver name explicitly.`

## امکانات بسته

| بخش | کارش |
|---|---|
| `Manager<TDriver, TOptions>` | driver را بر اساس نام resolve می‌کند، حتی با دسترسی هم‌زمان فقط یک بار می‌سازد، cache می‌کند و با `Extend` در زمان اجرا driver تازه می‌پذیرد. |
| `IDriverRegistry<TDriver>` | registry امن برای thread و بدون حساسیت به بزرگی حروف، برای factoryهای sync و async. |
| `ManagerOptions` | `Default` (نام driver پیش‌فرض) و `Stores` (بخش‌های تنظیمات نام‌دار، یکی برای هر driver پیکربندی‌شده). |
| `AddNaravelManager` | manager، registry و یک سرویس مستقیم `TDriver` برای driver پیش‌فرض را ثبت می‌کند. |
| `AddNaravelDriver`، `ConfigureNaravelDrivers` | factoryهای داخلی driver را ثبت می‌کنند (یکی، یا چند تا با هم). |

وقتی تنظیمات `Stores` عوض شود، `IOptionsMonitor<TOptions>` driverهای cacheشده را باطل می‌کند؛ تغییر در جای دیگر همان بخش این کار را نمی‌کند.

## driverهایی که تنظیماتشان را می‌خوانند

یک driver معمولاً تنظیمات لازم دارد. هر driver پیکربندی‌شده یک بخش زیر `Stores:<name>` دارد و `GetStore(name)` آن را برمی‌گرداند:

```json
{
  "Greeter": {
    "Default": "friendly",
    "Stores": {
      "friendly": { "Greeting": "Hi" }
    }
  }
}
```

```csharp
builder.Services.AddNaravelDriver<IGreeter>("friendly", sp =>
{
    var options = sp.GetRequiredService<IOptionsMonitor<GreeterOptions>>().CurrentValue;
    var store = options.GetStore("friendly");                 // بخش "Stores:friendly"
    return new FriendlyGreeter(store["Greeting"] ?? "Hello");
});

public sealed class FriendlyGreeter(string greeting) : IGreeter
{
    public string Greet(string name) => $"{greeting}, {name}!";
}
```

اگر بخش وجود نداشته باشد، `GetStore` یک `InvalidOperationException` پرتاب می‌کند: `Store 'friendly' is not configured. Configured stores: (none).` (پیام، storeهایی را که واقعاً هستند فهرست می‌کند).

## افزودن driver در حال اجرای برنامه

`Extend` یک driver را روی manager که از قبل ساخته شده ثبت می‌کند (معادل `Cache::extend` در لاراول). وقتی ساختن driver به I/O نیاز دارد از شکل async استفاده کنید:

```csharp
var manager = app.Services.GetRequiredService<GreeterManager>();

manager.Extend("shout", _ => new ShoutingGreeter());                 // factory هم‌زمان (sync)

manager.Extend("remote", async (sp, ct) =>                           // factory ناهمگام (async)
{
    var greeter = new RemoteGreeter();
    await greeter.ConnectAsync(ct);
    return greeter;
});

var shout = manager.Driver("shout");                                 // factoryهای sync با Driver() کار می‌کنند
var remote = await manager.DriverAsync("remote", ct);                // factoryهای فقط-async به DriverAsync() نیاز دارند
```

ثبت دوبارهٔ یک نام، factory آن را جایگزین و نمونهٔ cacheشده را بازنشسته می‌کند: فراخوانی بعدی از factory تازه یک نمونه می‌سازد، و کدی که نمونهٔ قدیمی را در دست دارد تا dispose شدن manager به استفاده از آن ادامه می‌دهد.

## انتخاب overload ثبت

`AddNaravelManager<TManager, TDriver, TOptions>(services, configuration)` بخش تنظیمات را برای شما bind می‌کند. این overload از binding مبتنی بر reflection استفاده می‌کند، پس هشدارهای trimming و dynamic code را دارد.

برای برنامه‌های trimmed یا Native AOT، نوع مشخص گزینه‌ها را خودتان با `services.Configure<TOptions>(section)` bind کنید، در پروژه‌ای که binding را انجام می‌دهد configuration binding generator را فعال کنید و overload بدون آرگومان `IConfiguration` را صدا بزنید:

```csharp
services.Configure<GreeterOptions>(configuration.GetSection("Greeter"));
services.AddNaravelManager<GreeterManager, IGreeter, GreeterOptions>();
```

برای یک factory از `AddNaravelDriver` و برای ثبت چند factory با هم از `ConfigureNaravelDrivers` استفاده کنید. overload دارای provider، هنگام ساخته‌شدن اولیهٔ registry، `IServiceProvider` ریشه را می‌گیرد؛ پس از آن سرویس scoped resolve نکنید.

## Resolve، هم‌زمانی و طول عمر

- `Driver(name)` نمونهٔ cacheشدهٔ آن نام را برمی‌گرداند. `DriverAsync(name, cancellationToken)` از factoryهای async هم پشتیبانی می‌کند.
- فراخوانی‌های هم‌زمان یک ساخت را به اشتراک می‌گذارند. لغو یک فراخوان فقط انتظار همان فراخوان را لغو می‌کند، نه فراخوانی مشترک factory را. ساختِ ناموفق cache نمی‌شود، پس فراخوانی بعدی دوباره تلاش می‌کند.
- متد sync یعنی `Driver` هرگز روی factory فقط-async منتظر نمی‌ماند. تا پیش از ساخته‌شدن چنین driverی `InvalidOperationException` پرتاب می‌کند: `Driver 'remote' is registered with an asynchronous factory and has not been created yet. Use DriverAsync to create it.` بعد از اینکه `DriverAsync` آن را ساخت، `Driver` آن را برمی‌گرداند.
- `Forget(name)` و `ForgetAll()` نمونه‌های cacheشده را بازنشسته می‌کنند تا فراخوانی بعدی نمونهٔ تازه بسازد. driverهای بازنشسته تا dispose شدن manager زنده می‌مانند، پس کدی که از قبل یکی را در دست دارد دچار اختلال نمی‌شود.
- manager، driverهایی را که factoryها ساخته‌اند dispose می‌کند. اگر driver پیش‌فرض از راه سرویس مستقیم `TDriver` که `AddNaravelManager` ثبت می‌کند resolve شده باشد، مالک و dispose‌کنندهٔ آن نمونه، dependency injection است و manager آن را رد می‌کند. پس factory باید نمونه‌ای برگرداند که manager حق دارد مالکش باشد، نه singleton نامرتبطی که container مالک آن است.

## خطاها

| چه وقت | exception و پیام |
|---|---|
| نام خالی است | `ArgumentException`: `Driver name must not be blank.` |
| نامی داده نشده و `Default` تنظیم نشده | `InvalidOperationException`: `No default driver is configured for IGreeter. Set the 'Default' option or pass a driver name explicitly.` |
| نام ثبت نشده است | `DriverNotRegisteredException`: `Driver 'x' is not registered for IGreeter. Available drivers: polite, shout.` سه ویژگی `DriverName`، `DriverType` و `AvailableDrivers` را دارد. |
| `Driver()` روی driver فقط-async که هنوز ساخته نشده | `InvalidOperationException`؛ از `DriverAsync` استفاده کنید. |
| `GetStore` روی بخش وجودنداشته | `InvalidOperationException`: `Store 'x' is not configured. Configured stores: a, b.` |
| manager را dispose کرده‌اند | `ObjectDisposedException` |

## تست ماژولی که روی Foundation ساخته شده

یک fake را با یک نام ثبت کنید تا کدِ تحت‌تست آن را مثل هر driver دیگری resolve کند و به هیچ backend واقعی نیاز نباشد:

```csharp
manager.Extend("fake", _ => new FakeGreeter());
Assert.Equal("Hello, Ada!", manager.Driver("fake").Greet("Ada"));
```

## معادل در لاراول

| لاراول (`Illuminate\Support\Manager`) | Naravel.Foundation |
|---|---|
| `driver('redis')` | `manager.Driver("redis")`، `DriverAsync("redis", ct)` |
| `getDefaultDriver()` | `manager.DefaultDriverName` (از `Default`) |
| `extend('name', $callback)` | `manager.Extend("name", factory)` |
| `__call` برای ارسال متدها به driver | عمداً منتقل نشده: روی همان driverی که resolve کرده‌اید متد را صدا بزنید. |

## وابستگی‌ها و مطالعهٔ بیشتر

این بسته به abstractionهای `Microsoft.Extensions.*` وابسته است و به هیچ ماژول دیگری از Naravel وابستگی ندارد. قراردادهای کامل در [راهنمای معماری](https://github.com/km-arc/Naravel/blob/main/docs/fa/architecture.md)، [راهنمای ساخت ماژول](https://github.com/km-arc/Naravel/blob/main/docs/fa/module-authoring-guide.md) و [راهنمای trimming و AOT](https://github.com/km-arc/Naravel/blob/main/docs/fa/trimming-aot.md) آمده است.

English documentation: [Foundation guide](https://github.com/km-arc/Naravel/blob/main/docs/en/foundation.md).
