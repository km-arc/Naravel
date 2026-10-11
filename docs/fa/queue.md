# Naravel.Queue

یک صف کار پس‌زمینه با الهام از لاراول برای دات‌نت، مبتنی بر `Naravel.Foundation`
(`Manager<IQueueDriver, QueueOptions>`). معادل لاراول: `illuminate/queue`
(`Queue::push()`, `Queue::connection()`, `php artisan queue:work`).

## شروع سریع

```csharp
builder.Services.AddQueue(builder.Configuration).AddRedisDriver(builder.Configuration);
builder.Services.AddQueueWorker();
await app.Services.GetRequiredService<IJobDispatcher>().DispatchAsync(new MyJob());
```

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

## مشاهده‌پذیری و تست

صف counterهای `naravel.queue.processed`، `naravel.queue.failed` و `naravel.queue.retried` و نیز
histogram با نام `naravel.queue.processing.duration` را از meter با نام `Naravel.Queue` منتشر می‌کند.
`ActivitySource` با همان نام span مربوط به consumer می‌سازد و `traceparent`/`tracestate` از نوع W3C
را از envelope صف بازیابی می‌کند. metricها شناسهٔ job یا مقدار payload را ثبت نمی‌کنند.

در تست برنامه، از `QueueFake` در namespace `Naravel.Queue.Testing` برای بررسی dispatch بدون worker
یا broker استفاده کنید:

```csharp
var queue = new QueueFake();
await queue.DispatchAsync(new SendWelcomeEmailJob("a@x.com"));
queue.AssertDispatched<SendWelcomeEmailJob>();
```

`AssertChained<TJob>()` jobهای زنجیرهٔ dispatchشده را بررسی می‌کند.

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

    repository پیش‌فرض batch درون‌حافظه‌ای است. برای state مشترک میان workerها و ماندگار پس از restart،
    `AddDatabaseBatchRepository<AppDbContext>()` یا `AddRedisBatchRepository("redis")` را ثبت کنید.
    mapping مربوط به `ConfigureQueueJobs()` جدول `QueueBatches` را هم شامل می‌شود. repository پایدار
    delegate ذخیره نمی‌کند؛ handler typed را زیر alias صریح ثبت و فقط payload JSON آن را پایدار کنید:

    ```csharp
    var callbacks = new BatchCallbackRegistry();
    callbacks.Register<string>("mail.batch-finished", (batch, note, ct) => SendSummaryAsync(batch, note, ct));
    builder.Services.AddSingleton(callbacks);

    await dispatcher.BatchAsync(jobs, batchConfigure: options =>
      options.Then("mail.batch-finished", "invoice batch complete").Finally("mail.batch-finished", "finished"));
    ```

    برای batch ناموفق از `Catch(alias, payload)` و برای هر دو نتیجه از `Finally(alias, payload)` استفاده
    کنید. با `AllowFailures = true` jobهای باقی‌مانده پس از شکست دائمی ادامه می‌یابند؛ در غیر این صورت
    batch لغو می‌شود و worker jobهای شروع‌نشده را بدون اجرا ack می‌کند. لغو صریح با
    `IBatchRepository.CancelAsync(batchId, cancellationToken)` همین رفتار را دارد. callbackها هنگام crash
    فرایند تضمین at-least-once دارند و باید idempotent باشند. شکست callback وضعیت آن را pending نگه
    می‌دارد تا startup worker بعدی دوباره آن را اجرا کند.

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

چرخهٔ عمر worker را می‌توان با `StopWhenEmpty`، `MaxJobs` و `MaxRuntime` محدود کرد؛ `Rest` مکث پس از
poll خالی را تعیین می‌کند. شمار `MaxJobs` میان loopهای هم‌زمان یک worker مشترک است و `MaxRuntime`
گرفتن reservation جدید را متوقف می‌کند، بدون اینکه job فعال را لغو کند. ویژگی قدیمی
`SleepWhenEmpty` همچنان alias برای `Rest` است.

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

### Jobهای ناموفق

`AddQueue()` به‌صورت پیش‌فرض store ناموفق‌های درون‌حافظه‌ای ثبت می‌کند. برای ماندگاری، یکی از
`builder.Services.AddDatabaseFailedJobStore<AppDbContext>()` یا
`builder.Services.AddRedisFailedJobStore("redis")` را ثبت کنید. `ConfigureQueueJobs()` جدول
`QueueFailedJobs` را هم map می‌کند؛ پس از افزودن آن migration را بسازید و اعمال کنید. Redis store
از connection انتخاب‌شدهٔ صف و hash پیش‌فرض `netqueue:failed-jobs` استفاده می‌کند.

برای مشاهده و مدیریت شکست‌های نهایی از `FailedJobManager` استفاده کنید:

```csharp
var failed = app.Services.GetRequiredService<FailedJobManager>();
var records = await failed.ListAsync(cancellationToken);
await failed.RetryAsync(records[0].Id, cancellationToken);
```

`RetryAllAsync` تعداد retryهای موفق و شناسه‌های انتشارناموفق را برمی‌گرداند. `ForgetAsync` یک
شکست ذخیره‌شده را حذف می‌کند؛ `FlushAsync` همهٔ شکست‌ها را بدون dispatch پاک می‌کند. retry، envelope
اصلی را با شناسهٔ پیام جدید و شمارش تلاش صفر به connection و queue اصلی می‌فرستد و پس از موفقیت
انتشار، رکورد شکست را حذف می‌کند. تضمین at-least-once است: اگر فرایند پس از انتشار و پیش از حذف رکورد
کرش کند، retry ممکن است تکرار شود. handlerهای job باید idempotent باشند.

## بازیابی بعد از کرش

درایورهای Redis، File و Database وقتی worker یک job بیش از `VisibilityTimeoutSeconds` (پیش‌فرض ۳۰۰ —
بالاتر از طولانی‌ترین job خودتان بگذارید) ناپدید شود، آن را به صف برمی‌گردانند. درایور Memory فقط
درون‌پروسه است، پس چیزی برای بازیابی وجود ندارد. Kafka و RabbitMQ به تحویل مجدد خود broker متکی‌اند که
Naravel آن را نمی‌شمارد؛ بخش [حسابداری تلاش به تفکیک درایور](#حسابداری-تلاش-به-تفکیک-درایور) را ببینید.

### حصار رزرو (Reservation fencing) در Redis، File و Database

هر `PopAsync` به پیام یک `QueuedMessage.ReservationId` تازه می‌زند. متدهای `AckAsync`، `ReleaseAsync` و `FailAsync`
فقط تا زمانی عمل می‌کنند که همان توکن هنوز توکن ذخیره‌شده باشد، و reclaim شدن یک reservation منقضی‌شده آن را پاک
می‌کند. پس workerی که از `VisibilityTimeoutSeconds` بیشتر طول بکشد دیگر نمی‌تواند نسخه‌ای را که اکنون worker دیگری
در اختیار دارد حذف، دوباره صف یا failed کند: ack دیرهنگام او بی‌اثر است و job فقط یک‌بار دیگر اجرا می‌شود
(همان at-least-once قبلی). پیام‌های بدون توکن (envelopeهایی که پیش از این فیلد نوشته شده‌اند) رفتار قبلیِ بدون حصار را
حفظ می‌کنند و Memory، RabbitMQ و Kafka تغییری نکرده‌اند چون reclaim مبتنی بر انقضا در خود درایور ندارند.

ارتقای درایور **Database** یک ستون nullable به نام `ReservationId varchar(64)` به جدول jobs اضافه می‌کند. **پیش از**
استقرار پکیج جدید یک EF migration اعمال کنید (برای جدول پیش‌فرض:
`ALTER TABLE QueueJobs ADD ReservationId varchar(64) NULL`)، وگرنه polling با خطای ستون ناشناخته شکست می‌خورد. حصار یک
job طولانی را ایمن نمی‌کند: اگر از visibility timeout بیشتر طول بکشد باز هم دوبار اجرا می‌شود، پس
`VisibilityTimeoutSeconds` را بالاتر از طولانی‌ترین job نگه دارید.

## تضمین تحویل: حداقل یک‌بار (at-least-once)

یک job ممکن است بیش از یک‌بار اجرا شود (کرش worker، تایم‌اوت visibility، شکست در ack)، پس jobها باید
idempotent باشند. ادامهٔ زنجیره قبل از ack شدن job فعلی منتشر می‌شود، پس زنجیره هرگز بی‌صدا گم نمی‌شود —
ولی این یعنی اگر پروسه بین انتشار و ack کردن job اصلی کرش کند، ممکن است ادامهٔ زنجیره دوبار منتشر شود.
بر همین اساس طراحی کنید.

با reclaim شدن reservation منقضی‌شده در Database، Redis یا File، شمار تلاش ذخیره‌شده افزایش می‌یابد.
وقتی این شمار به `MaxAttempts` برسد، worker شکست نهایی را ثبت می‌کند و job را دوباره اجرا نمی‌کند.
برای محدودیت هر job مقدار `Job.Timeout` را تنظیم کنید؛ worker مقدار کوچک‌تر میان آن و
`QueueWorkerOptions.JobTimeout` را اعمال می‌کند. Cancellation تعاونی است؛ handlerی که token را نادیده
بگیرد ممکن است به اجرا ادامه دهد.

#### اعتبارسنجی visibility timeout هنگام راه‌اندازی

برای driverهایی که reservation منقضی‌شده را reclaim می‌کنند (Database، Redis، File)، worker هنگام شروع
visibility timeout درایور را بررسی می‌کند:

- **خطا:** visibility timeout از `QueueWorkerOptions.JobTimeout` کمتر باشد. در این حالت job هنوز مجاز به
  اجراست اما reclaim می‌شود و بار دوم اجرا می‌گردد؛ پس `StartAsync` یک `InvalidOperationException` پرتاب
  می‌کند (host متوقف می‌شود) و خطا لاگ می‌شود.
- **هشدار:** `QueueWorkerOptions.MaxJobTimeout` تنظیم شده باشد و visibility timeout از
  `MaxJobTimeout + VisibilityMargin` (پیش‌فرض ۱۰ ثانیه) کمتر باشد. `MaxJobTimeout` را برابر بلندترین
  `Job.Timeout` پروژه بگذارید؛ اگر تنظیم نشده باشد چنین هشداری تولید نمی‌شود.
- اگر `Job.Timeout` خودِ یک job از visibility timeout منهای margin بیشتر باشد، هنگام اولین پردازش آن job
  همچنان warning لاگ می‌شود.
- Driverهای بدون visibility timeout (Memory، RabbitMQ، Kafka) اعتبارسنجی نمی‌شوند.

`VisibilityMargin` جایگزین نام قبلی `VisibilityTimeoutMargin` است.

### حسابداری تلاش به تفکیک درایور

`QueuedMessage.Attempts` روی envelope پیام ذخیره می‌شود و به دو شکل تغییر می‌کند:

- **retry آغازشده توسط worker** (job خطا داد): worker یک واحد اضافه می‌کند و پیام را release می‌کند. هر درایور
  باید دقیقاً همین مقدار را به تحویل بعدی بدهد و خود `Release` هرگز واحد دیگری اضافه نمی‌کند. تست‌های contract
  مشترک درایورها این را در retryهای پیاپی بررسی می‌کنند و یک تست worker یک افزایش به ازای هر شکست را بررسی می‌کند.
  وقتی شمار به `MaxAttempts` برسد، worker شکست را در failed-job store ثبت می‌کند و `FailAsync` درایور را فرا می‌خواند.
- **تحویل مجدد پس از کرش یا انقضای reservation** (worker ناپدید شد): فقط درایورهایی می‌شمارند که خودشان
  reservationها را ردیابی می‌کنند.

| درایور | retry آغازشده توسط worker | تحویل مجدد پس از کرش / انقضای reservation |
|---|---|---|
| Memory | شمرده می‌شود | کاربرد ندارد (درون‌پروسه) |
| File، Database، Redis | شمرده می‌شود | با انقضای visibility timeout **شمرده می‌شود** |
| RabbitMQ | شمرده می‌شود (delivery ack می‌شود و پیام تازه‌ای با شمار به‌روز منتشر می‌شود) | **شمرده نمی‌شود** |
| Kafka | شمرده می‌شود (job دوباره به انتهای topic فرستاده و offset اصلی commit می‌شود) | **شمرده نمی‌شود** |

در RabbitMQ و Kafka، پیامی که worker پیش از ack شدنش مرده باشد توسط broker دوباره تحویل می‌شود (deliveryهای
ack‌نشده هنگام بسته شدن channel یا connection به صف برمی‌گردند؛ offset commit‌نشدهٔ Kafka پس از ری‌استارت یا
rebalance دوباره خوانده می‌شود). این تحویل at-least-once است و `Attempts` در Naravel تغییر نمی‌کند. بنابراین jobی که هر
بار کل پروسه را از کار می‌اندازد (مثلاً کرش out-of-memory) روی این دو درایور می‌تواند بی‌پایان دوباره تحویل شود.
با jobهای idempotent، `Job.Timeout`، سقف حافظهٔ پروسه و هشدار روی ری‌استارت‌های مکرر worker یا رشد consumer lag
آن را مهار کنید.

jobهایی که `MaxAttempts` را تمام کنند علاوه بر آن به `netqueue.failed.<queue>` (RabbitMQ) یا topic
`netqueue-<queue>-failed` (Kafka) هم منتشر می‌شوند. Naravel هرگز این‌ها را مصرف نمی‌کند؛ با ابزار broker بررسی‌شان
کنید و برای retry از `FailedJobManager` استفاده کنید.

#### RabbitMQ: سقف تحویل و dead-letter سمت broker

با مستندات [quorum queue](https://www.rabbitmq.com/docs/quorum-queues) و
[dead letter exchange](https://www.rabbitmq.com/docs/dlx) در RabbitMQ در تاریخ ۲۰۲۶-۱۰-۱۱ تطبیق داده شد:

- quorum queueها سقف تحویل دارند: کلید policy با نام `delivery-limit` یا آرگومان صف `x-delivery-limit`.
  مقدار پیش‌فرض از RabbitMQ 4.0 برابر ۲۰ است. پیامی که بیش از این سقف برگردانده شود حذف می‌شود، یا اگر dead letter
  exchange تنظیم شده باشد dead-letter می‌شود. از RabbitMQ 4.3 این سقف تحویل‌های ناموفق را می‌شمارد (مثل کرش client،
  قطع connection و `basic.reject`) و requeue ساده با `basic.nack` را نمی‌شمارد؛ نسخه‌های قبلی هر acquisition را
  می‌شمردند، پس نسخهٔ broker خود را بررسی کنید.
- dead letter exchange با کلید policy به نام `dead-letter-exchange` یا آرگومان صف `x-dead-letter-exchange` تنظیم
  می‌شود؛ آرگومان بر policy غلبه می‌کند. پیام‌ها هنگام reject بدون requeue، انقضای TTL، رسیدن به سقف طول صف و سقف
  تحویل quorum، dead-letter می‌شوند.
- نوع صف هنگام declare تعیین می‌شود (`x-queue-type`) و با policy قابل تنظیم یا تغییر نیست. `x-max-priority` فقط
  برای classic queue است و quorum queue آن را نادیده می‌گیرد؛ اولویت سخت‌گیرانهٔ quorum از RabbitMQ 4.3 وجود دارد.

**محدودیت فعلی:** درایور `netqueue.ready.<queue>` را به‌صورت classic queue با `x-max-priority` declare می‌کند و در هر
عملیات دوباره declare می‌کند. چون نوع صف با policy تغییر نمی‌کند و RabbitMQ اجازهٔ declare دوبارهٔ صف موجود با
آرگومان‌های متفاوت را نمی‌دهد، اپراتور بدون پشتیبانی درایور نمی‌تواند این صف را به quorum تبدیل کند و در نتیجه
نمی‌تواند از `delivery-limit` استفاده کند. نوع صف قابل‌پیکربندی پیاده نشده و تصمیم جداگانه می‌خواهد (AUDIT D-27). تا آن
زمان، تحویل مجدد پس از کرش در RabbitMQ بی‌سقف و شمرده‌نشده است، همان‌طور که بالا آمد. Kafka سقف تحویل مجدد سمت
broker ندارد؛ offset commit‌شده تنها وضعیت است.

## محدودیت‌ها

- **Kafka**: تلاش مجدد به انتهای topic می‌رود (ترتیب حفظ نمی‌شود)؛ `PopAsync` اولویت حداکثر ۲۵۶ پیام آماده
  در یک batch را رعایت می‌کند، اما پیام‌های رسیده پس از poll را جابه‌جا نمی‌کند؛ job تأخیردار partition را
  تا زمان مقرر مسدود می‌کند — و چون هر retry به‌همراه backoff خود (به‌طور پیش‌فرض ۵ تا ۶۰ ثانیه) به‌صورت یک رکورد تأخیردار دوباره
  تولید می‌شود، یک job در حال retry در صف تک‌partition، jobهای بعدی را هم به همان مدت عقب نگه می‌دارد (D-29)؛ `SizeAsync` مقدار -1 برمی‌گرداند. هر صف consumer group مخصوص خود را دارد (`<groupId>-<queue>`)، پس consumer بیکارِ
  یک صف نمی‌تواند صف دیگر را متوقف کند. **نکتهٔ ارتقا (پیش از 1.0):** group idهای جدید از قدیمی‌ترین offset باقی‌مانده شروع
  می‌کنند، بنابراین پیام‌هایی که قبلاً با group id مشترک قدیمی ack شده‌اند ممکن است دوباره پردازش شوند؛ پیش از ارتقا صف‌های Kafka را
  خالی کنید.
  **poll مسدودکننده (D-21):** `PopAsync` متد همگام `IConsumer.Consume` را با timeout دویست میلی‌ثانیه صدا می‌زند؛ بنابراین یک poll
  بیکار تا ۲۰۰ms یک thread از thread-pool را برای هر صف اشغال می‌کند (popهای یک صف سریالی‌اند، پس workerهای هم‌زمان روی یک gate
  async منتظر می‌مانند و thread اضافه مصرف نمی‌شود). این timeout قابل لغو نیست: توقف worker بین pollها، یعنی حدود ۲۰۰ms
  بعد، دیده می‌شود. این یک محدودیت شناخته‌شدهٔ پیاده‌سازی فعلی است؛ حلقهٔ consume ناهمگام یک تغییر جداست که PDR مستقل می‌خواهد و در این نسخه برنامه‌ریزی نشده است.
- **RabbitMQ**: از عملیات async در RabbitMQ.Client 7 و channel جدا برای هر delivery در حال اجرا استفاده
  می‌کند تا acknowledgement روی channel اصلی همان delivery انجام شود. پیام با تأخیر طولانی می‌تواند
  پیام‌های کوتاه‌تر بعد از خودش را معطل کند. پیش از ارتقا، ready queueهایی که بدون `x-max-priority`
  ساخته شده‌اند باید حذف و دوباره ساخته شوند.
- **Database**: هر store که از درایور `database` با همان نوع `DbContext` استفاده کند، یک جدول مشترک
  بدون ستون store/connection دارد — اگر نیاز به تفکیک دارید از `DbContext`های متفاوت استفاده کنید.
- **storeهای اضافه‌شده بعد از startup خودکار شناسایی نمی‌شوند.** هر `AddXxxDriver()` فقط یک‌بار، در
  startup، storeهای کانفیگ‌شده را می‌خواند تا تصمیم بگیرد برای کدام نام‌ها factory ثبت کند. تغییر
  تنظیمات یک store *موجود* و reload کردن کانفیگ کار می‌کند (Naravel.Foundation درایور را می‌سازد)؛ اضافه
  کردن یک نام store کاملاً جدید نیاز به ری‌استارت دارد. این دقیقاً مثل لاراول است که آن هم بدون تغییر
  کد/فایل کانفیگ و ری‌استارت اجازهٔ افزودن کانکشن نام‌دار جدید را نمی‌دهد.
- ردیابی Batch به‌صورت پیش‌فرض درون‌حافظه‌ای است؛ برای state پایدار و چندپروسه‌ای از
  `AddDatabaseBatchRepository<TContext>()` یا `AddRedisBatchRepository()` استفاده کنید.

## تست‌ها

`tests/Naravel.Queue.Tests` این‌ها را پوشش می‌دهد:
- `QueueDriverContractTests` — بررسی‌های مشترک و قابل‌استفادهٔ مجدد برای pop فقط یک‌بار، اولویت، delay،
  release، شکست، اندازه، ایزوله‌بودن صف‌ها و مصرف‌کنندگان هم‌زمان؛ روی Memory و File اجرا می‌شود.
- `FileDriverRecoveryTests` — بازگرداندن reservation پس از کرش worker.
- `WorkerTests` — اجرای دقیقاً یک‌بار، retry تا موفقیت، شکست قطعی که `FailedAsync` را دقیقاً یک‌بار صدا
  می‌زند، dispatch تأخیردار، زنجیرهٔ استاتیک و داینامیک (شامل توقف زنجیره در شکست قطعی)، callback خراب
  Batch که نه job را دوباره اجرا می‌کند و نه worker را می‌کشد، retry job ناموفق، timeout، کنترل worker و
  policy شکست/cancellation مربوط به batch.
- `QueueFakeTests` و `QueueTelemetryTests` — assertionهای dispatch و chain، instrumentهای Meter، histogram
  مدت و بازیابی context ردیابی W3C.
- `ManagerIntegrationTests` — مهاجرت PDR-006: دو store با یک نوع درایور از هم ایزوله می‌مانند، `Extend`
  در زمان اجرا، reload کانفیگ که یک store را تغییر می‌دهد درایورش را می‌سازد، و reload بی‌ربط این کار را
  نمی‌کند.

`tests/Naravel.Queue.Providers.Tests` همین قرارداد را برای Database با SQLite (همیشه اجرا می‌شود)، Redis،
RabbitMQ و Kafka استفاده می‌کند. برای اجرای هر تست زنده، `NARAVEL_TEST_REDIS=host:port`،
`NARAVEL_TEST_RABBITMQ=amqp://user:password@host:5672/%2f` یا `NARAVEL_TEST_KAFKA=host:port` را
تنظیم کنید؛ در غیر این صورت xUnit آن را Skip می‌کند. تست Redis انتقال وضعیت اتمیک مبتنی بر Lua و storeهای
پایدار failed-job و batch را بررسی می‌کند. تست SQLite retry، بازیابی/cancel batch، شمارش reclaim و ذخیرهٔ
trace envelope را پوشش می‌دهد. tracker مربوط به offset در Kafka برای ack خارج از ترتیب تست واحد دارد. تست‌های provider cache نیز
از `NARAVEL_TEST_REDIS` و `NARAVEL_TEST_MEMCACHED=host:port` استفاده می‌کنند.

job `services` در CI تست زنده را با containerهای Redis، RabbitMQ، Kafka و Memcached اجرا می‌کند. job سریع،
کل solution را بدون سرویس بیرونی روی Ubuntu، Windows و macOS می‌سازد و تست می‌کند.
