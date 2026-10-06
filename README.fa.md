# ناراول (Naravel)

APIهای آشنا و قابل‌کشف با الهام از لاراول، همراه با نقاط قوت .NET: تایپ قوی، `async/await`، تزریق وابستگی و کارایی بومی. یک ایدهٔ لاراولی فقط وقتی پذیرفته می‌شود که نسبت به امکانات خود .NET ارزش عملی اضافه کند؛ دلیل تصمیم در PDR ثبت می‌شود.

**وضعیت ماژول‌ها و نقشه‌راه:** مرجع رسمی [ROADMAP.md](ROADMAP.md) است. نتیجهٔ build و test در [GitHub Actions](https://github.com/km-arc/Naravel/actions/workflows/ci.yml) گزارش می‌شود.

- انگلیسی: [README.md](README.md)
- راهنمای مشارکت و ایجنت‌ها: [AGENTS.md](AGENTS.md)
- مستندات فارسی: [docs/fa](docs/fa) · PDRها: [docs/pdr/fa](docs/pdr/fa)
- مستندات انگلیسی: [docs/en](docs/en) · PDRها: [docs/pdr/en](docs/pdr/en)

## ماژول‌ها

### Naravel.Foundation

زیرساخت مشترک مدیریت درایورها برای ماژول‌های مبتنی بر driver: resolve و cache درایورهای نام‌دار، `Extend` در زمان اجرا، واکنش به تغییر تنظیمات و مدیریت آزادسازی درایورها. برنامه‌ها معمولاً به‌جای ثبت مستقیم Foundation، از آن از مسیر Cache، Queue یا Filesystem استفاده می‌کنند. برای نمونه، manager فایل‌سیستم می‌تواند درایو local را در زمان اجرا اضافه کند:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Naravel.Filesystem;
using Naravel.Filesystem.Drivers;

var storage = app.Services.GetRequiredService<StorageManager>();
storage.Extend("scratch", _ => new LocalStorageDriver("storage/scratch", "/scratch"));
var scratch = storage.Disk("scratch");
```

### Naravel.Cache

storeهای نام‌دار Memory، Redis و Memcached؛ `RememberAsync`؛ scope صریح user/tenant؛ بی‌اعتبارسازی مبتنی بر نسخهٔ tag؛ lock توکنی؛ و rate limiter پنجره‌ای مبتنی بر Cache. providerهای Redis و Memcached بسته‌های اختیاری‌اند.

```csharp
using Naravel.Cache.Abstractions;

builder.Services.AddNaravelCache(builder.Configuration)
    .AddNaravelRedisCache(builder.Configuration);

public sealed class SettingsService(ICacheStore cache)
{
    public Task<string> GetAsync(CancellationToken ct) =>
        cache.RememberAsync("site-name", TimeSpan.FromMinutes(10), _ => Task.FromResult("Naravel"), ct);
}
```

برای store پیش‌فرض، `ICacheStore` را تزریق کنید؛ برای انتخاب store نام‌دار یا ساخت scope از `CacheManager` استفاده کنید. رفتار و محدودیت providerها، از جمله اثر `FlushAsync` روی کل cluster در Memcached، در [docs/fa/cache.md](docs/fa/cache.md) و [docs/en/cache.md](docs/en/cache.md) آمده است.

### Naravel.Queue

صف پس‌زمینهٔ async با درایورهای نام‌دار، dispatch تأخیردار و اولویت‌دار، retry و backoff، زنجیره، batch، مدیریت jobهای ناموفق، worker میزبانی‌شده و telemetry. تحویل **حداقل یک‌بار** است؛ jobها باید idempotent باشند. درایور Memory فقط در همان پردازه است؛ Redis، File و Database برای بازیابی از visibility timeout استفاده می‌کنند و Kafka و RabbitMQ به redelivery بروکر متکی‌اند.

```csharp
using Naravel.Queue.Extensions;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Jobs;

builder.Services.AddQueue(builder.Configuration)
    .AddRedisDriver(builder.Configuration);
builder.Services.AddJob<SendWelcomeEmailJob>("mail.welcome");
builder.Services.AddQueueWorker(w => { w.Queues = new[] { "default" }; w.Concurrency = 4; });

var app = builder.Build();
var dispatcher = app.Services.GetRequiredService<IJobDispatcher>();
await dispatcher.DispatchAsync(new SendWelcomeEmailJob("ali@example.com"));
```

برای تعریف job، تنظیمات، مدیریت jobهای ناموفق، batchهای پایدار، کنترل worker و راه‌اندازی providerها به [docs/fa/queue.md](docs/fa/queue.md) و [docs/en/queue.md](docs/en/queue.md) مراجعه کنید.

### Naravel.Filesystem

دیسک‌های نام‌دار برای ذخیره‌سازی local و S3/S3-compatible. درایور local مسیرهای بیرون از ریشهٔ تنظیم‌شده را رد می‌کند. Naravel برای فایل‌ها endpoint عمومی HTTP نمی‌سازد؛ ارائه یا کنترل دسترسی را جداگانه با ASP.NET Core انجام دهید.

```csharp
using Naravel.Filesystem;

builder.Services.AddNaravelFilesystem(builder.Configuration);

public sealed class ArchiveService(IStorageDriver storage)
{
    public Task SaveAsync(string key, Stream contents, CancellationToken ct) =>
        storage.PutAsync(key, contents, ct);
}
```

تنظیم دیسک، upload، URLهای S3 و نکات امنیتی: [docs/fa/filesystem.md](docs/fa/filesystem.md) و [docs/en/filesystem.md](docs/en/filesystem.md).

### Naravel.Routing

لایه‌ای نازک روی routing در ASP.NET Core: گروه‌های تودرتو، نام مسیر و ساخت URL، constraint، resource route، model binding و alias/group/parameter برای middleware مسیر.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelRouting();
var app = builder.Build();
app.UseNaravelRouting();
app.MapNaravel(routes => routes
    .Prefix("admin").Name("admin.")
    .Group(group => group.Get("users/{id}", (int id) => Results.Ok(new { id }))
        .Name("users.show")));
```

موتور route middleware موجود است، اما middlewareهای آمادهٔ throttle، signed URL و maintenance هنوز پیاده‌سازی نشده‌اند. جزئیات: [docs/fa/routing.md](docs/fa/routing.md) و [docs/en/routing.md](docs/en/routing.md).

## بسته‌های provider

بستهٔ اصلی و فقط providerهای موردنیاز storeهای پیکربندی‌شده را ثبت کنید. نمونه‌های زیر انتخاب‌های جایگزین‌اند؛ درایور Database علاوه بر این‌ها به EF Core context و نگاشت `ConfigureQueueJobs()` نیاز دارد. نیازمندی‌های هر provider در مستندات ماژول آمده است.

| بسته | نمونهٔ ثبت |
|---|---|
| `Naravel.Cache` (Memory) | `builder.Services.AddNaravelCache(configuration);` |
| `Naravel.Cache.Redis` | `builder.Services.AddNaravelRedisCache(configuration);` |
| `Naravel.Cache.Memcached` | `builder.Services.AddNaravelMemcachedCache(configuration);` |
| `Naravel.Queue.Memory` | `builder.Services.AddMemoryDriver(configuration);` |
| `Naravel.Queue.File` | `builder.Services.AddFileDriver(configuration);` |
| `Naravel.Queue.Redis` | `builder.Services.AddRedisDriver(configuration);` |
| `Naravel.Queue.Database` | `builder.Services.AddDatabaseDriver<AppDbContext>(configuration);` |
| `Naravel.Queue.RabbitMQ` | `builder.Services.AddRabbitMqDriver(configuration);` |
| `Naravel.Queue.Kafka` | `builder.Services.AddKafkaDriver(configuration);` |

extension methodهای Queue در `Naravel.Queue.Extensions` قرار دارند. بخش تنظیمات Queue به‌طور پیش‌فرض `NaravelQueue:Stores`، Cache بخش `Cache:Stores` و Filesystem بخش `Filesystem:Stores` است؛ برای هرکدام می‌توان نام بخش دیگری تعیین کرد.

## ساخت و تست

به .NET 10 SDK نیاز است. از ریشهٔ مخزن اجرا کنید:

```sh
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

این مخزن monorepo است: `Naravel.slnx` همهٔ ماژول‌ها، providerها، تست‌ها و sampleها را دربرمی‌گیرد. نسخه‌های مرکزی packageها در `Directory.Packages.props` و تنظیمات مشترک build و نسخهٔ lock-step در `Directory.Build.props` نگهداری می‌شوند.
