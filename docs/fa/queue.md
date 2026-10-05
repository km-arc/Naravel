# Naravel.Queue

یک صف کار پس‌زمینه با الهام از لاراول برای دات‌نت، مبتنی بر `Naravel.Foundation`
(`Manager<IQueueDriver, QueueOptions>`). معادل لاراول: `illuminate/queue`
(`Queue::push()`, `Queue::connection()`, `php artisan queue:work`).

## پکیج‌ها

| پکیج | درایور | متد ثبت |
|---|---|---|
| `Naravel.Queue` | `memory` (درون‌حافظه، بدون وابستگی) | `AddMemoryDriver(configuration)` |
| `Naravel.Queue.File` | `file` (فایل JSON روی دیسک) | `AddFileDriver(configuration)` |
| `Naravel.Queue.Redis` | `redis` | `AddRedisDriver(configuration)` |
| `Naravel.Queue.Database` | `database` (هر provider از EF Core) | `AddDatabaseDriver<TContext>(configuration)` |
| `Naravel.Queue.RabbitMQ` | `rabbitmq` | `AddRabbitMqDriver(configuration)` |
| `Naravel.Queue.Kafka` | `kafka` | `AddKafkaDriver(configuration)` |

## کانفیگ

طبق PDR-005، همهٔ ماژول‌های Naravel از یک کلمهٔ مشترک `"Stores"` برای کانکشن‌های نام‌دار استفاده می‌کنند؛
سطح API زبان C# همچنان واژهٔ «connection» را دارد، دقیقاً مثل لاراول:

```json
{
  "NaravelQueue": {
    "Default": "redis",
    "Stores": {
      "redis":   { "Driver": "redis",   "ConnectionString": "localhost:6379", "VisibilityTimeoutSeconds": "300" },
      "backup":  { "Driver": "redis",   "ConnectionString": "backup-redis:6379" },
      "file":    { "Driver": "file",    "Path": "storage/queue" },
      "sync":    { "Driver": "memory" }
    }
  }
}
```

کلید `"Driver"` هر store مشخص می‌کند کدام درایور آن را مدیریت می‌کند — **دو store می‌توانند با تنظیمات
متفاوت از یک نوع درایور استفاده کنند** (`redis` و `backup` بالا هر دو Redis هستند ولی به سرورهای متفاوت
وصل می‌شوند)، که دقیقاً همان چیزی است که helper ثبت درایور (`AddQueueDriver`، داخلی در `Naravel.Queue`)
برایش ساخته شده.

## شروع سریع

```csharp
using Naravel.Queue.Jobs;
using Naravel.Queue.Extensions;

public class SendWelcomeEmailJob : Job
{
    public string ToEmail { get; set; } = default!;
    public SendWelcomeEmailJob() { }                       // برای دیسریالایز لازم است
    public SendWelcomeEmailJob(string toEmail) => ToEmail = toEmail;

    public override Task HandleAsync(JobContext context, CancellationToken ct)
    {
        Console.WriteLine($"ارسال به {ToEmail} (تلاش {context.Attempt}/{context.MaxAttempts})");
        return Task.CompletedTask;
    }
}

// Program.cs
builder.Services.AddQueue(builder.Configuration).AddRedisDriver(builder.Configuration);
builder.Services.AddQueueWorker(w => { w.Queues = new[] { "default" }; w.Concurrency = 4; });

var dispatcher = app.Services.GetRequiredService<IJobDispatcher>();
await dispatcher.DispatchAsync(new SendWelcomeEmailJob("ali@example.com"));
```

## ثبت Job

فرآیند dispatch نوع CLR هر job را در همان process ثبت می‌کند. processای که فقط worker اجرا می‌کند باید همهٔ jobهایی را
که دریافت می‌کند ثبت کند. alias پیش‌فرض `Type.FullName` است؛ برای معتبر ماندن payloadهای صف پس از تغییر نام نوع CLR،
alias صریح تعیین کنید:

```csharp
builder.Services.AddJob<SendWelcomeEmailJob>("mail.welcome");
// یا [Job("mail.welcome")] روی نوع job و سپس AddJobsFromAssembly(...).
```

برای استفاده از attribute `[Job]`، namespace `Naravel.Queue.Serialization` را اضافه کنید.

فقط aliasهای ثبت‌شده resolve می‌شوند. نام‌های assembly-qualified در payloadهای قدیمی خودکار resolve نمی‌شوند؛ اگر صف
پیام قدیمی دارد، پیش از ارتقا یک alias سازگاری صریح ثبت کنید. تولید JSON با source generation نیز برای هر job از طریق
`AddJob<TJob>(jsonTypeInfo)` قابل فعال‌سازی است.

## مثال‌ها

### تنظیمات fluent در dispatch

```csharp
await dispatcher.DispatchAsync(new SendWelcomeEmailJob("ali@example.com"), options => options
    .OnConnection("backup")        // انتخاب یک store مشخص با نام
    .OnQueue("emails")             // انتخاب یک صف داخل آن store
    .DelayFor(TimeSpan.FromMinutes(5))
    .WithPriority(9)               // ۰ تا ۹، عدد بالاتر زودتر اجرا می‌شود
    .WithMaxAttempts(5));
```

### تلاش مجدد با backoff

```csharp
public class ChargeCardJob : Job
{
    public override int MaxAttempts => 5;
    public override TimeSpan[] Backoff => new[]
    {
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)
    };

    public override Task HandleAsync(JobContext context, CancellationToken ct) { /* ... */ return Task.CompletedTask; }

    public override Task FailedAsync(JobContext context, Exception exception, CancellationToken ct)
    {
        // فقط یک‌بار، بعد از اتمام آخرین تلاش فراخوانی می‌شود
        return Task.CompletedTask;
    }
}
```

### زنجیره‌سازی (استاتیک و داینامیک)

```csharp
// استاتیک: هر مرحله فقط اگر مرحلهٔ قبل موفق بود اجرا می‌شود.
await dispatcher.Chain(
    new GenerateInvoiceJob(orderId),
    new SendInvoiceEmailJob(orderId)
).DispatchAsync();

// داینامیک: خود جاب تصمیم می‌گیرد بعدش چه چیزی اجرا شود.
public override Task HandleAsync(JobContext context, CancellationToken ct)
{
    context.Then(new SendInvoiceEmailJob(orderId));
    return Task.CompletedTask;
}
```

### دسته‌بندی (Batching)

```csharp
var batch = await dispatcher.BatchAsync(
    jobs: new IJob[] { new SendWelcomeEmailJob("a@x.com"), new SendWelcomeEmailJob("b@x.com") },
    batchConfigure: b => b.OnCompleted = (batch, ct) =>
    {
        Console.WriteLine($"{batch.CompletedJobs} موفق، {batch.FailedJobs} ناموفق");
        return Task.CompletedTask;
    });
```

### `Extend` در زمان اجرا (درایوری که در startup شناخته‌شده نبود)

```csharp
var manager = app.Services.GetRequiredService<QueueManager>();
manager.Extend("test-double", provider => new MyInMemoryTestDriver());
await dispatcher.DispatchAsync(new MyJob(), o => o.OnConnection("test-double"));
```

### چند Worker، تفکیک اولویت

```csharp
builder.Services.AddQueueWorker(w => { w.Connection = "redis"; w.Queues = new[] { "high" }; w.Concurrency = 8; });
builder.Services.AddQueueWorker(w => { w.Connection = "redis"; w.Queues = new[] { "default" }; w.Concurrency = 2; });
```

### راه‌اندازی درایور Database

```csharp
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=queue.db"));
builder.Services.AddDatabaseDriver<AppDbContext>(builder.Configuration);
```

```csharp
public class AppDbContext : DbContext
{
    public DbSet<JobRecord> QueueJobs => Set<JobRecord>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ConfigureQueueJobs();
}
```
سپس یک migration از EF Core بسازید و اعمال کنید.

## بازیابی بعد از کرش

درایورهای Redis، File و Database وقتی worker یک job بیش از `VisibilityTimeoutSeconds` (پیش‌فرض ۳۰۰ —
بالاتر از طولانی‌ترین job خودتان بگذارید) ناپدید شود، آن را به صف برمی‌گردانند. درایور Memory فقط
درون‌پروسه است، پس چیزی برای بازیابی وجود ندارد. Kafka/RabbitMQ به تحویل مجدد خود broker متکی‌اند.

## تضمین تحویل: حداقل یک‌بار (at-least-once)

یک job ممکن است بیش از یک‌بار اجرا شود (کرش worker، تایم‌اوت visibility، شکست در ack)، پس jobها باید
idempotent باشند. ادامهٔ زنجیره قبل از ack شدن job فعلی منتشر می‌شود، پس زنجیره هرگز بی‌صدا گم نمی‌شود —
ولی این یعنی اگر پروسه بین انتشار و ack کردن job اصلی کرش کند، ممکن است ادامهٔ زنجیره دوبار منتشر شود.
بر همین اساس طراحی کنید.

## محدودیت‌ها

- **Kafka**: تلاش مجدد به انتهای topic می‌رود (ترتیب حفظ نمی‌شود)؛ `PopAsync` اولویت حداکثر ۲۵۶ پیام آماده
  در یک batch را رعایت می‌کند، اما پیام‌های رسیده پس از poll را جابه‌جا نمی‌کند؛ job تأخیردار partition را
  تا زمان مقرر مسدود می‌کند؛ `SizeAsync` مقدار -1 برمی‌گرداند.
- **RabbitMQ**: یک channel مشترک به‌ازای هر store؛ پیام با تأخیر طولانی می‌تواند پیام‌های کوتاه‌تر بعد از
  خودش را معطل کند. پیش از ارتقا، ready queueهایی که بدون `x-max-priority` ساخته شده‌اند باید حذف و دوباره
  ساخته شوند.
- **Database**: هر store که از درایور `database` با همان نوع `DbContext` استفاده کند، یک جدول مشترک
  بدون ستون store/connection دارد — اگر نیاز به تفکیک دارید از `DbContext`های متفاوت استفاده کنید.
- **storeهای اضافه‌شده بعد از startup خودکار شناسایی نمی‌شوند.** هر `AddXxxDriver()` فقط یک‌بار، در
  startup، storeهای کانفیگ‌شده را می‌خواند تا تصمیم بگیرد برای کدام نام‌ها factory ثبت کند. تغییر
  تنظیمات یک store *موجود* و reload کردن کانفیگ کار می‌کند (Naravel.Foundation درایور را می‌سازد)؛ اضافه
  کردن یک نام store کاملاً جدید نیاز به ری‌استارت دارد. این دقیقاً مثل لاراول است که آن هم بدون تغییر
  کد/فایل کانفیگ و ری‌استارت اجازهٔ افزودن کانکشن نام‌دار جدید را نمی‌دهد.
- ردیابی Batch به‌صورت پیش‌فرض درون‌حافظه‌ای است؛ برای چند پروسه `IBatchRepository` خودتان را بدهید.

## تست‌ها

`tests/Naravel.Queue.Tests` این‌ها را پوشش می‌دهد:
- `QueueDriverContractTests` — بررسی‌های مشترک و قابل‌استفادهٔ مجدد برای pop فقط یک‌بار، اولویت، delay،
  release، شکست، اندازه، ایزوله‌بودن صف‌ها و مصرف‌کنندگان هم‌زمان؛ روی Memory و File اجرا می‌شود.
- `FileDriverRecoveryTests` — بازگرداندن reservation پس از کرش worker.
- `WorkerTests` — اجرای دقیقاً یک‌بار، retry تا موفقیت، شکست قطعی که `FailedAsync` را دقیقاً یک‌بار صدا
  می‌زند، dispatch تأخیردار، زنجیرهٔ استاتیک و داینامیک (شامل توقف زنجیره در شکست قطعی)، callback خراب
  Batch که نه job را دوباره اجرا می‌کند و نه worker را می‌کشد.
- `ManagerIntegrationTests` — مهاجرت PDR-006: دو store با یک نوع درایور از هم ایزوله می‌مانند، `Extend`
  در زمان اجرا، reload کانفیگ که یک store را تغییر می‌دهد درایورش را می‌سازد، و reload بی‌ربط این کار را
  نمی‌کند.

`tests/Naravel.Queue.Providers.Tests` همین قرارداد را برای Database با SQLite (همیشه اجرا می‌شود)، Redis،
RabbitMQ و Kafka استفاده می‌کند. برای اجرای هر تست زنده، `NARAVEL_TEST_REDIS=host:port`،
`NARAVEL_TEST_RABBITMQ=amqp://user:password@host:5672/%2f` یا `NARAVEL_TEST_KAFKA=host:port` را
تنظیم کنید؛ در غیر این صورت xUnit آن را Skip می‌کند. تست Redis انتقال وضعیت اتمیک مبتنی بر Lua را بررسی
می‌کند و tracker مربوط به offset در Kafka برای ack خارج از ترتیب تست واحد دارد. تست‌های provider cache نیز
از `NARAVEL_TEST_REDIS` و `NARAVEL_TEST_MEMCACHED=host:port` استفاده می‌کنند.

job `services` در CI تست زنده را با containerهای Redis، RabbitMQ، Kafka و Memcached اجرا می‌کند. job سریع،
کل solution را بدون سرویس بیرونی روی Ubuntu، Windows و macOS می‌سازد و تست می‌کند.
