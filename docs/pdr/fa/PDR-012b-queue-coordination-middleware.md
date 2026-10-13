# PDR-012b — میدل‌ویرهای هماهنگی صف (R08.T01)

**داوری با اولویت .NET بومی:** دات‌نت هیچ ابزار بومی برای هماهنگی در سطح job ندارد: `System.Threading.RateLimiting` فقط درون یک پروسس کار می‌کند، `SemaphoreSlim` و `lock` از مرز پروسس عبور نمی‌کنند و ack بروکر نمی‌گوید کدام jobها اجازهٔ اجرای هم‌زمان دارند. پس هماهنگی بین چند پروسس به یک store مشترک نیاز دارد و Naravel آن را دارد: `ICacheLock` و `IRateLimiter` از PDR-007 و PDR-007a (R04). Naravel فقط باید سیاست نازک سطح job را روی آن‌ها بگذارد، نه موتور قفل یا محدودکنندهٔ دوم.

وضعیت: **تأییدشده ۲۰۲۶-۱۰-۱۳.** تصمیم‌های قبلی مالک در گفتگو (۲۰۲۶-۱۰-۱۳): مرز ماژول یک **پروژهٔ adapter جدا** است و **debounce به `roadmap/BACKLOG.md` منتقل می‌شود** (خارج از R08).

## زمینه

R07 این‌ها را تحویل داد: worker، تلاش مجدد، failed jobs و `IJobMiddleware`. قابلیت‌های هماهنگی Laravel عبارت‌اند از `ShouldBeUnique` / `ShouldBeUniqueUntilProcessing`، `WithoutOverlapping`، `RateLimited` و `ThrottlesExceptions`. R04 اجزای اتمیک را تحویل داد.

واقعیت‌های بررسی‌شده در کد فعلی (`QueueWorkerService.cs`، `IJobMiddleware.cs`، `IJob.cs`):

۱. `IJobMiddleware` به‌صورت **سراسری** در DI ثبت می‌شود و **همهٔ** jobها را در بر می‌گیرد. فهرست middleware برای هر job وجود ندارد.
۲. اگر middleware متد `next` را صدا نزند، worker job را **موفق** می‌شمارد: chain ادامه می‌یابد، `AckAsync` اجرا می‌شود و `Processed` شمرده می‌شود. middleware راهی برای گفتن «این job را بعداً release کن» یا «دورش بینداز» ندارد. بدون اصلاح، `WithoutOverlapping` و `RateLimited` بی‌صدا jobها را گم می‌کنند.
۳. `ICacheLock.AcquireAsync(name, ttl)` توکن مالک یا null برمی‌گرداند و `ReleaseAsync(name, token)` با توکن محافظت می‌شود. `IRateLimiter.AttemptAsync(limiter, subject, max, window)` پنجرهٔ ثابت است و در مرز پنجره ممکن است تا ۲ برابر مجاز کند (PDR-007a).
۴. قفل store حافظه فقط تک‌پروسسی است.

## تصمیم

### ۱. مرز ماژول: `Naravel.Queue.Coordination`

پروژهٔ اختیاری جدید `src/Naravel.Queue.Coordination` که به `Naravel.Queue` و `Naravel.Cache` ارجاع می‌دهد. خود `Naravel.Queue` **هیچ** ارجاعی به Cache پیدا نمی‌کند (قانون ۳ در AGENTS.md: وابستگی‌ها یک‌طرفه‌اند و کاربر فقط-Queue، Cache را نمی‌گیرد). ثبت: `services.AddQueueCoordination()`؛ به یک store قفل/limiter ثبت‌شده در Cache نیاز دارد و اگر نباشد هنگام راه‌اندازی با پیام روشن fail می‌شود. تست‌ها در `tests/Naravel.Queue.Coordination.Tests` و پروژه در `Naravel.slnx` اضافه می‌شود.

### ۲. تغییر کوچک هسته در `Naravel.Queue` (لازم برای همهٔ middlewareها)

- **middleware برای هر job.** اضافه شدن `IHasJobMiddleware` (نام در بازبینی API نهایی می‌شود) با `IEnumerable<IJobMiddleware> Middleware(JobContext context)`. jobی که آن را پیاده کند، این middlewareها را **داخل** middlewareهای سراسری می‌گیرد. نمونه‌ها را کد خود job می‌سازد، پس reflection یا جست‌وجوی type در کار نیست.
- **نتیجهٔ صریح.** اضافه شدن `JobContext.Release(TimeSpan delay)` و `JobContext.Skip()`. اگر middleware یکی را صدا بزند و `next` را نزند، worker آن را اجرا می‌کند: *Release* متد `driver.ReleaseAsync(message, delay)` را **بدون** شمارش attempt صدا می‌زند و chain و telemetry موفقیت ندارد؛ *Skip* پیام را ack می‌کند ولی به‌عنوان `skipped` می‌شمارد (مقدار tag جدید با cardinality پایین) و chain و شمارش `Processed` ندارد. middlewareی که نه `next` را صدا بزند و نه نتیجه‌ای تعیین کند، خطای برنامه‌نویسی است: worker به‌جای موفق شمردن (رفتار فعلی) job را با استثنای روشن fail می‌کند. این برای middlewareهای سفارشی که الان short-circuit می‌کنند تغییر رفتار است و در CHANGELOG به‌عنوان breaking پیش از 1.0 ثبت می‌شود.
- درایورهای بدون تأخیر (Kafka، PDR-012a) بدون تأخیر release می‌کنند؛ هشدار موجود برای قابلیت تأخیر همین را پوشش می‌دهد.

### ۳. معنای middlewareها (همه در `Naravel.Queue.Coordination`)

| Middleware | کلید | رفتار |
|---|---|---|
| `WithoutOverlapping(key)` | `key` (الزامی، از کاربر) | هنگام اجرا: `AcquireAsync(key, lockTtl)`. اگر گرفته نشد: `Release(releaseAfter)` (پیش‌فرض) یا `Skip()` (با `DontRelease`). قفل همیشه با توکن در `finally` آزاد می‌شود، حتی اگر job استثنا بدهد. `lockTtl` پیش‌فرض برابر timeout مؤثر job به‌علاوهٔ margin است تا worker کرش‌کرده قفل را برای همیشه نگه ندارد. |
| `UniqueJob(key, lockTtl)` | `key` | همین قفل، ولی علاوه بر بررسی هنگام اجرا، هنگام **dispatch** هم با hook گرفته می‌شود: dispatch دوم تا وقتی اولی در انتظار یا در حال اجراست **رد** می‌شود (شناسهٔ job موجود یا `null` مستند؛ شکل دقیق برای بازبینی API). حالت پیش‌فرض *تا پایان پردازش* است (قفل بعد از اتمام یا شکست دائمی آزاد می‌شود)؛ `UntilProcessing` قفل را وقتی job شروع شد آزاد می‌کند. TTL شبکهٔ ایمنی است، نه راه اصلی آزادسازی. |
| `RateLimited(limiter, max, window, subject)` | `limiter` + `subject` | `IRateLimiter.AttemptAsync`؛ اگر مجاز نبود `Release(retryAfter)` با retry-after تصمیم. محدودیت پنجرهٔ ثابت PDR-007a را به ارث می‌برد و مستند می‌کند. release ناشی از rate limit یک attempt مصرف **نمی‌کند**. |
| `ThrottlesExceptions(maxExceptions, window, backoff)` | به‌ازای نوع job (پیش‌فرض) یا کلید کاربر | استثناهای پشت‌سرهم را در شمارندهٔ مشترک می‌شمارد. بعد از رسیدن به حد، jobهای بعدی را بدون اجرا برای `backoff` release می‌کند (مدار باز) به‌جای fail کردن؛ موفقیت شمارنده را صفر می‌کند. تا قبل از حد، استثنا برای سیاست تلاش مجدد عادی دوباره پرتاب می‌شود. |

همهٔ کلیدها رشته‌های ارائه‌شده توسط کاربرند و با پیشوند `naravel:queue:` فضای‌نام می‌گیرند. چیزی از نام type در دادهٔ ذخیره‌شده استخراج نمی‌شود (OD-07).

### ۴. تضمین‌های صادقانه

- با قفل یا limiter **Memory**، هماهنگی فقط **درون یک پروسس** برقرار است. مستندات (EN/FA) و مسیر ثبت این را صریح می‌گویند؛ `AddQueueCoordination()` وقتی store حل‌شده Memory است و اتصال صف Memory نیست هشدار می‌دهد.
- با **Redis** (یا store مشترک دیگر) قفل بین پروسس‌ها برقرار است. TTL قفل یک کران است نه تضمین: jobی که بیش از `lockTtl` اجرا شود انحصار متقابل را از دست می‌دهد. آزادسازی با توکن مانع آزاد شدن قفل دیگری توسط پایان‌دهندهٔ دیرهنگام می‌شود.
- گرفتن قفل و اجرای job یک تراکنش نیستند: کرش بعد از acquire قفل را تا TTL نگه می‌دارد. این گفته می‌شود، پنهان نمی‌شود.
- یکتایی هنگام dispatch **بهترین‌تلاش** و هنگام اجرا **محافظت‌شده** است؛ exactly-once نیست.

### ۵. مشاهده‌پذیری و Fake

meter فعلی `Naravel.Queue` مقدار `released`/`skipped` را در tag به نام `outcome` (cardinality پایین؛ بدون شناسهٔ job و کلید) می‌گیرد. Fake در `Naravel.Queue.Testing` برای dispatchهای رد‌شدهٔ unique متد `AssertNotDispatched` می‌گیرد. middlewareها با `ICacheLock` جعلی تست‌پذیرند.

## خارج از محدوده

Debounce (به BACKLOG)، semaphore توزیع‌شده با N مجوز، موتور جدید قفل/limiter، داخل Cache (R04)، توپولوژی Redis (R03)، رمزنگاری (R18).

## گزینه‌های بررسی‌شده

۱. **ارجاع مستقیم Naravel.Queue به Cache.** رد شد (تصمیم مالک): Cache را بر همهٔ کاربران Queue تحمیل می‌کند.
۲. **فقط middleware سراسری.** رد شد: کلید unique/overlap مخصوص هر job است؛ middleware سراسری به جست‌وجوی type یا attribute با reflection نیاز دارد.
۳. **اینکه short-circuit یعنی موفقیت (رفتار فعلی).** رد شد: jobهای release یا rate-limit شده بی‌صدا گم می‌شوند.
۴. **پرتاب استثنای ویژه برای release.** رد شد: استثنا به‌عنوان کنترل جریان روی مسیر داغ allocation دارد و با سیاست retry تداخل می‌کند؛ نتیجهٔ صریح ارزان‌تر و روشن‌تر است.
۵. **قفل اختصاصی داخل Queue.** رد شد: R04 را تکرار می‌کند.
۶. **قول یکتایی بین پروسس‌ها با قفل Memory.** با تصمیم roadmap برای R08 رد شد.

## راستی‌آزمایی لازم پس از تأیید

اول تست رگرسیون برای تغییر هسته: middlewareی که بدون `next` و بدون نتیجه برگردد job را fail می‌کند؛ `Release` attempt نمی‌شمارد و chain ندارد؛ `Skip` بدون `Processed` ack می‌کند. تست واحد هر middleware با قفل Memory (شامل آزاد شدن قفل هنگام استثنای job، انقضای TTL و تأخیر release). یکتایی و عدم‌تداخل **بین پروسس‌ها** با تست Redis دو-worker (`NARAVEL_TEST_REDIS`) در job سرویس‌های CI. ورودی‌های Public API، مستندات EN/FA، جدول parity، CHANGELOG، چک‌لیست stage، پشتیبانی Fake، `roadmap/check.py`؛ هر چه اجرا نشد به‌عنوان NOT RUN گزارش می‌شود.

## تأیید

مالک در ۲۰۲۶-۱۰-۱۳ با این پاسخ‌ها به پرسش‌های باز تأیید کرد:

- **(الف) dispatch رد‌شدهٔ unique مقدار `null` برمی‌گرداند.** `IJobDispatcher.DispatchAsync` به `Task<string?>` تبدیل می‌شود؛ `null` یعنی dispatch به‌دلیل تکراری‌بودن رد شد. این همراه `UniqueJob` پیاده‌سازی می‌شود، نه هنوز.
- **(ب) تغییر رفتار short-circuit به‌عنوان breaking پیش از 1.0 پذیرفته شد.** میدل‌ویری که نه `next` را صدا بزند و نه outcome تنظیم کند، job را شکست می‌دهد. تغییر هستهٔ بخش ۲ پیش‌تر ادغام شده (PR #31) و در `CHANGELOG.md` آمده است.

## سابقهٔ پیاده‌سازی

- ۲۰۲۶-۱۰-۱۳: پروژهٔ `Naravel.Queue.Coordination` با `WithoutOverlapping`، `RateLimited`، `ThrottlesExceptions` و `AddQueueCoordination()`؛ تست‌ها در `tests/Naravel.Queue.Coordination.Tests` (Memory با worker واقعی، و تست دو‌workerهٔ Redis با شرط محیطی)؛ مستندات `docs/*/queue-coordination.md`. تست‌ها به‌صورت محلی **اجرا نشدند** (NOT RUN؛ SDK دات‌نت در دسترس نبود)؛ CI راستی‌آزمایی است.
- انحراف‌های کوچک از بخش ۳: عمر پیش‌فرض قفل ثابت ۱۵ دقیقه است چون میدل‌ویر timeout job را نمی‌بیند (`ExpireAfter` را بالاتر از طولانی‌ترین اجرا بگذارید)؛ `AddQueueCoordination()` اگر Cache نباشد در شروع خطا می‌دهد، ولی هنوز دربارهٔ ترکیب store حافظه‌ای با اتصال صف غیر‌Memory هشدار نمی‌دهد (مستندات این محدودیت را می‌گویند).
- هنوز انجام نشده: `UniqueJob` (نیاز به hook در dispatch و بازگشت `string?`)، `AssertNotDispatched` در fake، تگ `outcome` در meter و جدول parity (R08.T03).
