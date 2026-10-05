# ناراول (Naravel)

هدف ناراول، سادگی و APIهای آشنای لاراول در کنار سرعت دات‌نت است: تایپ قوی، `async/await`، تزریق وابستگی و کتابخانه‌های
پربازدهٔ بومی .NET. هر قابلیت لاراول فقط وقتی پیاده می‌شود که روی امکانات .NET **آورده** داشته باشد؛ تصمیم در PDR ثبت می‌شود.

**وضعیت:** `Naravel.Foundation`، `Naravel.Queue`، `Naravel.Filesystem` (PDR-008) و `Naravel.Cache` (PDR-007) پیاده‌سازی شده‌اند. Stage 4a از `Naravel.Routing` تأیید شده؛ middlewareهای آماده همچنان Stage 4b هستند. دیگر ماژول‌های فهرست‌شده کارهای آینده‌اند.

> **راستی‌آزمایی:** ۲۰۲۶-۱۰-۰۵ با .NET 10: `dotnet build Naravel.slnx -c Release --no-restore` هر ۲۰ پروژه را بدون warning و error ساخت؛ `dotnet test Naravel.slnx -c Release --no-restore` هر ۱۹۹ تست را موفق گذراند (Foundation 58، Queue 30، Filesystem 20، Cache 27، Routing 64). اتصال زنده به Redis/Memcached/AWS اجرا نشد.
>
> | پروژهٔ تست | تعداد تست |
> |---|---|
> | `Naravel.Foundation.Tests` | ۵۸ |
> | `Naravel.Queue.Tests` | ۳۰ |
> | `Naravel.Filesystem.Tests` | ۲۰ |
> | `Naravel.Cache.Tests` | ۲۷ |
> | `Naravel.Routing.Tests` | ۶۴ |

- انگلیسی: [README.md](README.md)
- ایجنت‌های هوش مصنوعی / مشارکت‌کنندگان: [AGENTS.md](AGENTS.md)
- نقشه‌راه و اولویت فعلی: [ROADMAP.md](ROADMAP.md) · مرور فارسی: [ROADMAP.fa.md](ROADMAP.fa.md)
- مستندات: [docs/fa](docs/fa) · تصمیم‌ها: [docs/pdr/fa](docs/pdr/fa)

## Cache در ۳۰ ثانیه
```csharp
using Naravel.Cache;
using Naravel.Cache.Abstractions;

builder.Services.AddNaravelCache(builder.Configuration)
    .AddNaravelRedisCache(builder.Configuration);

public sealed class SettingsService(ICacheStore cache)
{
    public Task<string> GetAsync(CancellationToken ct) =>
        cache.RememberAsync("site-name", TimeSpan.FromMinutes(10), _ => Task.FromResult("Naravel"), ct);
}

public sealed class Reports(CacheManager cache)
{
    public ICacheStore Redis => cache.Store("redis");
}
```
برای سادگی شبیه لاراول بدون facade ایستا، `ICacheStore` را برای store پیش‌فرض تزریق کنید و برای انتخاب store نام‌دار، scope یا tag از `CacheManager` استفاده کنید. تنظیمات و محدودیت providerها: [docs/fa/cache.md](docs/fa/cache.md) / [docs/en/cache.md](docs/en/cache.md).

## Naravel.Foundation
ماژول‌ها از manager مشترک Foundation استفاده می‌کنند تا resolve/cache درایور، `Extend` و reload تنظیمات تکرار نشود. توسعه‌دهندهٔ برنامه معمولاً مستقیم با Foundation کار نمی‌کند؛ APIهای ماژول را مصرف می‌کند.

## Naravel.Cache (کش، tag، scope و lock)
`Naravel.Cache` شامل storeهای Memory/Redis/Memcached، `RememberAsync`، scope صریح user/tenant، invalidation مبتنی بر نسخهٔ tag و lock توکنی است. فقط بستهٔ providerهای موردنیاز را اضافه کنید. جزئیات: [docs/fa/cache.md](docs/fa/cache.md).

## Naravel.Queue (صف کار پس‌زمینه)

صف کار مبتنی بر درایور، ساخته‌شده روی `Naravel.Foundation`: dispatch، تأخیر، اولویت، retry با backoff،
زنجیره‌سازی، batching، middleware، worker، `Extend` در زمان اجرا، hot reload کانفیگ. مستندات کامل با
مثال: [`docs/en/queue.md`](docs/en/queue.md) / [`docs/fa/queue.md`](docs/fa/queue.md).

```csharp
builder.Services.AddQueue(builder.Configuration).AddRedisDriver(builder.Configuration); // بخش کانفیگ: "NaravelQueue:Stores"
builder.Services.AddQueueWorker(w => { w.Queues = new[] { "default" }; w.Concurrency = 4; });
await dispatcher.DispatchAsync(new SendWelcomeEmailJob("ali@example.com"));
```

**تضمین تحویل: حداقل یک‌بار.** jobها باید idempotent باشند. ادامهٔ زنجیره قبل از ack شدن job منتشر می‌شود.

**بازیابی بعد از کرش.** درایورهای Redis، File و Database با `VisibilityTimeoutSeconds` (پیش‌فرض ۳۰۰) این را پشتیبانی می‌کنند.

**خلأهای شناخته‌شده.** تست برای worker، درایورهای Memory/File، و مهاجرت روی Foundation (چند store، `Extend`، reload کانفیگ) وجود دارد؛
Redis، Database، RabbitMQ و Kafka هنوز تست ندارند (`PROGRESS.md` را ببینید).

## Naravel.Filesystem (دیسک‌های ذخیره‌سازی نام‌دار)

`Naravel.Filesystem` برای ذخیره‌سازی local و S3 از manager درایور Foundation استفاده می‌کند و config reload، `Extend` زمان اجرا و آزادسازی driverها توسط manager را دارد. درایور local مسیرهای خارج از ریشهٔ تنظیم‌شده را رد می‌کند. تنظیمات، upload، URLهای S3 و محدودیت‌ها: [docs/en/filesystem.md](docs/en/filesystem.md) / [docs/fa/filesystem.md](docs/fa/filesystem.md).

## ساختار ریپو (مونوریپو)
یک ریپو، یک solution (`Naravel.slnx`)، همهٔ ماژول‌ها. `src/Naravel.<Module>` خود ماژول، `src/Naravel.<Module>.<Provider>` درایور با وابستگی
سنگین، `tests/` تست‌ها و `samples/` نمونه‌های اجرایی است. نسخهٔ پکیج‌ها در `Directory.Packages.props` و تنظیمات مشترک build و نسخهٔ
واحد lock-step در `Directory.Build.props` است. قوانین و وضعیت فعلی ماژول‌ها در `AGENTS.md` آمده.

## ساخت و تست
```
dotnet restore Naravel.slnx && dotnet build Naravel.slnx -c Release && dotnet test Naravel.slnx -c Release
```
نیازمند .NET 10 SDK (آخرین LTS).

## Naravel.Routing (روت و middleware به سبک لاراول)

یک لایهٔ نازک روی ASP.NET Core: گروه‌های تودرتو، مسیر نام‌دار، `where`، route model binding، مسیرهای resource و موتور middleware با alias، group،
پارامتر (`throttle:60,1`)، اولویت، `withoutMiddleware`، attribute کنترلر و middleware از نوع terminable. جزئیات: [docs/fa/routing.md](docs/fa/routing.md).

```csharp
builder.Services.AddNaravelRouting(o => o.Middleware.Alias<EnsureAge>("age").Group("api", "bindings", "age:18"));
app.UseNaravelRouting();
app.MapNaravel(r => r.Prefix("admin").Name("admin.").Middleware("api").Group(g =>
    g.Get("users/{user}", (string user) => user).Name("users.show")));
```

**وضعیت:** مرحلهٔ ۴a با ۶۴ تست نوشته شده و راستی‌آزمایی شده است (۶۴ از ۶۴ تست موفق و build بدون هشدار). گام بعدی: [PROGRESS-ROUTING.md](PROGRESS-ROUTING.md) را دنبال کنید.
مرحلهٔ ۴b middlewareهای آماده (throttle، signed URL، maintenance mode ...) را درون `Naravel.Routing` اضافه می‌کند؛ این پیاده‌سازی‌ها هنوز موجود نیستند.
