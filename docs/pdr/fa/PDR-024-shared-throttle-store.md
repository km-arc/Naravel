# PDR-024 — store مشترک برای middleware مسیر `throttle`

**حکم native-first:** `System.Threading.RateLimiting` برای یک process انتخاب درست است و پیش‌فرض می‌ماند. این limiter نمی‌تواند سهمیه را بین processها مشترک کند و هیچ جزء native در .NET هم این کار را نمی‌کند. بخش‌های مشترک را Naravel در Cache از قبل دارد (شمارندهٔ اتمیک با expiry هنگام ساخت، و `IRateLimiter` مبتنی بر Cache، PDR-007a)؛ آنچه کم است اتصال آن‌ها به middleware مسیر است.

وضعیت: **پیشنهادی (پیش‌نویس، 2026-11-01). تأیید نشده و زمان‌بندی نشده.** `NEXT` اکنون R17 است؛ در صورت تأیید، این کار پس از R17 یک stage می‌شود و شناسهٔ آن را مالک تعیین می‌کند. گزینهٔ A و شمارندهٔ هم‌تراز با ساعت، پیشنهاد این پیش‌نویس هستند و همراه با سیاست خطا و breaker هنوز به تأیید صریح مالک نیاز دارند. پیش‌نیاز: ادغام PR شمارهٔ 192 و موفقیت کامل CI آن (AUDIT D-74، ماندگاری lockها در Memory provider).

## زمینه

`throttle` (PDR-009) روی `PartitionedRateLimiter` ساخته شده و برای هر partition یک limiter پنجرهٔ ثابت در `ThrottleLimiterRegistry` و در حافظهٔ process نگه می‌دارد. partition برابر `policy|endpoint|subject` است؛ subject شناسهٔ کاربر احراز‌شده است و برای ناشناس‌ها IP درخواست. PDR-009 store مشترک را به «PDR جداگانه (پس از `Naravel.Cache`)» واگذار کرده بود؛ این همان PDR است.

| وضعیت | امروز چه می‌شود |
|---|---|
| N instance پشت load balancer | هر کدام جدا می‌شمارد: فراخوان تا N برابر سقف در هر پنجره می‌فرستد و از یک سرور 429 و از سرور دیگر 200 می‌گیرد |
| restart، crash، scale-in یا scale-out یک instance | شمارنده‌های آن خالی شروع می‌شوند؛ فراخوان‌ها پنجرهٔ تازه می‌گیرند |
| فراخوان ناشناس پشت proxy بدون forwarded headers | همه IP خود proxy را دارند و یک bucket مشترک می‌شوند (خطای معکوس؛ تنظیم deployment و قابلیت native ASP.NET Core است و در این تصمیم نیست) |

این موضوع در `docs/en|fa/routing.md` («throttle پشت بیش از یک سرور») مستند شده و رفتار فعلی با یک تست ثبت شده است (`Throttle_counts_per_application_instance_and_starts_empty_after_a_restart`). رفتار in-process برای یک instance یا برای کند کردن سوءاستفاده ایرادی ندارد؛ محدودیتِ طراحی است.

چه چیزی آماده است: `IRateLimiter.AttemptAsync(limiter, subjectKey, maxAttempts, window, ct)` مقدارهای `Allowed`، `Remaining` و `RetryAfter` را برمی‌گرداند که برای `X-RateLimit-*` و `Retry-After` کافی است. روی Redis مشترک، یک سهمیه برای همهٔ instanceها می‌شود. مثال login در `cache.md` همین را در handler به کار برده است. هزینه: هر attempt یک lock می‌گیرد، دو کلید می‌خواند، increment می‌کند، در اولین attempt هر پنجره زمان شروع را می‌نویسد و lock را آزاد می‌کند؛ یعنی پنج تا شش فراخوانی store برای هر درخواست. برای throttle بدتر این است که lock همهٔ attemptهای یک subject را پشت‌سرهم می‌کند و تا ۵ ثانیه منتظر می‌ماند: همان فراخوانی که throttle برای مهارش ساخته شده (یک کلاینت با burst) درخواست‌های خودش را روی lock صف می‌کند، پس throttle درست وقتی لازم است بار اضافه می‌کند. این طراحی برای شمارندهٔ صریح «تلاش‌ها» مثل handler لاگین مناسب است؛ برای middleware روی مسیر درخواست شکل نادرستی است.

شکل بهتر از قبل روی قرارداد Cache هست. `ICacheStore.IncrementAsync(key, by, ttl)` در همهٔ providerها اتمیک است و expiry را فقط هنگام ساخت کلید می‌گذارد (Redis: یک فراخوانی Lua؛ Memcached: `add` و سپس `increment`؛ Memory). اگر شمارهٔ پنجره در کلید باشد (`bucket = floor(now / window)`)، پنجرهٔ ثابت فقط به **یک** فراخوانی نیاز دارد، بدون lock و بدون ذخیرهٔ زمان شروع: `Allowed = count <= limit`، `Remaining = limit - count`، `RetryAfter = window - (now mod window)`. این مسیر مخصوص Redis نیست، پس تعویق «مسیر سریع فقط‌-Redis» در PDR-007a را دوباره باز نمی‌کند.

## گزینه‌ها

| | گزینه | Routing به چه وابسته می‌شود | کد کاربر |
|---|---|---|---|
| **A (پیشنهادی)** | Routing یک seam کوچک `IThrottleStore` می‌گیرد. پیاده‌سازی پیش‌فرض همین limiter in-process فعلی است، بدون تغییر. یک adapter کوچک روی شمارندهٔ Cache (`ICacheStore.IncrementAsync`) در بستهٔ جدید (نام موقت `Naravel.Routing.Cache`) می‌آید که به Routing و Cache وابسته است | چیز تازه‌ای | `services.AddNaravelSharedThrottle("redis");` (یک خط کنار `AddNaravelRouting`)؛ policyها و `throttle:login` همان‌طور می‌مانند |
| B | Routing به `Naravel.Cache` ارجاع می‌دهد و گزینهٔ `ThrottleStore` یک store نام‌دار را انتخاب می‌کند | `Naravel.Cache` و از طریق آن Foundation و بستهٔ memory cache | `o.ConfigureThrottleStore("redis")` |
| C | چیزی ساخته نمی‌شود؛ محدودیت‌ها و recipeها مستند می‌مانند (`IRouteMiddleware` خودتان روی `IRateLimiter` یا محدودسازی در proxy/gateway) | چیزی | مثل امروز |

چرا A: Routing فقط به framework reference ASP.NET Core نیاز دارد (PDR-009)، کسی که فقط throttle in-process می‌خواهد Cache را نمی‌کشد، و جهت وابستگی (Routing.Cache ← Routing، Cache) با قانون سخت ۳ می‌خواند. B کد کمتری دارد ولی Cache را به همهٔ کاربران Routing اضافه می‌کند. C هزینه‌ای ندارد و همان وضعیتی است که مستندات می‌گویند؛ هر تیم باید همان middleware ۱۵ خطی را بنویسد، با مدیریت 429 و هدرها.

## تصمیم (در صورت تأیید: گزینهٔ A)

- `IThrottleStore` (عمومی، در Routing): `ValueTask<ThrottleDecision> AttemptAsync(string partitionKey, int permitLimit, TimeSpan window, CancellationToken ct)`. `ThrottleDecision` شامل `Allowed`، `Remaining` و `RetryAfter` است. کلید partition همان رشتهٔ `policy|endpoint|subject` امروز است.
- `ThrottleMiddleware` از store استفاده می‌کند؛ هدرها و پاسخ 429 برای هر دو store بدون تغییر می‌مانند. store درون‌فرایندی `PartitionedRateLimiter` و رفتار فعلی را عیناً نگه می‌دارد؛ تست‌های موجود باید بدون ویرایش بگذرند.
- `Naravel.Routing.Cache` تابع `AddNaravelSharedThrottle(storeName)` را اضافه می‌کند، یعنی یک `IThrottleStore` روی `ICacheStore.IncrementAsync(key, 1, window)` به شکل بالا: کلید `throttle:{SHA-256 کلید partition}:{bucket}`، یک فراخوانی store برای هر درخواست، بدون lock. کلید hash می‌شود چون Memcached برای طول و نویسهٔ کلید محدودیت دارد. وابستگی شخص‌ثالث تازه‌ای ندارد و از `IRateLimiter` استفاده نمی‌کند.
- معنا: پنجرهٔ ثابتِ هم‌تراز با ساعت (`bucket = floor(UtcNow / window)`)، نه «اولین درخواست پنجره را شروع می‌کند» مثل `IRateLimiter`. ساعت instanceها باید در حد معمول NTP نزدیک باشد؛ اختلاف چند میلی‌ثانیه فقط مرز پنجره را همان‌قدر جابه‌جا می‌کند. burst تا ۲ برابر در مرز پنجره که مستند است باقی می‌ماند. sliding window و token bucket در این PDR نیست.
- **تأخیر محدود.** throttle نباید backend کند را برای همه کند کند. هر فراخوانی store timeout کوتاه خودش را دارد (`ThrottleOptions.StoreTimeout`، پیش‌فرض پیشنهادی ۲۵۰ms) که به cancellation token درخواست وصل است؛ timeout یک خطای store حساب می‌شود.
- **سیاست خطا (تصمیم مالک).** وقتی store خطا می‌دهد یا timeout می‌شود، گزینه‌ها: *fail open* (پذیرش بدون هیچ سقف)، *fail closed* (503 با `Retry-After`) و *degrade to local* (برای همان درخواست limiter درون‌فرایندی امروز اعمال شود). پیشنهاد: **degrade to local** به‌عنوان پیش‌فرض و `FailClosed` به‌صورت opt-in صریح برای هر policy برای endpointهای credential. این کار قطع Redis را از انداختن برنامه جلو می‌گیرد و باز هم سوءاستفاده را محدود می‌کند (در سطح هر instance مثل محصول امروز، هرگز نامحدود). Laravel خطا را پرتاب می‌کند که به 500 تبدیل می‌شود؛ این پیشنهاد نیست. قرارداد:
  - `ThrottleFailureMode { DegradeToLocal (پیش‌فرض)، FailClosed، FailOpen }` برای هر policy (`o.ConfigureThrottlePolicy("login", 5, TimeSpan.FromMinutes(1), ThrottleFailureMode.FailClosed)`)؛ `FailOpen` فقط به‌صورت opt-in صریح وجود دارد.
  - **چه چیزی خطاست:** store پرتاب کند یا فراخوانی از `StoreTimeout` بیشتر شود. `OperationCanceledException` وقتی token خود درخواست لغو شده (کلاینت رفته) خطا **نیست**: عبور می‌کند و به breaker دست نمی‌زند.
  - **یک breaker برای هر store** (همهٔ policyها یک backend دارند)، زمان از `TimeProvider`. *Closed*: فراخوانی‌ها به store می‌روند. پس از یک خطا به‌مدت `BreakerPeriod` (پیشنهاد ۵ ثانیه) *Open* می‌شود: store فراخوانی نمی‌شود و هر درخواست با failure mode policy خودش اداره می‌شود. پس از پایان دوره *Half-open* می‌شود: دقیقاً یک درخواست (probe) store را صدا می‌زند و بقیه هنوز مثل Open اداره می‌شوند. probe موفق آن را Closed می‌کند؛ probe ناموفق برای یک دورهٔ دیگر Open می‌کند.
  - **درخواست‌های degraded** از یک limiter درون‌فرایندی هر instance با همان اعداد policy استفاده می‌کنند؛ شمارنده‌هایش جداست و درخواست‌هایی که پذیرفته است پس از بازگشت backend به store بازپخش نمی‌شود. پاسخ‌ها هدرهایی دارند که limiter محلی حساب می‌کند. `FailClosed` با 503 و `Retry-After` برابر زمان باقی‌ماندهٔ حالت Open (دست‌کم ۱ ثانیه) و بدون هدرهای `X-RateLimit-*` پاسخ می‌دهد.
  - هر بار باز شدن breaker یک بار log می‌شود (warning همراه exception) و هر خطا در `naravel.routing.throttle.store_failures` شمرده می‌شود.
- Telemetry: Routing یک شمارندهٔ `naravel.routing.throttle` با dimensionهای policy و outcome (`allowed`، `rejected`، `degraded`) و شمارندهٔ خطای بالا را اضافه می‌کند. dimensionها محدود هستند و هرگز subject نیستند.
- Fake: `CacheFake` (در Naravel.Cache.Testing) از قبل با ساعت قابل‌تزریق جای شمارندهٔ مشترک را می‌گیرد؛ store درون‌فرایندی fake لازم ندارد.

## Memory provider و ماندگاری lockها (پیش‌نیاز؛ در انتظار PR شمارهٔ 192)

- **throttle مشترک هیچ lockی نمی‌گیرد.** adapter برای هر درخواست یک فراخوانی `IncrementAsync` دارد. `MemoryLock` و `IRateLimiter` روی مسیر آن نیستند، پس هیچ چیز اینجا به رفتار lockهای Memory وابسته نیست.
- **نقصی که در بازبینی پیدا شد و اول رفع می‌شود.** Memory provider برای هر کلید یا نام lockی که دیده بود حالت lock نگه می‌داشت و هرگز آزاد نمی‌کرد: `MemoryCacheStore` برای هر کلیدِ `IncrementAsync` یک شیء lock داشت و جدول استاتیک `MemoryLock` حتی پس از release برای هر نام lock یک state نگه می‌داشت. یک کلید برای هر آدرس کلاینت و پنجره (این طراحی) یا یک lock برای هر subject (`IRateLimiter` امروز) حافظه را بی‌حد و با کنترل کلاینت بزرگ می‌کرد. این نقص با شناسهٔ AUDIT D-74 ثبت شده و در PR شمارهٔ 192 در حال رفع است؛ بسته‌شدن آن منوط به ادغام و موفقیت CI است (۲۵۶ gate ثابت بر اساس hash کلید؛ حذف حالت lock پس از release؛ جاروی stateهای منقضی؛ تست برای هر کدام). آن PR **پیش‌نیاز** این stage است: stage پیش از merge آن شروع نمی‌شود. limiter مبتنی بر Cache روی Memory store را هم مستقل از این PDR درست می‌کند.
- **چه چیزی در Memory برای این طراحی می‌ماند.** یک ورودی شمارنده برای هر subject و پنجره، با پنجره به‌عنوان time-to-live. ورودی‌های منقضی را `IMemoryCache` و callback حذف store پاک می‌کنند؛ پس حافظه با تعداد subjectهای متمایزِ دیده‌شده در حدود یک پنجره (به‌اضافهٔ فاصلهٔ scan کش) متناسب است، نه با تاریخچهٔ process.
- **Memory محلی process است.** `AddNaravelSharedThrottle("memory")` در هر process جدا می‌شمارد و چیزی را مشترک نمی‌کند، مثل `MemoryLock` (`IsShared` برابر false). برای توسعه و تست مجاز است و مستندات این را می‌گویند. بررسی در runtime نداریم: `ICacheStore` نمی‌گوید store مشترک است یا نه و اضافه کردن چنین پرچمی به قرارداد عمومی Cache تصمیم جداگانه‌ای است که اینجا گرفته نمی‌شود.

## پرسش‌های باز برای مالک

1. گزینهٔ A، B یا C.
2. سیاست خطا: degrade to local (پیشنهادی)، fail open، fail closed، یا بدون پیش‌فرض (فراخوان باید برای هر policy انتخاب کند)، با قرارداد breaker بالا و پیش‌فرض‌های `StoreTimeout` برابر 250ms و `BreakerPeriod` برابر ۵ ثانیه.
3. اینکه throttle مشترک اجازه دارد پنجرهٔ هم‌تراز با ساعت روی `IncrementAsync` به کار ببرد (پیشنهادی: یک فراخوانی، بدون lock) به‌جای `IRateLimiter` (چند فراخوانی و یک lock). هزینهٔ این پیشنهاد فقط این است که مرز پنجره‌ها از ساعت instanceها پیروی می‌کند.
4. state حالت maintenance هم به همین دلیل process-local است (`docs/en/routing.md`، بخش محدودیت‌ها). اینجا پوشش داده نشده؛ اگر PDR جدا می‌خواهید بگویید.

## اینجا تصمیم گرفته نمی‌شود

الگوریتم‌های sliding window و token bucket، limitهای پویا برای هر کاربر، پرچم maintenance بین instanceها، و هر تغییر در نحوهٔ تشخیص آدرس کلاینت (forwarded headers همچنان native ASP.NET Core است).

## راستی‌آزمایی لازم پس از تأیید

تست‌های Routing از یک test double برای `IThrottleStore` (تعداد فراخوانی را می‌شمارد، می‌تواند پرتاب کند، تا لغو hang کند یا پاسخ دهد) و یک `TimeProvider` دستی استفاده می‌کنند؛ پس هیچ تستی منتظر دورهٔ واقعی breaker نمی‌ماند.

**مدیریت خطا (قرارداد بالا):**

| تست | چه چیزی را ثابت می‌کند |
|---|---|
| `Degrade_to_local_limits_with_the_in_process_limiter_when_the_store_throws` | policy با سقف ۲ در هر پنجره و store پرتاب‌کننده: درخواست ۱ و ۲ پاسخ 200 و درخواست ۳ پاسخ 429 از limiter محلی می‌گیرند؛ هرگز نامحدود نیست |
| `Degrade_to_local_when_the_store_exceeds_the_timeout` | storeای که تا لغو hang می‌کند: درخواست حدود `StoreTimeout` بعد تمام می‌شود نه وقتی store تمام شود؛ سپس سقف محلی اعمال می‌شود |
| `Degraded_responses_keep_the_rate_limit_headers_and_count_the_degraded_outcome` | `X-RateLimit-*` و `Retry-After` از limiter محلی می‌آیند؛ شمارنده `degraded` و شمارندهٔ خطا ۱ ثبت می‌کند |
| `Breaker_opens_after_a_failure_and_skips_the_store_for_the_breaker_period` | پس از یک خطا، N درخواست دیگر در همان دوره صفر فراخوانی store دارند |
| `Breaker_probes_once_after_the_period_and_closes_when_the_probe_succeeds` | ساعت از دوره جلوتر رفته: یک فراخوانی store؛ سپس هر درخواست دوباره از store استفاده می‌کند |
| `Breaker_reopens_for_another_period_when_the_probe_fails` | probe ناموفق: برای یک دورهٔ کامل دیگر فراخوانی store نیست |
| `Only_one_request_probes_a_half_open_breaker` | درخواست‌های هم‌زمان زیاد در پایان دوره: دقیقاً یک فراخوانی store |
| `Fail_closed_policy_answers_503_with_retry_after_and_never_uses_the_local_limiter` | 503، `Retry-After` دست‌کم ۱ ثانیه، بدون `X-RateLimit-*`، limiter محلی دست‌نخورده |
| `Fail_open_policy_admits_without_a_limit_when_the_store_fails` | فقط برای policyای که opt-in کرده است |
| `Failure_mode_is_per_policy_on_one_store` | `login` با fail closed و `api` با degrade to local روی یک store خراب رفتار متفاوت دارند |
| `A_client_abort_is_not_a_store_failure` | token درخواست لغو شده: breaker تغییر نمی‌کند، outcome برابر `degraded` نیست، log نیست |
| `The_breaker_opening_is_logged_once_per_period` | درخواست‌های ناموفق زیاد در یک دوره: یک warning |
| `Recovery_does_not_replay_locally_admitted_requests_into_the_store` | پس از بسته شدن breaker با probe، store از حالت خودش می‌شمارد |

**رفتار store:** store درون‌فرایندی تست‌های فعلی throttle را بدون تغییر می‌گذراند؛ یک contract روی store درون‌فرایندی، روی adapter با Memory store و، در job provider که با env فعال می‌شود، روی Redis اجرا می‌شود. دو host برنامه روی Redis باید یک سهمیه را مشترک بگیرند (تست دو-hostِ همین repository با انتظار معکوس) و hostی که restart شده سهمیه را حفظ کند. هدرها و 429 برای هر دو store یکسان است. تست burst: درخواست‌های هم‌زمان زیاد از یک subject روی Redis نباید پشت lock صف شوند. تست کلید: کلید hash می‌شود و برای کلیدهای partition طولانی و غیرمعمول در چارچوب قواعد کلید Memcached می‌ماند.

**Memory provider:** adapter روی Memory store هیچ `MemoryLock` نمی‌گیرد؛ subjectهای متمایز زیاد در چند پنجرهٔ کوتاه باعث می‌شوند تعداد ورودی‌ها پس از انقضای پنجره‌ها کم شود (تست از پنجرهٔ واقعی چند صد میلی‌ثانیه‌ای استفاده می‌کند چون `MemoryCacheStore` ساعت سیستم را می‌خواند)؛ تست‌های D-74 در PR شمارهٔ 192 باید در CI موفق شوند.

benchmark مسیر درون‌فرایندی (نباید بدتر شود) و مسیر مشترک روی Redis (ادعا یک فراخوانی store برای هر درخواست است) که پیش از merge بازبینی می‌شود. مستندات EN/FA، `CHANGELOG.md` و فایل‌های public API. build، تست و benchmark فقط با CI راستی‌آزمایی می‌شوند.

## جایگزین‌های بررسی‌شده

1. **جایگزین کردن limiter درون‌فرایندی با limiter مشترک.** رد شد: همهٔ کاربران Routing را مجبور به پرداخت round tripهای Redis و وابستگی سخت می‌کند، برای کاری که یک instance در حافظه بهتر انجام می‌دهد.
2. **قابل‌تعویض کردن خود `ThrottleLimiterRegistry` با `Manager<TDriver,TOptions>`.** رد شد: Routing driver-based نیست (PDR-009)؛ یک interface کوچک کافی است.
3. **`PartitionedRateLimiter` سفارشی روی Redis.** رد شد: leaseها و timerهای replenishment در limiter native حالت محلی را فرض می‌کنند؛ قرارداد شمارندهٔ Cache از قبل یک فراخوانی اتمیک با معنای expiry درست می‌دهد و تست provider دارد.
4. **استفادهٔ مستقیم از `IRateLimiter`.** برای مسیر درخواست رد شد: پنج تا شش فراخوانی store و یک lock برای هر subject با انتظار ۵ ثانیه‌ای که همان burstی را صف می‌کند که throttle باید مهار کند. برای شمارندهٔ صریح تلاش‌ها در handler همچنان ابزار درست است (`cache.md`).
