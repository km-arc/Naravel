# PDR-022 — Query builder پایگاه داده (`Naravel.Database`)

**داوری با اولویت .NET بومی:** .NET از قبل کوئری تایپ‌شده (LINQ با EF Core)، API یکپارچهٔ اتصال (ADO.NET `DbConnection`) و mapper سریع (Dapper) دارد. چیزی که ندارد یک **query builder پویا و رشته‌ای** با سطح API مثل Laravel است (`Table("users").Where("age", ">", 18).OrderBy("name")`) که همان زنجیرهٔ فراخوانی را به MySQL، PostgreSQL، SQL Server و SQLite کامپایل کند. LINQ بدون expression tree نمی‌تواند نام ستون را در زمان اجرا ترکیب کند و رشته‌های SQL خام هم مستعد خطا و ناامن‌اند. پس Naravel **فقط کامپایلر SQL و یک executor نازک** می‌سازد و EF Core را برای entity، change tracking و migration نگه می‌دارد.

وضعیت: **پیشنهادی ۲۰۲۶-۱۰-۱۶، منتظر تأیید مالک.** پیش از تأیید هیچ کدی نوشته نمی‌شود. تصمیم‌های قبلی مالک در گفتگو (۲۰۲۶-۱۰-۱۶): کامپایلر خودمان (نه SqlKata)، بسته‌های فهرست‌شده در زیر در اصل تأیید شده‌اند، اتصال و تراکنش مشترک با EF Core، و تحویل بعد از R09 در مراحل جدا و قابل‌تست.

## زمینه

مالک query builderای خواست که **همهٔ متدهای صفحهٔ query builder در Laravel** را روی MySQL، SQL Server، PostgreSQL و SQLite پشتیبانی کند، با EF Core به‌عنوان پایه برای entityها و Dapper برای کوئری‌های سریع. قوانین مخزن که طراحی را شکل می‌دهند: monorepo (`Naravel.slnx`)، Manager به Driver روی `Naravel.Foundation`، نسخهٔ بسته‌ها به‌صورت مرکزی، PDR پیش از هر قابلیت معادل Laravel، async و `CancellationToken` در همهٔ I/O، بدون reflection روی مسیر داغ (S2)، Meter و ActivitySource و Fake (S3)، تست‌های provider با پرچم محیطی روی کانتینرهای سرویس CI (OD-01) و راستی‌آزمایی صادقانه: در sandbox ایجنت SDK دات‌نت نیست، پس CI تنها راه راستی‌آزمایی است.

سطح Laravel: `Illuminate\Database\Query\Builder`، کلاس‌های `Grammar` به‌ازای هر dialect، دستورهای facade با نام `DB::`، `DB::transaction` و `DB::listen`. مبنای بومی: ADO.NET، `FromSql` و LINQ در EF Core، و Dapper.

## تصمیم

### ۱. شروع سریع (S1: حداکثر ۳ خط کد کاربر)

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);      // Database:Default، Database:Connections:<name>
builder.Services.AddNaravelSqliteDatabase(builder.Configuration); // یا بستهٔ MySql / SqlServer / Postgres

var adults = await db.Table("users").Where("age", ">=", 18).OrderBy("name").Limit(10).GetAsync<UserRow>(ct);
```

`db` همان `IDatabase` تزریق‌شده است. `GetAsync<T>()` با Dapper نگاشت می‌کند؛ `GetAsync()` ردیف‌ها را به‌صورت `IReadOnlyList<IDictionary<string, object?>>` برمی‌گرداند.

### ۲. بسته‌ها و مرزها

| بسته | محتوا | وابستگی‌ها |
|---|---|---|
| `Naravel.Database` | `IDatabase`، `DatabaseManager`، `Query` (builder)، پایهٔ `Grammar` به‌علاوهٔ **چهار grammar برای dialectها** (فقط تولید رشته، بدون driver)، `Raw`، query log و listenerها، `DatabaseFake`، telemetry | Foundation، Dapper، abstractionهای `Microsoft.Extensions.*` |
| `Naravel.Database.Sqlite` | factory اتصال، ثبت driver برای SQLite | Microsoft.Data.Sqlite |
| `Naravel.Database.MySql` | همین برای MySQL | MySqlConnector |
| `Naravel.Database.SqlServer` | همین برای SQL Server | Microsoft.Data.SqlClient |
| `Naravel.Database.Postgres` | همین برای PostgreSQL | Npgsql |
| `Naravel.Database.EntityFrameworkCore` | قرض گرفتن اتصال و تراکنش `DbContext` (R25) | Microsoft.EntityFrameworkCore.Relational |

گذاشتن grammarها در هسته یعنی SQL هر dialect روی هر سیستم‌عامل **بدون پایگاه داده** unit-test می‌شود؛ بسته‌های driver فقط ساخت `DbConnection` را اضافه می‌کنند. Dapper در هسته است چون اجرای کوئری ساخته‌شده و نگاشت ردیف‌ها کار خود builder است؛ تنها وابستگی runtime شخص ثالث هسته است.

وابستگی‌های جدید (تأیید اصولی مالک در ۲۰۲۶-۱۰-۱۶): **Dapper** (Apache-2.0)، **MySqlConnector** (MIT)، **Microsoft.Data.SqlClient** (MIT)، **Npgsql** (مجوز PostgreSQL)، **Microsoft.Data.Sqlite** (MIT). نسخهٔ پایدار فعلی که از `net10.0` پشتیبانی می‌کند، پیش از ویرایش `Directory.Packages.props` در R19.T02 و R20.T01 از متادیتای NuGet راستی‌آزمایی می‌شود. `FluentAssertions` روی 7.x می‌ماند.

### ۳. معماری

- `DatabaseManager : Manager<IDatabaseConnection, DatabaseOptions>` (قانون ۲: حل‌کردن driver را هرگز دوباره‌نویسی نکن). `Database:Connections:<name>:Driver` مقدار `sqlite`، `mysql`، `sqlserver` یا `pgsql` دارد؛ `Extend` در زمان اجرا و بارگذاری دوبارهٔ پیکربندی از کلاس پایه می‌آید.
- `IDatabaseConnection` همان driver است: `Grammar` خودش را می‌شناسد و `DbConnection` باز می‌کند. هرگز خودش SQL نمی‌سازد.
- `Query` یک builder روان و قابل‌تغییر (مثل Laravel) با `Clone()` است. `ToSql()` و `ToRawSql()` grammar را صدا می‌زنند؛ متدهای اجرا (`GetAsync`، `FirstAsync`، `InsertAsync`، ...) یک بار کامپایل می‌کنند، اتصال را باز می‌کنند (یا از تراکنش جاری استفاده می‌کنند)، از طریق Dapper اجرا می‌کنند و telemetry ثبت می‌کنند. SQL کامپایل‌شده با reflection کش نمی‌شود؛ builderهای پویا در هر فراخوانی یک رشته تولید می‌کنند، پس مسیرهای داغ باید یک `Query` ساخته‌شده را با `Clone()` دوباره استفاده کنند یا یک بار `Compile()` بزنند.
- **ایمنی قانون سخت است.** مقادیر **همیشه** به‌صورت پارامتر bind می‌شوند و هرگز به هم چسبانده نمی‌شوند. شناسه‌ها (جدول، ستون، alias) با الگوی محافظه‌کارانه اعتبارسنجی و مطابق dialect quote می‌شوند. هر چیزی که متن SQL است باید در `Raw` پیچیده شود (یا از متدهای `*Raw` استفاده کند) تا مسیر ناامن صریح و قابل grep باشد. Raw SQL پارامتر (binding) می‌پذیرد.
- **تفاوت dialectها صریح است.** فراخوانی‌ای که یک dialect نمی‌تواند درست بیان کند (مثلاً `WhereFullText` در SQLite یا lateral join در SQLite) هنگام کامپایل با `NotSupportedException` و ذکر نام dialect و متد شکست می‌خورد. هیچ چیز بی‌صدا و اشتباه شبیه‌سازی نمی‌شود. ماتریس پشتیبانی بخشی از مستندات است و با تست تأیید می‌شود.
- نتایج: `GetAsync<T>()` از طریق Dapper نگاشت می‌شود (`DefaultTypeMap.MatchNamesWithUnderscores` با `DatabaseOptions` اختیاری است)؛ `GetAsync()` دیکشنری برمی‌گرداند. reflection مخصوص خودمان روی مسیر داغ نیست.
- هر چیزی که I/O دارد `async` با `CancellationToken` است؛ streaming (`Lazy`، `Cursor`، `Chunk`) از `IAsyncEnumerable` استفاده می‌کند.

### ۴. مراحل (هر کدام مستقل قابل‌ساخت، قابل‌تست و قابل‌انتشار)

| مرحله | دامنه | اثبات همیشه‌روشن | اثبات با پرچم محیطی |
|---|---|---|---|
| R19 | هسته، manager، SQLite، `Table`، select، where پایه، order، limit/offset، دستورهای خام، query log، Fake، telemetry | تست کامپایل (۴ grammar برای متدهای پوشش‌داده‌شده)، اجرای SQLite | - |
| R20 | grammarهای MySQL، PostgreSQL و SQL Server روی سرور واقعی، بسته‌های driver، کانتینرهای سرویس CI | تست کامپایل grammar | مجموعهٔ قرارداد `NARAVEL_TEST_MYSQL`، `NARAVEL_TEST_POSTGRES`، `NARAVEL_TEST_SQLSERVER` |
| R21 | خانوادهٔ کامل where، join، grouping، having، کمک‌های ordering، `When`/`Unless` | تست کامپایل برای هر dialect، اجرای SQLite | همان مجموعهٔ قرارداد گسترش‌یافته |
| R22 | aggregate، `Exists`، chunking و streaming | اجرای SQLite | همان |
| R23 | insert، update، upsert، delete، truncate، increment، به‌روزرسانی JSON، returning | تست کامپایل، اجرای SQLite | همان |
| R24 | union، subquery، CTE، قفل، عبارت‌های خام، ابزار debug | تست کامپایل، اجرای SQLite | همان |
| R25 | تراکنش (با تلاش مجدد برای deadlock و savepoint) و همکاری با EF Core | اجرای SQLite | همان |
| R26 | یکپارچگی pagination با نوع‌های R16 | اجرای SQLite | همان |

هر مرحله باید متدهایش را به **هر چهار** grammar اضافه کند (یا `NotSupportedException` را مستند کند) و مجموعهٔ قرارداد مشترک را گسترش دهد، تا هیچ dialectی عقب نماند. فهرست مالکیت متدها در [roadmap/QUERY-BUILDER-COVERAGE.md](../../../roadmap/QUERY-BUILDER-COVERAGE.md) است.

### ۵. CI

job با نام `services` کانتینرهای سرویس MySQL، PostgreSQL و SQL Server می‌گیرد (OD-01: بدون Testcontainers؛ فقط job لینوکس). SQLite و همهٔ تست‌های کامپایل در job سه‌سیستم‌عاملهٔ `fast` اجرا می‌شوند. SQL Server به `ACCEPT_EULA`، گذرواژهٔ قوی و health check نیاز دارد؛ نسخه‌ها در R20.T02 از imageهای پشتیبانی‌شدهٔ فعلی انتخاب می‌شوند.

### ۶. رابطه با EF Core (R25)

`Naravel.Database.EntityFrameworkCore` اجازه می‌دهد یک `DbContext` و query builder **یک اتصال و یک تراکنش** را به اشتراک بگذارند: `db.Using(dbContext)` یا `dbContext.Query("table")` متد `Database.GetDbConnection()` و `IDbContextTransaction` جاری را قرض می‌گیرد. هیچ طرفی تراکنش دیگری را commit نمی‌کند. entityها، migrationها و change tracking در EF Core می‌مانند.

### ۷. مشاهده‌پذیری و fake (S3)

`Meter` و `ActivitySource` با نام `Naravel.Database` با شمارنده‌های کوئری و شکست و هیستوگرام زمان؛ tagها فقط نام اتصال و نوع عملیات‌اند، هرگز متن SQL یا مقدار. `DatabaseFake` (فضای نام `Naravel.Database.Testing`) کوئری‌های کامپایل‌شده را ثبت می‌کند و برای تست‌هایی که پایگاه داده نمی‌خواهند، نتیجهٔ از پیش‌تعیین‌شده برمی‌گرداند. `DB::listen` به `IQueryListener` و یک query log اختیاری در حافظه تبدیل می‌شود.

## خارج از محدوده

مدل‌ها و روابط Eloquent، schema builder، migration (از EF Core استفاده کنید)، seeder، connection pool اختصاصی، cache نتیجهٔ کوئری، و ابزارهای debug مخصوص PHP یعنی `dd`/`dump` (از `ToSql`، `ToRawSql` و query log استفاده کنید).

## گزینه‌های بررسی‌شده

۱. **پیچیدن SqlKata.** شروع سریع‌تر، ولی وابستگی خارجی است که کل سطح Laravel (قفل‌ها، where روی JSON، lateral join، انواع upsert) را پوشش نمی‌دهد و تضمین‌های ایمنی SQL ما را به دست دیگری می‌سپارد. مالک رد کرد.
۲. **ساخت builder روی `IQueryable` در EF Core.** LINQ از قبل کوئری تایپ‌شده می‌دهد؛ ترکیب پویای ستون به کار expression tree نیاز دارد و builder را به مدل entity گره می‌زند. رد شد.
۳. **فقط Dapper، بدون builder.** ترکیب SQL را به چسباندن رشته می‌سپارد، همان مشکلی که این ماژول حل می‌کند.
۴. **یک بسته با همهٔ driverها.** کلاینت SQL Server و Postgres و MySQL را بر هر کاربر تحمیل می‌کند. رد شد؛ driverها بسته‌های جدا هستند.
۵. **شبیه‌سازی قابلیت‌های پشتیبانی‌نشدهٔ dialect.** شبیه‌سازی بی‌صدا خطر نتیجهٔ غلط دارد (مثلاً جست‌وجوی تمام‌متن جعلی). رد شد؛ `NotSupportedException` می‌دهیم.

## راستی‌آزمایی لازم پس از تأیید

در هر مرحله: اول تست رگرسیون؛ تست کامپایل برای هر متد جدید روی هر چهار grammar (SQL و bindingهای مورد انتظار به‌صورت رشته)؛ تست اجرای SQLite؛ تست قرارداد با پرچم محیطی برای هر سرور؛ تست injection (شناسهٔ مخرب رد می‌شود و مقدار مخرب فقط پارامتر است)؛ تست cancellation؛ تست Fake و telemetry؛ مستندات EN/FA؛ ورودی‌های PublicAPI؛ benchmark کامپایل و اجرا در برابر Dapper دست‌نویس در R19 و R22؛ `roadmap/check.py`. هر چه اجرا نشد به‌عنوان NOT RUN گزارش می‌شود.

## تأیید

هنوز تأیید نشده. پرسش‌های باز برای مالک: (الف) آیا Dapper در هستهٔ `Naravel.Database` قابل‌قبول است یا به بستهٔ جدا برود؟ (ب) آیا مرزهای مراحل R19 تا R26 قابل‌قبول است؟ (ج) اگر زمان CI نگران‌کننده شد، آیا نسخهٔ اول فقط SQLite به‌علاوهٔ یک سرور را پوشش دهد و سرور سوم بعداً بیاید؟
