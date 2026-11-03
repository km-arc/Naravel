# شروع کار

کدام بسته را نصب کنیم، کدام فراخوانی آن را ثبت می‌کند و برنامهٔ نمونه چطور اجرا می‌شود. نسخهٔ کوتاه در [README](../../README.fa.md) است؛ هر ماژول راهنمای جداگانهٔ خودش را کنار همین صفحه دارد.

## نصب

Naravel برای **.NET 10** (`net10.0`) است. هر ماژول یک بستهٔ NuGet جداست و همهٔ بسته‌ها یک نسخهٔ یکسان دارند. ماژول‌هایی را که لازم دارید اضافه کنید؛ وابستگی‌هایشان، از جمله `Naravel.Foundation`، خودکار می‌آید:

```sh
dotnet add package Naravel.Cache          # یک ماژول اصلی
dotnet add package Naravel.Cache.Redis    # یک provider، فقط اگر از Redis استفاده می‌کنید
```

همهٔ بسته‌های Naravel را در یک پروژه روی یک نسخه نگه دارید: با هم ساخته و تست می‌شوند.

## کدام بسته‌ها را لازم دارم

| می‌خواهید | نصب کنید | سپس ثبت کنید |
|---|---|---|
| Cache | `Naravel.Cache`؛ برای storeهای Redis یا Memcached، `Naravel.Cache.Redis` یا `Naravel.Cache.Memcached` را هم اضافه کنید | `AddNaravelCache(configuration)` |
| Queue | `Naravel.Queue` و یک driver: `Naravel.Queue.Memory`، `.File`، `.Redis`، `.Database`، `.RabbitMQ` یا `.Kafka` | `AddQueue(configuration)` و `Add...Driver` همان driver |
| هماهنگی صف (بدون هم‌پوشانی، محدودیت نرخ، jobهای یکتا) | `Naravel.Queue.Coordination` | [queue-coordination](queue-coordination.md) را ببینید |
| Events | `Naravel.Events`؛ برای listenerهای صف‌شده `Naravel.Events.Queue` | `AddNaravelEvents()` |
| Mail | `Naravel.Mail`؛ برای ارسال با SMTP `Naravel.Mail.Smtp` و برای ایمیل صف‌شده `Naravel.Mail.Queue` | `AddNaravelMail(configuration)` |
| Notifications | `Naravel.Notifications`؛ در صورت نیاز `Naravel.Notifications.Mail` و `Naravel.Notifications.Queue` | `AddNaravelNotifications()` |
| Query builder | `Naravel.Database` و یک dialect: `Naravel.Database.Sqlite`، `.MySql`، `.Postgres` یا `.SqlServer`؛ برای اشتراک اتصال یک context در EF Core، `Naravel.Database.EntityFrameworkCore` | `AddNaravelDatabase(configuration)` و `AddNaravel...Database` همان dialect |
| Pagination | `Naravel.Pagination`؛ برای کوئری‌های EF Core، `Naravel.Pagination.EntityFrameworkCore` | لازم نیست |
| فایل | `Naravel.Filesystem` | `AddNaravelFilesystem(configuration)` |
| Routing | `Naravel.Routing` (ASP.NET Core) | `AddNaravelRouting()` |
| Encryption | `Naravel.Encryption`؛ برای jobهای رمزگذاری‌شده `Naravel.Queue.Encryption` | `AddNaravelEncryption()` |
| Scheduling | `Naravel.Scheduling` | `AddNaravelScheduling(...)` |
| Broadcasting | `Naravel.Broadcasting` (ASP.NET Core، SignalR) | `AddNaravelBroadcasting(configuration)` |
| اتصال Redis | `Naravel.Redis` (`Naravel.Cache.Redis` و `Naravel.Queue.Redis` از قبل به آن وابسته‌اند) | `AddNaravelRedis(configuration)` |

جدول بخش «بسته‌های provider» (پایین‌تر) همهٔ فراخوانی‌های ثبت را دارد و هر بخش ماژول در [ماژول‌ها](modules.md) با یک مثال کوتاه شروع می‌شود. اول می‌خواهید اجرایش را ببینید؟ بخش «اجرای برنامهٔ نمونه» یک دستور برای اجرا دارد.

## بسته‌های provider

بستهٔ اصلی و فقط providerهای موردنیاز storeهای پیکربندی‌شده را ثبت کنید. نمونه‌های زیر انتخاب‌های جایگزین‌اند؛ درایور Database علاوه بر این‌ها به EF Core context و نگاشت `ConfigureQueueJobs()` نیاز دارد. نیازمندی‌های هر provider در مستندات ماژول آمده است.

| بسته | نمونهٔ ثبت |
|---|---|
| `Naravel.Cache` (Memory) | `builder.Services.AddNaravelCache(configuration);` |
| `Naravel.Redis` | `builder.Services.AddNaravelRedis(configuration);` |
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
| `Naravel.Broadcasting` | `builder.Services.AddNaravelBroadcasting(configuration); app.MapNaravelBroadcasting("/hubs/broadcast");` |
| `Naravel.Database` | `builder.Services.AddNaravelDatabase(configuration);` |
| `Naravel.Database.Sqlite` | `builder.Services.AddNaravelSqliteDatabase(configuration);` |
| `Naravel.Database.MySql` | `builder.Services.AddNaravelMySqlDatabase(configuration);` |
| `Naravel.Database.Postgres` | `builder.Services.AddNaravelPostgresDatabase(configuration);` |
| `Naravel.Database.SqlServer` | `builder.Services.AddNaravelSqlServerDatabase(configuration);` |

extension methodهای Queue در `Naravel.Queue.Extensions` قرار دارند. بخش تنظیمات Queue به‌طور پیش‌فرض `NaravelQueue:Stores`، Cache بخش `Cache:Stores` و Filesystem بخش `Filesystem:Stores` است؛ برای هرکدام می‌توان نام بخش دیگری تعیین کرد.

## اجرای برنامهٔ نمونه

`samples/Naravel.Sample` یک برنامهٔ وب قابل اجراست که ماژول‌ها را نشان می‌دهد و هرگز pack نمی‌شود.

```sh
dotnet run --project samples/Naravel.Sample/Naravel.Sample.csproj
```

| نشانی | چه چیزی را نشان می‌دهد |
|---|---|
| `/queue` | dispatch در Queue، تأخیر، اولویت، retry/backoff، زنجیره و batch (به‌طور پیش‌فرض روی فایل) |
| `/events` | dispatch رویداد `OrderPlaced`: listenerهای DI، توقف انتشار، callback از نوع `Listen` و listener صف‌شده |
| `/routing/` | گروه مسیر، constraint، resource، model binding و route middleware |
| `/database` | query builder روی SQLite درون‌حافظه: شرط‌هایی که به‌ازای هر پارامتر درخواست اضافه می‌شوند، مقدارهای bind‌شده، نام ستون ردشده، `WhereYear`، `FindAsync` و query log |

برای تعویض provider و headerهای نمونه، [samples/Naravel.Sample/README.md](../../samples/Naravel.Sample/README.md) را ببینید.

## ساخت از سورس

به .NET 10 SDK نیاز است. از ریشهٔ مخزن اجرا کنید:

```sh
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

این مخزن monorepo است: `Naravel.slnx` همهٔ ماژول‌ها، providerها، تست‌ها و sampleها را دربرمی‌گیرد. نسخه‌های مرکزی packageها در `Directory.Packages.props` و تنظیمات مشترک build و نسخهٔ lock-step در `Directory.Build.props` نگهداری می‌شوند.
