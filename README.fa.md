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

صف پس‌زمینهٔ async با درایورهای نام‌دار، dispatch تأخیردار و اولویت‌دار، retry و backoff، زنجیره، batch، مدیریت jobهای ناموفق، worker میزبانی‌شده و telemetry. تحویل **حداقل یک‌بار** است؛ jobها باید idempotent باشند. درایور Memory فقط در همان پردازه است؛ Redis، File و Database برای بازیابی از visibility timeout استفاده می‌کنند و Kafka و RabbitMQ به redelivery بروکر متکی‌اند. Kafka تحویل با تأخیر را پشتیبانی نمی‌کند (dispatch تأخیری رد می‌شود و retry فوری است)؛ [docs/fa/queue.md](docs/fa/queue.md#kafka-بدون-تحویل-با-تأخیر) را ببینید.

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

### Naravel.Events

رویدادهای تایپ‌شدهٔ ناهمگام و درون‌فرایندی. listenerهای `IEventListener<TEvent>` که در DI ثبت شده‌اند به ترتیب ثبت اجرا می‌شوند و پس از آن‌ها callbackهای `Listen` مخصوص scope جاری فراخوانی می‌شوند (هرکدام یک subscription از نوع `IDisposable` برمی‌گردانند). هر listener می‌تواند با `context.Stop()` ادامهٔ انتشار را متوقف کند؛ نخستین exception به caller منتقل می‌شود و dispatch متوقف می‌گردد. listenerها بر اساس همان نوع دقیقی انتخاب می‌شوند که به `DispatchAsync<TEvent>` داده می‌شود، بدون fan-out بر اساس کلاس پایه یا interface. dispatcher از نوع scoped است، `Meter` و `ActivitySource` با نام `Naravel.Events` منتشر می‌کند و `Naravel.Events.Testing.EventFake` رویدادهای dispatch‌شده را برای تست ثبت می‌کند.

```csharp
using Naravel.Events;

builder.Services.AddNaravelEvents();
builder.Services.AddScoped<IEventListener<OrderPaid>, UpdateOrderReadModel>();

public sealed record OrderPaid(string OrderId);

public sealed class UpdateOrderReadModel : IEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken ct = default) =>
        Task.CompletedTask;
}

public sealed class CheckoutService(IEventDispatcher events)
{
    public Task PayAsync(string orderId, CancellationToken ct) =>
        events.DispatchAsync(new OrderPaid(orderId), ct);
}
```

adapter اختیاری `Naravel.Events.Queue` یک listener را به‌جای اجرای مستقیم، از طریق `Naravel.Queue` اجرا می‌کند. `IQueuedEventListener<TEvent>` را پیاده‌سازی و با یک alias صریح ثبت کنید؛ آنچه در صف ذخیره می‌شود alias است، نه نام نوع CLR. Queue و یک driver را مثل همیشه پیکربندی کنید. adapter روی connection پیش‌فرض و queue با نام `default` dispatch می‌کند و رویداد را با `System.Text.Json` serialize می‌کند؛ پس رویدادها را کوچک و قابل serialize نگه دارید. اجرای صف‌شده مانند هر job دیگر Queue به‌صورت at-least-once است.

```csharp
builder.Services.AddNaravelEventsQueue();
builder.Services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");
```

جزئیات: [docs/fa/events.md](docs/fa/events.md) و [docs/en/events.md](docs/en/events.md). برای یک مثال قابل اجرا، صفحهٔ `/events` در sample را اجرا کنید.

### Naravel.Mail

ایمیل تایپ‌شده بدون وابستگی شبکه‌ای در بستهٔ هسته: کلاس `Mailable` پیام را توصیف می‌کند، یک transport نام‌دار (به‌طور پیش‌فرض `log`، برای تست `array` و در تسکی بعدی `smtp` از طریق بستهٔ اختیاری) آن را تحویل می‌دهد و `MailFake` آن را قابل تست می‌کند. شکست transport به فراخواننده گزارش می‌شود و هرگز بی‌صدا تکرار نمی‌شود.

```csharp
builder.Services.AddNaravelMail(builder.Configuration);   // مقدار پیش‌فرض Mail:Default برابر "log" است

public class WelcomeMail(User user) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.From("app@example.com").Subject("Welcome").Text($"Hi {user.Name}").Build();
}

await mailer.To(user.Email).SendAsync(new WelcomeMail(user), cancellationToken);

// اختیاری Naravel.Mail.Queue: services.AddNaravelMailQueue(); سپس
await mailer.To(user.Email).QueueAsync(new WelcomeMail(user), cancellationToken);   // فقط دادهٔ پیام ذخیره می‌شود، at-least-once
```

جزئیات: [docs/fa/mail.md](docs/fa/mail.md) و [docs/en/mail.md](docs/en/mail.md).

### Naravel.Notifications

یک اعلان تایپ‌شده که از راه channelهایی که نام می‌برید، به همان ترتیب، برای یک گیرنده فرستاده می‌شود، با یک fake برای تست. بستهٔ هسته هیچ وابستگی به mail، queue یا شبکه ندارد؛ Mail (`Naravel.Notifications.Mail`) و Queue (`Naravel.Notifications.Queue`) بسته‌های اختیاری‌اند و هر channel دیگری یک کلاس یا delegate است. channel ثبت‌نشده پیش از ارسال هر چیزی شکست می‌خورد؛ channel ناموفق ارسال را متوقف می‌کند و `NotificationDeliveryException` می‌گوید کدام channel شکست خورد و کدام‌ها پیش‌تر تحویل داده بودند.

```csharp
builder.Services.AddNaravelNotifications();
builder.Services.AddLogNotificationChannel<InvoiceReady>();          // یک channel با نام "log"

await notifications.SendAsync(user, new InvoiceReady(7), ["log"], cancellationToken);   // notifications یک INotificationSender تزریق‌شده است

// اختیاری Naravel.Notifications.Mail: services.AddMailNotificationChannel<InvoiceReady>(c => c.GetRecipient<User>().Email, n => new InvoiceMail(n));
// اختیاری Naravel.Notifications.Queue: services.AddNaravelNotificationQueue(); services.AddQueuedNotification<InvoicePayload>("invoice.ready", handler);
//   await notificationQueue.QueueAsync("invoice.ready", payload);   // فقط alias و داده‌ٔ ساده ذخیره می‌شود، at-least-once
```

جزئیات: [docs/fa/notifications.md](docs/fa/notifications.md) و [docs/en/notifications.md](docs/en/notifications.md).

### Naravel.Encryption

یک facade نازک `Encrypt` / `Decrypt` روی ASP.NET Core Data Protection (بدون رمزنگاری سفارشی)، مقدارهای تایپ‌شده، جداسازی با `ForPurpose` و `DecryptException`. `Naravel.Queue.Encryption` payload جاب‌های علامت‌دار با `IShouldBeEncrypted` را رمزشده ذخیره می‌کند؛ نوع جاب همچنان فقط با alias registry انتخاب می‌شود. **تولیدکننده و worker باید key ring و نام برنامهٔ Data Protection را به‌اشتراک بگذارند.** نگاه کنید به [docs/en/encryption.md](docs/en/encryption.md) و [docs/fa/encryption.md](docs/fa/encryption.md).

```csharp
builder.Services.AddNaravelEncryption();
var token = encrypter.Encrypt("secret");
var text = encrypter.Decrypt(token);
// اختیاری Naravel.Queue.Encryption: services.AddNaravelQueueEncryption(); class MyJob : Job, IShouldBeEncrypted
```

### Naravel.Scheduling

کارهای نام‌دار طبق یک برنامه، که یک hosted service درون میزبان شما اجرا می‌کند: بدون خط cron و بدون دستور برای شروع. فرکانس‌ها و فیلترها مثل Laravel خوانده می‌شوند (`EveryFiveMinutes`، `DailyAt`، `Weekdays`، `Cron`) و دو محافظ بین چند نمونه روی قفل‌های کش کار می‌کنند: `WithoutOverlapping` و `OnOneServer` (اگر درایور قفل مشترک نباشد راه‌اندازی شکست می‌خورد). هر اجرا دامنه‌ی DI تازه می‌گیرد؛ `SchedulerFake` برنامه را در زمان‌هایی که انتخاب می‌کنید اجرا می‌کند. نیمه‌ی Artisan بسته نیست: دستورها یک دستورالعمل مستند با `System.CommandLine` هستند (PDR-017).

```csharp
builder.Services.AddNaravelScheduling(schedule =>
{
    schedule.Call("prune-sessions", (sp, ct) => sp.GetRequiredService<SessionPruner>().RunAsync(ct)).EveryFiveMinutes().WithoutOverlapping();
    schedule.Call("daily-digest", (sp, ct) => sp.GetRequiredService<Digest>().SendAsync(ct)).DailyAt(8, 0).OnOneServer();
});
```

نگاه کنید به [docs/en/scheduling.md](docs/en/scheduling.md) و [docs/fa/scheduling.md](docs/fa/scheduling.md).

### Naravel.Database

query builder به سبک Laravel با اتصال‌های نام‌دار و SQL با پارامتر، برای SQLite، MySQL، PostgreSQL و SQL Server (Dapper ردیف‌ها را map می‌کند؛ EF Core همچنان ابزار entity و migration است). **امروز:** اتصال‌ها، درایورهای SQLite، MySQL، PostgreSQL و SQL Server، builder با خانوادهٔ کامل where (مقدار، null، between، ستون، بخش تاریخ، any/all/none، الگو، متن کامل، مسیر JSON، exists و زیرکوئری)، شرطی‌ها، join، group by و having، کمک‌های ترتیب، limit، union، زیرکوئری و join جانبی (lateral)، CTE، عبارت raw همراه با binding، قفل ردیف و `ToSql`/`ToRawSql` که برای هر چهار دیالکت کامپایل می‌شود و روی هر چهار اجرا می‌شود (یک suite قرارداد مشترک در CI)، و مشاهده‌پذیری (meter و activity با نام `Naravel.Database` بدون SQL در tagها، `IQueryListener`، query log اختیاری) به‌همراه `DatabaseFake` برای تست. aggregate (`CountAsync`، `SumAsync`، `ExistsAsync`...)، chunk و استریم `IAsyncEnumerable` شامل هستند؛ نوشتن هم هست: `InsertAsync`، `InsertGetIdAsync`، `UpdateAsync`، `IncrementAsync`، `UpsertAsync`، `DeleteAsync`، `TruncateAsync` و `*ReturningAsync` و برای update و delete `where` صریح لازم است؛ تراکنش (`TransactionAsync` با savepoint و تکرار هنگام deadlock، `BeginTransactionAsync`، که `IDatabase` تزریق‌شده هم به آن می‌پیوندد) و آداپتور اختیاری EF Core (`Naravel.Database.EntityFrameworkCore`: `dbContext.Query("table")` و `db.Using(dbContext)` اتصال و تراکنش context را به اشتراک می‌گذارند)؛ یکپارچگی صفحه‌بندی در مرحله‌ای بعد می‌آید (فایل [roadmap/QUERY-BUILDER-COVERAGE.md](roadmap/QUERY-BUILDER-COVERAGE.md) را ببینید).

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);
builder.Services.AddNaravelSqliteDatabase(builder.Configuration);   // Database:Connections:main = { Driver: sqlite, Path: app.db }

var adults = await db.Table("users").Where("active", true).Where("age", ">=", 18).OrderBy("name").GetAsync(cancellationToken);
var ada    = await db.Table("users").Where("email", email).FirstAsync<UserRow>(cancellationToken);   // email پارامتر می‌شود، هرگز به متن SQL نمی‌چسبد

var authors = await db.Table("users")                       // join، مسیر JSON، exists، گروه‌بندی: همان نام‌های Laravel
    .Join("posts", "users.id", "posts.user_id")
    .WhereLike("users.name", "a%")
    .Where("users.settings->theme", "dark")                  // مسیر JSON، به‌صورت پارامتر bind می‌شود
    .WhereExists(s => s.Table("comments").WhereColumn("comments.post_id", "posts.id"))
    .When(onlyRecent, q => q.Latest("posts.created_at"))
    .Select("users.name").GroupBy("users.name")
    .GetAsync(cancellationToken);
```

مقدارها همیشه پارامتر می‌شوند، شناسه‌ها اعتبارسنجی می‌شوند و SQL خام فقط در متدهای `*Raw` وجود دارد. [docs/fa/database.md](docs/fa/database.md) و [docs/en/database.md](docs/en/database.md) را ببینید.

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

aliasهای آمادهٔ route middleware عبارت‌اند از `throttle` (پنجرهٔ ثابت در سطح فرایند)، `signed`، `maintenance`، `cache.headers`، `trim`، `convert.empty` و `guest`؛ aliasها، groupها و پارامترهای سفارشی هم پشتیبانی می‌شوند. وضعیت throttle و maintenance در سطح فرایند است و signed URL برای چند instance به key ring مشترک و پایدار Data Protection نیاز دارد. جزئیات: [docs/fa/routing.md](docs/fa/routing.md) و [docs/en/routing.md](docs/en/routing.md).

### Naravel.Pagination

بستهٔ `Naravel.Pagination` (بدون وابستگی به EF Core) صفحه‌هایی با شکل Laravel و `data`، `links` و `meta` برمی‌گرداند؛ `Naravel.Pagination.EntityFrameworkCore` متدهای `PaginateAsync`، `SimplePaginateAsync` و `CursorPaginateAsync` را به queryهای EF Core اضافه می‌کند. صفحه‌بندی cursor کلیدها را یک بار اعلام می‌کند و هم `ORDER BY` و هم `WHERE` را می‌نویسد؛ cursor یک موقعیتِ بدون امضاست که سخت‌گیرانه خوانده می‌شود (`InvalidCursorException`) و اندازهٔ صفحه حداکثر ۱۰۰ است.

```csharp
var page = await db.Posts.OrderBy(p => p.Id).PaginateAsync(page: 2, perPage: 15);
var next = await db.Posts.CursorPaginateAsync(k => k.By(p => p.CreatedAt).Then(p => p.Id), 15, cursor);
```

راهنما: [docs/en/pagination.md](docs/en/pagination.md) و [docs/fa/pagination.md](docs/fa/pagination.md).

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
| `Naravel.Events.Queue` (adapter) | `builder.Services.AddNaravelEventsQueue();` |
| `Naravel.Mail` | `builder.Services.AddNaravelMail(configuration);` |
| `Naravel.Mail.Queue` | `builder.Services.AddNaravelMailQueue();` |
| `Naravel.Mail.Smtp` | `builder.Services.AddNaravelMailSmtp(configuration);` |
| `Naravel.Notifications` | `builder.Services.AddNaravelNotifications();` |
| `Naravel.Notifications.Mail` | `builder.Services.AddMailNotificationChannel<TNotification>(address, mailable);` |
| `Naravel.Notifications.Queue` | `builder.Services.AddNaravelNotificationQueue();` |
| `Naravel.Encryption` | `builder.Services.AddNaravelEncryption();` |
| `Naravel.Queue.Encryption` | `builder.Services.AddNaravelQueueEncryption();` |
| `Naravel.Scheduling` | `builder.Services.AddNaravelScheduling(schedule => schedule.Call("name", handler).EveryFiveMinutes());` |
| `Naravel.Database` | `builder.Services.AddNaravelDatabase(configuration);` |
| `Naravel.Database.Sqlite` | `builder.Services.AddNaravelSqliteDatabase(configuration);` |
| `Naravel.Database.MySql` | `builder.Services.AddNaravelMySqlDatabase(configuration);` |
| `Naravel.Database.Postgres` | `builder.Services.AddNaravelPostgresDatabase(configuration);` |
| `Naravel.Database.SqlServer` | `builder.Services.AddNaravelSqlServerDatabase(configuration);` |

extension methodهای Queue در `Naravel.Queue.Extensions` قرار دارند. بخش تنظیمات Queue به‌طور پیش‌فرض `NaravelQueue:Stores`، Cache بخش `Cache:Stores` و Filesystem بخش `Filesystem:Stores` است؛ برای هرکدام می‌توان نام بخش دیگری تعیین کرد.

## برنامهٔ نمونه

`samples/Naravel.Sample` یک برنامهٔ وب قابل اجراست که ماژول‌ها را نشان می‌دهد و هرگز pack نمی‌شود.

```sh
dotnet run --project samples/Naravel.Sample/Naravel.Sample.csproj
```

| نشانی | چه چیزی را نشان می‌دهد |
|---|---|
| `/queue` | dispatch در Queue، تأخیر، اولویت، retry/backoff، زنجیره و batch (به‌طور پیش‌فرض روی فایل) |
| `/events` | dispatch رویداد `OrderPlaced`: listenerهای DI، توقف انتشار، callback از نوع `Listen` و listener صف‌شده |
| `/routing/` | گروه مسیر، constraint، resource، model binding و route middleware |

برای تعویض provider و headerهای نمونه، [samples/Naravel.Sample/README.md](samples/Naravel.Sample/README.md) را ببینید.

## ساخت و تست

به .NET 10 SDK نیاز است. از ریشهٔ مخزن اجرا کنید:

```sh
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

این مخزن monorepo است: `Naravel.slnx` همهٔ ماژول‌ها، providerها، تست‌ها و sampleها را دربرمی‌گیرد. نسخه‌های مرکزی packageها در `Directory.Packages.props` و تنظیمات مشترک build و نسخهٔ lock-step در `Directory.Build.props` نگهداری می‌شوند.
