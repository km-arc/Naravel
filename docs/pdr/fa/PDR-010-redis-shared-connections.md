# PDR-010 — اتصال‌های مشترک Redis (`Naravel.Redis`)

**داوری با اولویت .NET بومی:** برای **یک** اتصال Redis، همان .NET ساده کافی است: `services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost"))` دو خط است و `IConnectionMultiplexer` از قبل شیئی thread-safe، بلندمدت و قابل اشتراک است. پوشش Naravel فقط یک API خوب را پنهان می‌کرد. **دلیل داشتن ماژول محدودتر است و از کد خود همین مخزن می‌آید:** اتصال‌های نام‌دار، یک مالک برای dispose، و نبود pool تکراری. **نتیجه‌ی پیشنهادی: پذیرفته، به‌صورت ماژولی نازک که همان `IConnectionMultiplexer` واقعی را می‌دهد و هیچ چیز دیگر.** اگر مالک راه کاملاً بومی را ترجیح دهد، جایگزین پایین `DECLINED` با دستورالعمل است.

وضعیت: **پیشنهادی، منتظر تأیید مالک.**

## زمینه

کد امروز چه می‌کند (در مخزن بررسی شده):

- `Naravel.Cache.Redis` برای هر store یک factory برای store و یک factory برای قفل ثبت می‌کند و **هر factory خودش `ConnectionMultiplexer.Connect` را صدا می‌زند**. پس یک store و قفلش دو اتصال به یک سرور دارند و `RedisCacheStore` و `RedisLock` هرکدام همان را که ساخته‌اند dispose می‌کنند.
- `Naravel.Queue.Redis` برای هر store صف یک بار `ConnectionMultiplexer.Connect` را صدا می‌زند و driver، ذخیره‌ی jobهای شکست‌خورده و مخزن batch همان یک multiplexer را می‌گیرند. `RedisQueueDriver` متد `Dispose` ندارد، پس **وقتی manager آن driver را بازنشسته یا dispose می‌کند، اتصال هرگز بسته نمی‌شود**.
- Cache و Queue نمی‌توانند یک اتصال به همان سرور Redis را به اشتراک بگذارند و ماژول آینده‌ای که Redis بخواهد (ذخیره‌ی session، backplane در SignalR یا قفل توزیع‌شده) pool سوم و چهارم اضافه می‌کرد.
- هر دو ماژول `ConnectionString` را از بخش store خودشان می‌خوانند، پس رشته‌ی همان سرور در چند جای پیکربندی تکرار می‌شود.

آنچه .NET و StackExchange.Redis از قبل می‌دهند: multiplexer برای یک بار ساخته شدن و اشتراک طراحی شده؛ اتصال مجدد را خودش انجام می‌دهد؛ `ConfigurationOptions` (یا رشته‌ی اتصال) همه‌ی گزینه‌ها را دارد (TLS، رمز، `abortConnect`، `defaultDatabase`، timeoutها). `Microsoft.Extensions.Caching.StackExchangeRedis` اتصال مخصوص cache خودش را دارد. در اکوسیستم مدیر اتصال نام‌دار به سبک Laravel نیست.

سمت Laravel: `config/database.php` در `redis.*` اتصال‌های نام‌دار (`default`، `cache`) را تعریف می‌کند و `Redis::connection('cache')` آن‌ها را resolve می‌کند؛ driverهای cache و صف به‌جای تکرار سرور، اتصال را با نام ارجاع می‌دهند.

## شروع سریع پیشنهادی (S1)

```json
{ "Redis": { "Default": "main", "Stores": { "main": { "ConnectionString": "localhost:6379" } } },
  "Cache": { "Stores": { "redis": { "Driver": "redis", "Connection": "main", "Prefix": "app" } } },
  "NaravelQueue": { "Stores": { "jobs": { "Driver": "redis", "Connection": "main" } } } }
```

```csharp
builder.Services.AddNaravelRedis(builder.Configuration);                            // یک multiplexer برای هر اتصال نام‌دار
builder.Services.AddNaravelCache(builder.Configuration).AddNaravelRedisCache(builder.Configuration);
var multiplexer = redis.Connection("main").Multiplexer;                             // redis یک IRedisConnections تزریق‌شده است؛ همان IConnectionMultiplexer واقعی
```

## تصمیم پیشنهادی

1. بسته‌ی جدید `Naravel.Redis` (با ارجاع پروژه‌ای از `Naravel.Cache.Redis` و `Naravel.Queue.Redis` که دیگر مستقیم به StackExchange.Redis ارجاع نمی‌دهند). یک manager روی `Manager<,>` در Foundation اضافه می‌کند (PDR-004، PDR-005): پیکربندی `Redis:Default` و `Redis:Stores:<name>` است و هر store فقط یک `ConnectionString` است (رشته‌ی StackExchange.Redis همه‌ی گزینه‌های client را دارد، پس Naravel نام گزینه‌ای اختراع نمی‌کند).
2. **manager همان `IConnectionMultiplexer` واقعی را در دسترس می‌گذارد** (و `GetDatabase`/subscriber از طریق آن). دستورها را wrap نمی‌کند: ماتریس parity می‌گوید قابلیت‌های مفید client در .NET را پنهان نکنیم.
3. **برای هر اتصال نام‌دار یک multiplexer، در اولین استفاده ساخته و مالکش manager است.** manager همه را هنگام خاموشی میزبان dispose می‌کند. مصرف‌کننده‌ها قرض می‌گیرند: store کش، قفل، driver صف یا هر کد برنامه هرگز multiplexer‌ی را که از manager گرفته dispose نمی‌کند.
4. **Cache و Queue اتصال را با نام ارجاع می‌دهند** با کلید جدید `Connection`. **کلید قدیمی `ConnectionString` هنوز کار می‌کند:** manager یک اتصال ضمنی می‌سازد (و با hash رشته dedupe می‌کند، بی‌آنکه رشته را نگه دارد یا لاگ کند)، پس دو store یا یک store و قفلش که یک رشته داشته باشند حالا یک multiplexer را به اشتراک می‌گذارند و چندتا باز نمی‌کنند. این اتصال دوگانه در `Naravel.Cache.Redis` را بدون تغییر پیکربندی رفع می‌کند.
5. **پرچم مالکیت، بدون شکست سازگاری:** سازنده‌های عمومی `RedisCacheStore` و `RedisLock` رفتارشان را نگه می‌دارند (multiplexer‌ی که گرفته‌اند dispose می‌کنند) و یک پارامتر اختیاری `ownsConnection` می‌گیرند که مسیر مشترک آن را `false` می‌گذارد. `RedisQueueDriver` پرچم مشابهی می‌گیرد؛ وقتی مالک اتصالش باشد حالا dispose می‌کند (نشت را رفع می‌کند).
6. **بارگذاری مجدد طبق PDR-004:** تغییر واقعی رشته‌ی اتصال multiplexer قدیمی را بازنشسته می‌کند و هنگام dispose شدن manager آن را dispose می‌کند (ممکن است درخواستی هنوز از آن استفاده کند)؛ روش fingerprint از ساخت دوباره با ویرایش بی‌ربط جلوگیری می‌کند.
7. **شکست آشکار است:** نام اتصال ناشناخته با نام و فهرست نام‌های موجود استثنا می‌دهد؛ `ConnectionString` گم‌شده با نام store استثنا می‌دهد؛ خطاهای اتصال همان استثناهای خود client هستند و wrap نمی‌شوند.
8. **S3:** `ActivitySource`/`Meter` اضافه نمی‌شود (client شمارنده‌های خودش را دارد)؛ درز آزمون یک delegate (`Func<string, IConnectionMultiplexer>`) است تا آزمون‌ها تعداد ساخت را بشمارند و بدون سرور اتصال را جایگزین کنند. fake برای پروتکل Redis نوشته نمی‌شود: رفتاری (اسکریپت، انقضا) را جعل می‌کرد که سرور واقعی تعریف می‌کند. آزمون‌های provider به‌صورت env-gated می‌مانند (OD-01).
9. **خارج از دامنه:** ارتقای StackExchange.Redis 3.x (تأیید جدا می‌خواهد)، اسکریپت‌های Lua (همان‌جا که R01 گذاشت می‌مانند)، قابلیت‌های Redis Cluster/Sentinel بیش از آنچه رشته‌ی اتصال از قبل اجازه می‌دهد.

## گزینه‌های بررسی‌شده

1. **رد کردن: `IConnectionMultiplexer` را یک بار در DI ثبت کنید و مستند کنید.** پاسخ بومی صادقانه برای یک سرور. اتصال را نام‌گذاری نمی‌کند، اتصال‌هایی را که Cache و Queue امروز باز می‌کنند dedupe نمی‌کند، نشت Queue را رفع نمی‌کند و رشته‌ی یکسان را در چند بخش پیکربندی نگه می‌دارد. اگر مالک این را ترجیح دهد دستورالعمل و `DECLINED`؛ آن‌وقت دو نقص بالا داخل هر ماژول رفع می‌شود.
2. **wrap کردن دستورهای Redis (سبک `Redis::get()`).** رد شد: client را تکرار و قابلیت‌ها را پنهان می‌کند.
3. **یک pool برای هر store، مثل حالا.** رد شد: همان مشکلی است که این PDR حل می‌کند.

## تصمیم‌ها برای مالک

- **D1:** پذیرش `Naravel.Redis` نازک (توصیه‌شده)، یا `DECLINED` با دستورالعمل DI (گزینه‌ی ۱).
- **D2:** کلید `Connection` به‌علاوه‌ی نگه داشتن `ConnectionString` با dedupe (توصیه‌شده)، یا فقط `Connection` (شکست سازگاری که در changelog می‌آید و OD-09 قبل از 1.0 اجازه می‌دهد).
- **D3:** manager مالک است و dispose می‌کند، مصرف‌کننده‌ها قرض می‌گیرند، و پرچم `ownsConnection` سازنده‌های قدیمی را مثل قبل نگه می‌دارد (توصیه‌شده).
- **D4:** بدون fake پروتکل Redis؛ درز factory برای آزمون (توصیه‌شده).

## قراردادها و کیفیت (در صورت پذیرش)

رشته‌ی اتصال و رمز هرگز در لاگ، پیام استثنا، برچسب متریک یا نام نمی‌آیند (اتصال‌های ضمنی با hash نام می‌گیرند). هر کار اتصال که client ناهمگام بدهد ناهمگام است، با `CancellationToken`. آزمون‌ها: آزمون `Extend` و بارگذاری مجدد؛ آزمونی که ساخت multiplexer را می‌شمارد (یکی برای هر نام، یکی برای رشته‌ی یکسان که store و قفل هر دو به کار می‌برند)؛ driver صف اتصال تحت مالکیتش را dispose می‌کند؛ اتصال قرض‌گرفته را store dispose نمی‌کند؛ آزمون‌های provider مربوط به R01 روی Redis واقعی در مجموعه‌ی env-gated بدون تغییر پاس می‌شوند.

## تأیید

هنوز تأیید نشده. پس از تأیید، مرحله‌ی ماژول، مرحله‌ی مهاجرت و مرحله‌ی اعتبارسنجی اجرا می‌شوند.
