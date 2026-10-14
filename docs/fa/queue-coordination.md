# Naravel.Queue.Coordination (میان‌افزار jobها برای هماهنگی workerها)

وضعیت: `WithoutOverlapping`، `RateLimited`، `ThrottlesExceptions` و jobهای یکتا (`IUniqueJob`، رد dispatch تکراری) در دسترس‌اند.
سند تصمیم: [PDR-012b](../pdr/fa/PDR-012b-queue-coordination-middleware.md).

## شروع سریع

```csharp
builder.Services.AddNaravelCache(builder.Configuration);   // اگر چند پروسه worker دارند AddNaravelRedisCache(...) هم اضافه کنید
builder.Services.AddQueueCoordination();

public sealed class SyncInvoiceJob : Job, IHasJobMiddleware
{
    public int InvoiceId { get; set; }

    public IEnumerable<IJobMiddleware> Middleware(JobContext context) => new IJobMiddleware[]
    {
        new RateLimited("billing-api", maxAttempts: 60, window: TimeSpan.FromMinutes(1)),
        new WithoutOverlapping($"invoice:{InvoiceId}")
    };
}
```

پکیج `Naravel.Queue.Coordination` را نصب کنید. به `Naravel.Queue` و `Naravel.Cache` وابسته است؛ خود `Naravel.Queue` هرگز به Cache ارجاع
نمی‌دهد، پس کسی که فقط صف می‌خواهد Cache را هم نصب نمی‌کند.

## چرا وجود دارد

قفل‌های .NET و `System.Threading.RateLimiting` فقط داخل یک پروسه کار می‌کنند. صف معمولاً روی چند worker اجرا می‌شود، پس «یک فاکتور را
هم‌زمان دوبار پردازش نکن» یا «از سهمیه‌ی API رد نشو» به ذخیره‌گاهی نیاز دارد که همه‌ی workerها در آن شریک باشند. Naravel از قبل یکی دارد: قفل‌های
Cache و rate limiter مبتنی بر Cache. این middlewareها فقط قاعده‌ی نازک سطح job روی آن‌ها هستند. کلید همیشه متنی است که خودتان انتخاب می‌کنید؛ چیزی از
نام کلاس استخراج نمی‌شود.

## سه middleware (و jobهای یکتا)

| middleware | چه می‌کند | وقتی نمی‌تواند job را اجرا کند |
|---|---|---|
| `WithoutOverlapping(key)` | برای هر کلید هم‌زمان فقط یک job | job را برمی‌گرداند و پس از `ReleaseAfter(...)` (پیش‌فرض ۱۰ ثانیه) دوباره تلاش می‌کند، یا با `DontRelease()` دورش می‌اندازد |
| `RateLimited(limiter, max, window, subject)` | حداکثر `max` job در هر `window` برای آن subject | job را تا پایان پنجره برمی‌گرداند |
| `ThrottlesExceptions(max, window, backoff)` | پس از `max` exception در `window`، اجرای آن نوع job را به مدت `backoff` متوقف می‌کند | job را بدون اجرا برمی‌گرداند |

**Release و Skip.** middlewareای که `next` را صدا نمی‌زند باید بگوید سرنوشت job چیست. *Release* job را پس از یک تأخیر به صفش برمی‌گرداند. این کار یک
attempt را **مصرف نمی‌کند**، زنجیره را اجرا نمی‌کند و موفقیت نمی‌شمارد؛ پس jobی که فقط منتظر نوبتش بود هرگز به‌خاطر انتظار fail نمی‌شود. *Skip* job را
acknowledge می‌کند و دور می‌اندازد. middlewareای که هیچ‌کدام را انجام ندهد باعث fail شدن job با خطای واضح می‌شود؛ پیش از PDR-012b بی‌صدا موفق شمرده می‌شد.

**ترتیب.** middlewareهای هر job داخل `IJobMiddleware`های سراسری و به همان ترتیبی که برمی‌گردانید اجرا می‌شوند. `RateLimited` را پیش از
`WithoutOverlapping` بگذارید: jobی که limiter عقبش می‌اندازد هیچ‌وقت قفل را نگرفته است.

### WithoutOverlapping

```csharp
new WithoutOverlapping($"invoice:{InvoiceId}")
    .ReleaseAfter(TimeSpan.FromSeconds(5))   // یک job مسدودشده چقدر صبر کند
    .ExpireAfter(TimeSpan.FromMinutes(30));  // عمر قفل اگر worker بمیرد؛ بالاتر از طولانی‌ترین اجرای job بگذارید
```

قفل همیشه با پایان job آزاد می‌شود، حتی وقتی job exception بدهد. عمر قفل (`ExpireAfter`، پیش‌فرض ۱۵ دقیقه) فقط در برابر workerی که با قفل در دست مرده محافظت می‌کند.
اگر jobی بیشتر از آن عمر اجرا شود، انحصار را از دست می‌دهد؛ آزادسازی با توکن محافظت می‌شود، پس worker دیرتمام‌کننده نمی‌تواند قفلی را آزاد کند که اکنون مال کس دیگری است.

### RateLimited

```csharp
new RateLimited("billing-api", maxAttempts: 60, window: TimeSpan.FromMinutes(1), subject: accountId)
```

`subject` چیزی است که سهمیه روی آن شمرده می‌شود، مثلاً یک حساب مشتری (پیش‌فرض `"global"`). limiter پنجره‌ی ثابت است، پس نزدیک مرز پنجره می‌تواند تا دو برابر سقف را
عبور دهد (PDR-007a). جایی که این مهم است از آن استفاده نکنید.

### ThrottlesExceptions

```csharp
new ThrottlesExceptions(maxExceptions: 5, window: TimeSpan.FromMinutes(2), backoff: TimeSpan.FromMinutes(1))
```

آن را مثل مدارشکن (circuit breaker) تصور کنید. تا وقتی سرویس پایین‌دستی کار می‌کند چیزی عوض نمی‌شود. وقتی ۵ job در ۲ دقیقه exception بدهند، مدار یک دقیقه باز می‌شود:
jobهای آن نوع به‌جای سوزاندن attemptهایشان روی سرویسی که از کار افتاده، بدون اجرا برگردانده می‌شوند. هر exception همچنان به سیاست retry عادی rethrow می‌شود،
موفقیت شمارنده را صفر می‌کند و خاموش‌شدن worker (`OperationCanceledException`) شمرده نمی‌شود. به‌طور پیش‌فرض همه‌ی jobهای یک نوع یک مدار مشترک دارند؛ برای کلید دیگر `key:` بدهید.

## jobهای یکتا (`IUniqueJob`)

`WithoutOverlapping` اجازه می‌دهد job تکراری وارد صف شود و فقط هنگام اجرا نگهش می‌دارد. **job یکتا** سخت‌گیرانه‌تر است: job تکراری اصلاً وارد صف
نمی‌شود. این معادل `ShouldBeUnique` در Laravel است.

```csharp
public class BuildReportJob : Job, IUniqueJob
{
    public int ReportId { get; set; }

    public string UniqueKey => $"report:{ReportId}";          // «همان job» یعنی چه
    public TimeSpan UniqueFor => TimeSpan.FromMinutes(10);     // فقط شبکه‌ی ایمنی (پیش‌فرض یک ساعت)
    // public bool UniqueUntilProcessing => true;              // اختیاری، پایین توضیح داده شده

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) { /* ... */ }
}

var id = await dispatcher.DispatchAsync(new BuildReportJob { ReportId = 7 });
if (id is null)
{
    // job یکسانی در انتظار یا در حال اجراست: چیزی وارد صف نشد
}
```

**`DispatchAsync` اکنون `Task<string?>` برمی‌گرداند.** `null` یعنی «به‌عنوان تکراری رد شد». برای jobی که یکتا نیست نتیجه هرگز `null` نیست؛ پس کد موجود
جز annotation مربوط به nullable بدون تغییر کار می‌کند.

| وضعیت | سرنوشت کلید |
|---|---|
| job با موفقیت تمام می‌شود | worker آزادش می‌کند |
| job شکست می‌خورد و دوباره تلاش می‌شود | **قفل می‌ماند**، چون تلاش مجدد همان job است |
| job برای همیشه شکست می‌خورد (تلاش‌ها تمام شد) | آزاد می‌شود تا dispatch تازه بتواند دوباره امتحان کند |
| middleware ردش می‌کند (`Skip()`) | آزاد می‌شود |
| middleware به صف برش می‌گرداند (`Release(delay)`) | قفل می‌ماند، چون job هنوز منتظر است |
| `UniqueUntilProcessing => true` | به‌محض **شروع** job آزاد می‌شود تا هنگام اجرا یک job دیگر بتواند وارد صف شود |
| worker می‌میرد | با پایان `UniqueFor` آزاد می‌شود |
| خودِ push به صف شکست می‌خورد | فوراً آزاد می‌شود تا قطعی broker کلید را مسدود نکند |

قواعد و محدودیت‌ها:

- **به `AddQueueCoordination()` نیاز دارد.** خودِ `Naravel.Queue` به Cache وابسته نیست، پس قفل در این بسته است. dispatch کردن یک `IUniqueJob` بدون آن
  **خطا می‌دهد** و پیام خطا `AddQueueCoordination()` را نام می‌برد؛ یکتایی هرگز بی‌صدا نادیده گرفته نمی‌شود.
- **نه در chain و نه در batch.** حلقه‌ی ردشده chain را متوقف می‌کند و jobِ ردشده batch را برای همیشه ناتمام می‌گذارد؛ پس `Chain(...)` و
  `BatchAsync(...)` برای `IUniqueJob` خطای `ArgumentException` می‌دهند. آن را جداگانه dispatch کنید.
- **ذخیره‌گاه Memory = یک پروسه.** با Cache پیش‌فرض (Memory) دو پروسه‌ی worker کلیدهای هم را نمی‌بینند. برای یکتایی میان پروسه‌ها از Redis (یا ذخیره‌گاه
  مشترک دیگر) استفاده کنید.
- کلید متنی است که شما انتخاب می‌کنید، نه نام کلاس. نوع job دیگری با همان کلید «همان job» حساب می‌شود.
- آزادسازی مالک را بررسی می‌کند: workerی که دیر تمام شد و کلیدش منقضی شده و dispatch تازه‌تری آن را گرفته، نمی‌تواند کلید dispatch تازه را آزاد کند.
- گرفتن کلید و ثبت مالک آن دو نوشتن جدا هستند. کرش بین این دو، کلید را تا پایان `UniqueFor` قفل نگه می‌دارد (نه برای همیشه).

### چرا این‌طور ساخته شده

| انتخاب | دلیل |
|---|---|
| dispatch ردشده `null` برمی‌گرداند (تصمیم مالک ۲۰۲۶-۱۰-۱۳) | Laravel هم برای dispatch یکتای ردشده چیزی برنمی‌گرداند؛ استثنا یک اتفاق عادی و مورد انتظار را شبیه خطا می‌کرد و شناسه‌ی ساختگی دربارهٔ jobی که وجود ندارد دروغ می‌گفت. |
| gate یک interface در `Naravel.Queue` است (`IUniqueJobGate`) و در `Naravel.Queue.Coordination` پیاده می‌شود | کاربرانی که فقط از Queue استفاده می‌کنند نباید Cache را بکشند (قاعده‌ی ۳ در AGENTS.md). هسته فقط می‌داند «از gate بپرس»؛ قفل Cache در adapter می‌ماند. |
| مالک قفل **شناسه‌ی پیام** است | workerی که کلید را آزاد می‌کند شناسه‌ی پیام را می‌داند، نه توکن قفل را. نگه‌داشتن توکن در envelope صف برای هر جدول Database ستون جدید می‌خواست. adapter به‌جایش `messageId|token` را کنار قفل نگه می‌دارد؛ پس driver و schema تغییر نمی‌کنند. |
| بدون gate ثبت‌شده خطا می‌دهد | job یکتایی که بی‌صدا تکراری را مجاز می‌کند بدتر از jobی است که در اولین dispatch بلند خطا می‌دهد. |
| `UniqueFor` پیش‌فرض یک ساعت | پیش‌فرض Laravel «تا پایان job» و بدون حد است که یک worker کرش‌کرده را به کلید برای‌همیشه‌مسدود تبدیل می‌کند. حد TTL پیش‌فرض ایمن است؛ برای jobهای طولانی بالاترش ببرید. |

## چه چیزی تضمین می‌شود و چه چیزی نه

| ذخیره‌گاه | هماهنگی کجا برقرار است |
|---|---|
| Memory (پیش‌فرض `AddNaravelCache`) | **فقط داخل یک پروسه**. دو پروسه‌ی worker قفل‌های هم را نمی‌بینند. |
| Redis (`AddNaravelRedisCache`) یا ذخیره‌گاه مشترک دیگر | میان همه‌ی workerهایی که از آن استفاده می‌کنند |

- `AddQueueCoordination()` هنگام شروع، اگر قفل/store از نوع Memory باشد ولی driver صف Memory نباشد، هشدار لاگ می‌دهد؛ چون این ترکیب فعال به نظر می‌رسد ولی میان پروسه‌ها هیچ هماهنگی‌ای نمی‌کند.
- قفل بخشی از تراکنش job نیست. اگر worker بعد از گرفتن قفل کرش کند، قفل تا پایان عمرش می‌ماند.
- همه‌چیز **at-least-once** است. این middlewareها تکرار و فشار را کم می‌کنند، ولی job را دقیقاً یک‌بار اجرا نمی‌کنند؛ پس jobها باید idempotent بمانند.
- `AddQueueCoordination()` هنگام راه‌اندازی بررسی می‌کند قفل و store مربوط به Cache ثبت شده باشند و در غیر این صورت با پیام روشن خطا می‌دهد.
- روی driverی که نمی‌تواند تأخیر بدهد (Kafka، [PDR-012a](../pdr/fa/PDR-012a-kafka-delay-and-retry.md)) jobِ Release‌شده فوراً برمی‌گردد، پس
  `WithoutOverlapping` و `RateLimited` مدام دور خودشان می‌چرخند. برای این middlewareها driverی با تحویل تأخیری استفاده کنید.

## تست

middlewareها قفل و limiter را از service provider خود job می‌خوانند، پس تست‌ها می‌توانند Cache از نوع Memory یا یک fake ثبت کنند. تست‌های مخزن آن‌ها را از طریق
worker واقعی اجرا می‌کنند (`tests/Naravel.Queue.Coordination.Tests`) و وقتی `NARAVEL_TEST_REDIS` تنظیم باشد یک تست Redis با دو worker هم اجرا می‌شود.

برای jobهای یکتا، `Naravel.Queue.Testing.QueueFake` رفتار gate را شبیه‌سازی می‌کند: dispatch دوم همان `UniqueKey` مقدار `null` برمی‌گرداند و ثبت نمی‌شود، و چون fake
هرگز job را اجرا نمی‌کند کلیدها تا `Reset()` قفل می‌مانند. با `AssertNotDispatched<TJob>()` و `AssertDispatchedCount<TJob>(n)` نتیجه را بررسی کنید.
