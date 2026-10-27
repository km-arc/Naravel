# زمان‌بندی (Scheduling)

`Naravel.Scheduling` کارهای نام‌دار را طبق یک برنامه داخل میزبان شما اجرا می‌کند. هر کار یک delegate است؛ برنامه می‌گوید کی سررسید است؛ و یک hosted service هر دقیقه یک بار بیدار می‌شود و کارهای سررسیدشده را شروع می‌کند. دو محافظ بین چند نمونه‌ی در حال اجرا کار می‌کنند: `WithoutOverlapping` (تا وقتی اجرای قبلی تمام نشده اجرای تازه شروع نشود) و `OnOneServer` (هر دقیقه‌ی سررسیدشده فقط روی یک نمونه اجرا شود). هر دو از قفل‌های کش `Naravel.Cache` استفاده می‌کنند.

تصمیم و دلایل: [PDR-017](../pdr/fa/PDR-017-console-and-scheduling.md). نسخه‌ی انگلیسی: [docs/en/scheduling.md](../en/scheduling.md).

## شروع سریع

```csharp
builder.Services.AddNaravelCache(builder.Configuration);   // درایور قفل برای محافظ‌ها (برای چند نمونه Redis یا Memcached)
builder.Services.AddNaravelScheduling(schedule =>
{
    schedule.Call("prune-sessions", (sp, ct) => sp.GetRequiredService<SessionPruner>().RunAsync(ct)).EveryFiveMinutes().WithoutOverlapping();
    schedule.Call("daily-digest", (sp, ct) => sp.GetRequiredService<Digest>().SendAsync(ct)).DailyAt(8, 0).OnOneServer();
});
```

همین کافی است: خط cron لازم نیست و دستوری برای اجرا نیست. hosted service همراه میزبان شما شروع و متوقف می‌شود (`dotnet run`، کانتینر، سرویس ویندوز). هر اجرا **دامنه‌ی DI تازه** می‌گیرد: handler یک `IServiceProvider` با دامنه‌ی scoped و `CancellationToken` میزبان دریافت می‌کند.

## استفاده

### نام‌ها

هر کار به نامی یکتا نیاز دارد با حروف ASCII، رقم و `. _ : -` (حداکثر ۱۰۰ نویسه). نام همان کلید قفل و برچسب متریک است، پس باید بین استقرارها ثابت بماند. Laravel نام mutex را از متن دستور می‌سازد که با تغییر کد بی‌صدا عوض می‌شود؛ نام صریح عوض نمی‌شود.

### فرکانس‌ها

کاری که فرکانس ندارد هر دقیقه اجرا می‌شود، مثل Laravel. صدا زدن فرکانس دیگر، فرکانس قبلی را جایگزین می‌کند.

| Laravel | Naravel | cron |
|---|---|---|
| `everyMinute` | `EveryMinute()` | `* * * * *` |
| `everyFiveMinutes` | `EveryFiveMinutes()` | هر ۵ دقیقه |
| `everyTenMinutes`، `everyFifteenMinutes`، `everyThirtyMinutes` | `EveryTenMinutes()`، `EveryFifteenMinutes()`، `EveryThirtyMinutes()` | هر ۱۰، ۱۵، ۳۰ دقیقه |
| (ندارد) | `Every(TimeSpan)` | تعداد صحیح دقیقه، حداقل یک |
| `hourly`، `hourlyAt(15)` | `Hourly()`، `HourlyAt(15)` | `0 * * * *`، `15 * * * *` |
| `daily`، `dailyAt('08:00')` | `Daily()`، `DailyAt(8, 0)` | `0 0 * * *`، `0 8 * * *` |
| `weekly`، `weeklyOn(1, '09:30')` | `Weekly()`، `WeeklyOn(DayOfWeek.Monday, 9, 30)` | یکشنبه ۰۰:۰۰، `30 9 * * 1` |
| `monthly` | `Monthly()` | `0 0 1 * *` |
| `cron('*/20 9-17 * * 1-5')` | `Cron("*/20 9-17 * * 1-5")` | هر عبارت پنج‌فیلدی |

شکل‌های «هر N دقیقه» از epoch یونیکس شمرده می‌شوند، پس همه‌ی نمونه‌ها بدون گفت‌وگو با هم روی یک دقیقه‌ها توافق دارند. `Cron` پنج فیلد می‌گیرد (دقیقه، ساعت، روز ماه، ماه، روز هفته)؛ ثانیه پشتیبانی نمی‌شود و عبارت نادرست هنگام افزودن کار `ArgumentException` می‌دهد، نه ساعت ۳ بامداد. تجزیه را کتابخانه‌ی [Cronos](https://github.com/HangfireIO/Cronos) انجام می‌دهد.

### فیلترها

```csharp
schedule.Call("report", Report).DailyAt(9, 0).Weekdays().InTimeZone(tehran);
schedule.Call("poll", Poll).EveryFiveMinutes().Between(new TimeOnly(8, 0), new TimeOnly(18, 0)).When(() => featureOn);
```

- `InTimeZone(TimeZoneInfo)` فرکانس، `Weekdays` و `Between` را در آن منطقه می‌خواند (پیش‌فرض UTC، **نه** منطقه‌ی محلی ماشین، تا همه‌ی نمونه‌ها یکسان رفتار کنند). تغییر ساعت تابستانی از منطقه پیروی می‌کند.
- `Weekdays()` دوشنبه تا جمعه اجرا می‌کند. `Between(start, end)` هر دو سر را شامل می‌شود؛ اگر شروع بعد از پایان باشد از نیمه‌شب می‌گذرد.
- `When(predicate)` باید درست و `Skip(predicate)` باید نادرست باشد. چند فراخوانی همه اعمال می‌شوند. predicateها ابتدای دقیقه پرسیده می‌شوند، پس سبک نگهشان دارید؛ predicate‌ای که استثنا بدهد آن کار را برای همان دقیقه رد می‌کند و ثبت می‌شود.

### هم‌پوشانی

`WithoutOverlapping()` پیش از اجرا قفل `schedule:overlap:<name>` را می‌گیرد و در `finally` آزاد می‌کند، بعد از شکست یا لغو هم. اگر قفل در دست دیگری باشد، اجرا رد می‌شود (در صف نمی‌ماند). قفل بعد از یک ساعت منقضی می‌شود (`SchedulingOptions.OverlapTtl`) تا کرش کار را برای همیشه نبندد؛ به کار طولانی مقدار خودش را بدهید: `WithoutOverlapping(TimeSpan.FromHours(6))`. پیش‌فرض Laravel ۲۴ ساعت است که برای کاری که هر پنج دقیقه اجرا می‌شود خیلی بلند است.

### یک سرور

`OnOneServer()` قفل `schedule:slot:<name>:<minute>` را می‌گیرد و هرگز آزاد نمی‌کند، پس برای هر دقیقه‌ی سررسیدشده دقیقاً یک نمونه کار را اجرا می‌کند (Laravel هم همین کار را می‌کند). قفل به اندازه‌ی `SchedulingOptions.SlotLockTtl` (پنج دقیقه) می‌ماند تا از دقیقه و اختلاف‌های کوچک ساعت بگذرد.

به درایور قفلی نیاز دارد که همه‌ی نمونه‌ها مشترکاً ببینند (Redis یا Memcached). درایور memory فقط درون فرایند است و هر نمونه کار را اجرا می‌کرد؛ پس میزبان **از راه‌اندازی خودداری می‌کند** و در پیامش نام کار و درایور را می‌آورد. `ICacheLock.IsShared` برای همه‌ی درایورها `true` است جز `MemoryLock`.

### شکست‌ها و قلاب‌ها

استثنا در یک کار ثبت و شمرده می‌شود و به `OnFailure(exception => ...)` می‌رسد؛ هرگز حلقه یا کارهای دیگر را نمی‌ایستاند. `OnSuccess(() => ...)` بعد از اجرایی که استثنا نداد اجرا می‌شود. استثنا درون قلاب ثبت و نادیده گرفته می‌شود. لغو هنگام توقف میزبان شکست نیست: اجرا با نتیجه‌ی `cancelled` تمام می‌شود.

وقتی میزبان متوقف می‌شود، توکن کارهای در حال اجرا را لغو می‌کند و تا مهلت خاموشی میزبان منتظرشان می‌ماند. کاری که توکن را نادیده بگیرد خاموشی را تا همان مهلت عقب می‌اندازد.

### دقیقه‌های از دست‌رفته

اگر میزبان متوقف یا معلق بوده باشد، دقیقه‌های از دست‌رفته **رد می‌شوند، دوباره اجرا نمی‌شوند**، مثل cron. حلقه دقیقه‌ای را به کار می‌برد که ساعت هنگام بیدار شدن نشان می‌دهد، پس بیدار شدن دیر هرگز یک دقیقه را دو بار اجرا نمی‌کند. در این نسخه جبران (catch-up) نیست. کاری که نباید حتی یک اجرا را از دست بدهد باید idempotent باشد و فرکانس کوتاه داشته باشد، یا از طریق `Naravel.Queue` صف شود.

### فهرست کارها

`Scheduler.List()` نام، متن فرکانس و دقیقه‌ی سررسید بعدی هر کار را برمی‌گرداند (Laravel: `schedule:list`). فیلترها ارزیابی نمی‌شوند. یک متد است، پس هر دستور، endpoint یا صفحه‌ی سلامت می‌تواند چاپش کند.

## مثال کامل

سه task با guardهای رایج (یک سرور، بدون هم‌پوشانی، منطقهٔ زمانی، بازهٔ ساعت، پرچم و hook) و یک تست برای قاعدهٔ روزهای کاری. سه سرویس مال خود شما هستند؛ cache مبتنی بر Redis به guardها lockی می‌دهد که همهٔ instanceها مشترکاً می‌بینند.

```csharp
// Program.cs: a web app (or a worker) with three tasks. Digest, Cleanup and FeedPoller are your own services.
var tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

builder.Services.AddNaravelRedisCache(builder.Configuration);        // a lock that every instance shares
builder.Services.AddNaravelScheduling(schedule =>
{
    // 1. Once per day on exactly one instance, on weekdays at 08:00 Tehran time. Failures go to your alert.
    schedule.Call("daily-digest", (sp, ct) => sp.GetRequiredService<Digest>().SendAsync(ct))
        .DailyAt(8, 0).Weekdays().InTimeZone(tehran)
        .OnOneServer()
        .OnSuccess(() => { Console.WriteLine("digest sent"); return Task.CompletedTask; })
        .OnFailure(ex => { Console.Error.WriteLine($"digest failed: {ex.Message}"); return Task.CompletedTask; });

    // 2. Every five minutes, never two runs at once, only inside office hours and only while the flag is on.
    schedule.Call("feed-poll", (sp, ct) => sp.GetRequiredService<FeedPoller>().PollAsync(ct))
        .EveryFiveMinutes()
        .Between(new TimeOnly(8, 0), new TimeOnly(18, 0)).InTimeZone(tehran)
        .When(() => builder.Configuration.GetValue<bool>("Feeds:Enabled"))
        .WithoutOverlapping();

    // 3. Nightly cleanup with a cron expression (03:30 every day).
    schedule.Call("nightly-cleanup", (sp, ct) => sp.GetRequiredService<Cleanup>().RunAsync(ct))
        .Cron("30 3 * * *").OnOneServer();
});
```

```csharp
// Test: no host, no timer, no waiting.
[Fact]
public async Task Digest_runs_on_weekdays_only()
{
    var fake = new SchedulerFake(schedule =>
        schedule.Call("daily-digest", (sp, ct) => Task.CompletedTask).DailyAt(8, 0).Weekdays());

    fake.IsDue("daily-digest", new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero)).Should().BeTrue();    // Monday
    fake.IsDue("daily-digest", new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero)).Should().BeFalse();   // Saturday

    await fake.RunAtAsync(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
    fake.AssertRan("daily-digest");
}
```

## تنظیمات

```csharp
builder.Services.AddNaravelScheduling(configure, options =>
{
    options.OverlapTtl = TimeSpan.FromHours(2);
    options.SlotLockTtl = TimeSpan.FromMinutes(10);
});
```

## مشاهده‌پذیری

- Meter با نام `Naravel.Scheduling`: شمارنده‌ی `naravel.scheduling.task.runs` با برچسب‌های `task` و `outcome`، و هیستوگرام `naravel.scheduling.task.duration` (میلی‌ثانیه).
- `outcome` یکی از `succeeded`، `failed`، `cancelled`، `skipped_overlap`، `skipped_other_server` است. کاری که سررسید نیست یا فیلتر شده شمرده نمی‌شود: این برای هر کار در هر دقیقه یک اندازه‌گیری بی‌فایده اضافه می‌کرد.
- `ActivitySource` با نام `Naravel.Scheduling`: برای هر اجرا یک activity با همان برچسب‌ها.
- برچسب‌ها هرگز پیام استثنا، داده‌ی کار یا توکن قفل ندارند. نام کارها از کد شما می‌آید، پس کران‌دارند.
- لاگ‌ها نام کار و استثنا را دارند؛ هرگز توکن قفل ندارند.

## آزمون

`SchedulerFake` برنامه را در زمان‌هایی که شما انتخاب می‌کنید اجرا می‌کند: بدون میزبان، بدون تایمر، بدون انتظار.

```csharp
var fake = new SchedulerFake(schedule => schedule.Call("daily-digest", (sp, ct) => Task.CompletedTask).DailyAt(8, 0).Weekdays());

fake.IsDue("daily-digest", new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero)).Should().BeTrue();    // دوشنبه
fake.IsDue("daily-digest", new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero)).Should().BeFalse();   // شنبه

await fake.RunAtAsync(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
fake.AssertRan("daily-digest");
```

`Outcomes` هر اجرا را با نتیجه‌اش فهرست می‌کند. برای آزمون محافظ‌ها دو fake روی یک قفل بسازید (`new SchedulerFake(configure, sharedLock)`)؛ بدون قفل، fake از قفل حافظه‌ای خصوصی استفاده می‌کند که خودش را مشترک گزارش می‌دهد. handlerهای زیر fake یک service provider خالی می‌گیرند، پس کاری که به سرویس نیاز دارد بهتر است با کانتینر واقعی آزموده شود: `Scheduler` را resolve کنید و `RunDueAsync(time)` را صدا بزنید.

## پورت‌نشده و دلیلش

- **`command`، `exec`، `job`:** کارهای فرایندی و دستور Artisan در این نسخه نیستند؛ دستورها به‌صورت `System.CommandLine` بومی تصمیم‌گیری شده‌اند (PDR-017 را ببینید) و صف‌کردن یک job یک خط داخل کار با dispatcher صف است.
- **نشانی‌های ping، `runInBackground`، آگاهی از حالت تعمیر، ماندگاری زمان آخرین اجرا، داشبورد:** در backlog ثبت شده‌اند. کارها از قبل در فرایند میزبان هم‌زمان اجرا می‌شوند.
- **قلاب‌های `before` و `after`:** از بدنه‌ی کار استفاده کنید؛ `OnSuccess` و `OnFailure` دو نتیجه را پوشش می‌دهند.
- **جبران دقیقه‌های از دست‌رفته:** بالا را ببینید.

## دستورها (نیمه‌ی Artisan): دستورالعمل، بدون بسته

Artisan در Laravel یک چارچوب دستور است. در .NET نیمه‌ی خط فرمان از قبل با [System.CommandLine](https://learn.microsoft.com/en-us/dotnet/standard/commandline/) حل شده (آرگومان و گزینه‌ی تایپ‌دار، راهنما، تکمیل، actionهای ناهمگام با `CancellationToken`) و Generic Host همان تزریق وابستگی برنامه‌ی وب را می‌دهد. برای همین PDR-017 بسته‌ی `Naravel.Console` و تجزیه‌گر رشته‌ی signature را رد کرده است: مدل دستور دومی می‌شد کنار مدلی که از قبل هست. دستور یک action عادی است که سرویس‌های لازمش را resolve می‌کند.

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddNaravelCache(builder.Configuration);
builder.Services.AddNaravelScheduling(schedule => schedule.Call("daily-digest", (sp, ct) => sp.GetRequiredService<Digest>().SendAsync(ct)).DailyAt(8, 0));
using var host = builder.Build();   // ساخته می‌شود، شروع نمی‌شود: دستور نباید hosted serviceها (حلقه‌ی زمان‌بندی) را شروع کند

Argument<string> user = new("user") { Description = "کاربری که ایمیل می‌گیرد" };
Option<string> queue = new("--queue") { Description = "نام صف" };
Command send = new("mail:send", "ارسال خلاصه به یک کاربر") { user, queue };
send.SetAction(async (parseResult, ct) =>
{
    await using var scope = host.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<Digest>().SendToAsync(parseResult.GetValue(user)!, parseResult.GetValue(queue), ct);
    return 0;
});

Command list = new("schedule:list", "نمایش کارهای زمان‌بندی‌شده");
list.SetAction(_ =>
{
    foreach (var task in host.Services.GetRequiredService<Scheduler>().List()) Console.WriteLine($"{task.Name}  {task.Expression}  next: {task.NextRun:u}");
    return 0;
});

RootCommand root = new("app") { send, list };
return await root.Parse(args).InvokeAsync();
```

| Artisan | .NET |
|---|---|
| `php artisan make:command` | یک متد یا کلاس کوچک با action؛ چیزی برای تولید کردن نیست |
| signature `mail:send {user} {--queue=}` | `new Command("mail:send") { new Argument<string>("user"), new Option<string>("--queue") }` |
| `$this->argument('user')`، `$this->option('queue')` | `parseResult.GetValue(user)`، `parseResult.GetValue(queue)` |
| `$this->call('other')` | مستقیم متد سرویس را صدا بزنید، یا `root.Parse(["other"]).InvokeAsync()` |
| `$this->info()`، `ask`، `confirm`، `table` | `Console`، یا کتابخانه‌ای مثل Spectre.Console اگر پرسش و جدول می‌خواهید |
| `schedule:list` | `Scheduler.List()` (بالا) |
| `schedule:work` | میزبان را اجرا کنید: hosted service همان حلقه است |
| `schedule:run` (cron هر دقیقه صدایش می‌زند) | با hosted service لازم نیست. اگر زمان‌بند پلتفرم باید هر دقیقه یک فرایند شروع کند، میزبان را مثل بالا بسازید و `await scheduler.RunDueAsync(DateTimeOffset.UtcNow, ct)` را صدا بزنید و خارج شوید؛ همان محافظ‌ها اعمال می‌شوند |
| `route:list`، `migrate`، `queue:work` | پرسش‌های جدا (فراداده‌ی مسیر R05، مرحله‌ی پایگاه داده، میزبان کارگر صف)؛ اینجا تصمیم گرفته نمی‌شوند |

API پکیج System.CommandLine بین نسخه‌های preview و 2.0 تغییر کرد؛ این دستورالعمل برای نسخه‌ی 2.x نوشته شده، پس نسخه‌ای را که نصب می‌کنید بررسی کنید. actionهای همگام و ناهمگام را در یک برنامه مخلوط نکنید.

## جایگزین‌ها

Hangfire و Quartz.NET ذخیره‌ی کار، تلاش مجدد و داشبورد یا خوشه‌بندی اضافه می‌کنند؛ وقتی به ماندگاری برنامه‌ها یا تاریخچه‌ی هر اجرا نیاز دارید از آن‌ها استفاده کنید. زمان‌بندهای پلتفرم (cron، `CronJob` در Kubernetes، تایمرهای systemd) وقتی درست‌اند که هر اجرا باید فرایند جدا باشد. این ماژول برای حالت رایجی است که `Schedule` در Laravel پوشش می‌دهد: چند کار درون‌فرایندی با محافظ‌های ساده.
