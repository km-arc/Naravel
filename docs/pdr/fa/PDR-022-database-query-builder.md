# PDR-022 — Query builder پایگاه داده (`Naravel.Database`)

**داوری با اولویت .NET بومی:** .NET از قبل کوئری تایپ‌شده (LINQ با EF Core)، API یکپارچهٔ اتصال (ADO.NET `DbConnection`) و mapper سریع (Dapper) دارد. چیزی که ندارد یک **query builder پویا و رشته‌ای** با سطح API مثل Laravel است (`Table("users").Where("age", ">", 18).OrderBy("name")`) که همان زنجیرهٔ فراخوانی را به MySQL، PostgreSQL، SQL Server و SQLite کامپایل کند. LINQ بدون expression tree نمی‌تواند نام ستون را در زمان اجرا ترکیب کند و رشته‌های SQL خام هم مستعد خطا و ناامن‌اند. پس Naravel **فقط کامپایلر SQL و یک executor نازک** می‌سازد و EF Core را برای entity، change tracking و migration نگه می‌دارد.

وضعیت: **تأییدشده ۲۰۲۶-۱۰-۱۶.** تصمیم‌های قبلی مالک در گفتگو (۲۰۲۶-۱۰-۱۶): کامپایلر خودمان (نه SqlKata)، بسته‌های فهرست‌شده در زیر در اصل تأیید شده‌اند، اتصال و تراکنش مشترک با EF Core، و تحویل بعد از R09 در مراحل جدا و قابل‌تست.

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

> با اصلاحیهٔ الف۶ پایین اصلاح شده است: containerهای سرور در یک job جدا و موازی به نام `database` اجرا می‌شوند، نه داخل `services`.

job با نام `services` کانتینرهای سرویس MySQL، PostgreSQL و SQL Server می‌گیرد (OD-01: بدون Testcontainers؛ فقط job لینوکس). SQLite و همهٔ تست‌های کامپایل در job سه‌سیستم‌عاملهٔ `fast` اجرا می‌شوند. SQL Server به `ACCEPT_EULA`، گذرواژهٔ قوی و health check نیاز دارد؛ نسخه‌ها در R20.T02 از imageهای پشتیبانی‌شدهٔ فعلی انتخاب می‌شوند.

### ۶. رابطه با EF Core (R25)

`Naravel.Database.EntityFrameworkCore` اجازه می‌دهد یک `DbContext` و query builder **یک اتصال و یک تراکنش** را به اشتراک بگذارند: `db.Using(dbContext)` یا `dbContext.Query("table")` متد `Database.GetDbConnection()` و `IDbContextTransaction` جاری را قرض می‌گیرد. هیچ طرفی تراکنش دیگری را commit نمی‌کند. entityها، migrationها و change tracking در EF Core می‌مانند.

### ۷. مشاهده‌پذیری و fake (S3)

`Meter` و `ActivitySource` با نام `Naravel.Database` با شمارنده‌های کوئری و شکست و هیستوگرام زمان؛ tagها فقط نام اتصال و نوع عملیات‌اند، هرگز متن SQL یا مقدار. `DatabaseFake` (فضای نام `Naravel.Database.Testing`) کوئری‌های کامپایل‌شده را ثبت می‌کند و برای تست‌هایی که پایگاه داده نمی‌خواهند، نتیجهٔ از پیش‌تعیین‌شده برمی‌گرداند. `DB::listen` به `IQueryListener` و یک query log اختیاری در حافظه تبدیل می‌شود.

## اصلاحیه ۲۰۲۶-۱۰-۱۷ (درخواست مالک، بازبینی معماری)

بازبینی معماری این PDR شش ریسک پیدا کرد. مالک خواست پیش از شروع R19 این‌ها در طراحی نوشته شوند. این موارد **چگونگی** ساخت مراحل را عوض می‌کنند، نه دامنهٔ تأییدشده را. هر مورد مرحله‌ای را نام می‌برد که باید آن را ثابت کند.

### الف۱. seam اتصال و تراکنش در R19 (ریسک: تغییر breaking دیرهنگام)

R25 تراکنش و همکاری با EF Core را می‌آورد، ولی اگر R19 executor را حول «اتصال را باز کن، اجرا کن، ببند» بسازد، R25 باید امضاهای عمومی را عوض کند. پس R19 همین حالا seam را تعریف می‌کند و فقط نیمهٔ ساده را پیاده می‌کند:

- executor هرگز خودش اتصال باز نمی‌کند. از یک `IConnectionScope` یک **lease** می‌گیرد: `IConnectionLease { DbConnection Connection; DbTransaction? Transaction; bool OwnsConnection }`.
- R19 یک scope می‌دهد: اتصال pool‌شدهٔ تازه می‌دهد، `Transaction` برابر `null` است، `OwnsConnection` درست است و dispose کردن lease اتصال را می‌بندد.
- R25 دو scope دیگر اضافه می‌کند بی‌آنکه به `Query` یا امضای اجرا دست بزند: scope تراکنش ambient (همهٔ queryهای داخل callback مربوط به `TransactionAsync` یک lease مشترک دارند) و scope مربوط به EF Core (اتصال و تراکنش قرض‌گرفته، `OwnsConnection` نادرست، هرگز توسط ما بسته یا commit نمی‌شود).
- هر command که executor می‌سازد `command.Transaction` را از lease می‌گیرد. lease قرض‌گرفته هرگز dispose نمی‌شود.
- **اثبات در R19.T05:** یک scope آزمایشی نشان می‌دهد executor مقدار `command.Transaction` را می‌گذارد و اتصالی را که مالکش نیست نمی‌بندد. `IConnectionScope` و `IConnectionLease` از R19 در PublicAPI هستند.

### الف۲. واقعیت‌ها و محدودیت‌های Dapper (ریسک: وضعیت سراسری، AOT، نوع‌های provider)

Dapper در `Naravel.Database` می‌ماند (تصمیم مالک، بدون تغییر). طراحی محدودیت‌های شناخته‌شدهٔ آن را مهار می‌کند و پنهانشان نمی‌کند:

- **وضعیت سراسری.** type handlerهای `SqlMapper` و نقشهٔ نوع پیش‌فرض Dapper در سطح کل پروسه‌اند. Naravel خودش هیچ handler سراسری ثبت نمی‌کند. اگر نوعی به handler نیاز داشت (در R19.T02 روی نسخهٔ pin‌شده بررسی می‌شود، مثلاً `DateOnly` و `TimeOnly`)، به‌صورت اختیاری با `DatabaseOptions.RegisterDapperTypeHandlers` عرضه می‌شود و مستندات می‌گویند سراسری است.
- **Trim و AOT.** Dapper هنگام اجرا IL تولید می‌کند، پس `GetAsync<T>()` با AOT و trim **سازگار نیست** و بسته با `IsAotCompatible` علامت نمی‌خورد. مسیر دیکشنری `GetAsync()` از Dapper استفاده نمی‌کند و مستقیم `DbDataReader` را می‌خواند که سریع‌تر و سازگار با trim است. این قاعدهٔ «بدون reflection روی مسیر داغ» (S2) را صادقانه نگه می‌دارد.
- **نوع پارامترها.** مقدارها به‌صورت `DbParameter` با `DbType` صریح برای نوع‌هایی که بین providerها فرق دارند (`bool`، `decimal`، `Guid`، `DateTime`، `DateTimeOffset`، `DateOnly`، `TimeOnly`، `byte[]`، enum) bind می‌شوند. Dapper برای ساخت ردیف‌ها استفاده می‌شود، نه برای حدس زدن نوع پارامتر.
- **اثبات در R20.T04:** تست رفت‌وبرگشت نوع (نوشتن و خواندن: null، bool، long، decimal، Guid، انواع `DateTime`، `DateTimeOffset`، `DateOnly`، `TimeOnly`، `byte[]`) روی SQLite و هر سرور، با مقایسهٔ یکسان‌بودن مقدارها.

### الف۳. گرامر شناسه و عملگر، قفل‌شده در R19 (ریسک: شل‌شدن قاعدهٔ امنیتی در مراحل بعد)

«الگوی محافظه‌کارانه» اکنون دقیق است، پس R21 نمی‌تواند آن را برای join یا JSON شل کند:

- **قطعه:** `^[\p{L}_][\p{L}\p{N}_$]*$`، حداکثر ۱۲۸ نویسه. حروف Unicode مجازند، پس نام ستون فارسی کار می‌کند.
- **شکل‌های پذیرفته:** `col`، `table.col`، `schema.table.col` (حداکثر سه قطعه)؛ `*` و `table.*` فقط در فهرست select؛ `source as alias` یا `source AS alias` که alias یک قطعه است؛ مسیر ستون JSON به شکل `col->key->sub` یا `col->list[0]` که قطعه‌های مسیرش با `^[\p{L}\p{N}_]+(\[\d+\])?$` می‌خوانند.
- **مسیر JSON به‌صورت پارامتر bind می‌شود** هرجا dialect اجازه بدهد (`JSON_EXTRACT(col, ?)`، `JSON_VALUE(col, @p)`، `json_extract(col, ?)`، پارامتر آرایه‌ای برای `#>` در PostgreSQL)، پس مسیر هرگز نقطهٔ تزریق نمی‌شود.
- **عملگرها** از فهرست مجاز هر متد می‌آیند (`=`، `<`، `>`، `<=`، `>=`، `<>`، `!=`، `like`، `not like` و موارد مخصوص dialect) و هرگز از متن caller الحاق نمی‌شوند.
- **quote کردن** مخصوص هر dialect نویسهٔ بستن را دوبل می‌کند (`"`، backtick، `]`).
- هر چیز دیگر **همان لحظهٔ فراخوانی** `ArgumentException` می‌دهد، نه هنگام اجرای query. جدول‌های ورودی مخرب برای هر dialect در R19.T04 نوشته می‌شود. مرحلهٔ بعدی می‌تواند caller جدید برای این گرامر اضافه کند، ولی **شل کردن آن اصلاحیهٔ PDR می‌خواهد**.

### الف۴. محدودیت تعداد پارامتر (ریسک: شکست فقط در تولید)

هر grammar مقدار `MaxParameters` را نشان می‌دهد: SQL Server برابر ۲۱۰۰ (از ۲۰۹۸ استفاده شود)، SQLite برابر ۳۲۷۶۶، MySQL برابر ۶۵۵۳۵، PostgreSQL برابر ۶۵۵۳۵ (مقدارها در R20.T01 با درایورهای pin‌شده تأیید می‌شوند).

- `WhereIn` و `WhereNotIn` بالاتر از حد، به `(col IN (...) OR col IN (...))` و `(col NOT IN (...) AND col NOT IN (...))` **شکسته** می‌شوند. معنی حفظ می‌شود و شکسته‌شدن در `ToSql()` دیده می‌شود.
- هر دستور دیگر بالاتر از حد `InvalidOperationException` می‌دهد که نام dialect، حد و تعداد را می‌گوید. درج دسته‌ای (R23) داخل یک تراکنش (خودی یا ambient) به چند دستور شکسته می‌شود، پس دسته اتمی می‌ماند.
- **اثبات در R19.T04:** تست‌های grammar با `limit - 1` و `limit` و `limit + 1` مقدار برای هر dialect (رشتهٔ خالص، بدون پایگاه داده) و یک تست اجرای SQLite با ۴۰٬۰۰۰ مقدار.

### الف۵. R16 پیش از R26 (ریسک: ترتیب)

R26 به نوع‌های pagination در R16 نیاز دارد. **Default order** در `ROADMAP.md` از قبل R16 را پیش از R26 گذاشته و R26 در وابستگی‌هایش R16 را دارد. R26 نباید زودتر شروع شود، حتی اگر وابستگی‌های دیگرش تمام شده باشد.

### الف۶. CI هم‌زمان تست‌دار و سریع (ریسک: طول job)

هدف پوشش هر چهار dialect بدون کند شدن هر push است. چهار اقدام:

1. **لایهٔ ۱، هر push و PR، سه سیستم‌عامل (job با نام `fast`):** همهٔ تست‌های compile گرامر، تست‌های تزریق، تست‌های `MaxParameters`، اجرای SQLite، و تست‌های Fake و telemetry. تست‌های compile بخش اصلی‌اند و مقایسهٔ رشتهٔ ساده از دادهٔ `[Theory]` هستند، پس میلی‌ثانیه‌ای‌اند و I/O ندارند.
2. **لایهٔ ۲، job جدید `database`، فقط Linux، یک پایهٔ matrix برای هر سرور (`mysql`، `postgres`، `sqlserver`):** هر پایه container سرویس خودش را دارد و فقط مجموعهٔ contract همان dialect را اجرا می‌کند (trait در xUnit به نام `Dialect`). پایه‌ها هم‌زمان اجرا می‌شوند، پس زمان کل برابر کندترین پایه است نه جمع آن‌ها، و job موجود `services` طولانی‌تر نمی‌شود. این بخش ۵ را اصلاح می‌کند که سه container را داخل `services` بالا می‌آورد.
3. **containerهای سریع و تست‌های سریع:**
   - PostgreSQL از image alpine با `fsync=off` و `synchronous_commit=off` و `full_page_writes=off` استفاده می‌کند؛ MySQL بدون binary log و با تنظیمات flush سبک اجرا می‌شود؛ هر دو داده‌شان را روی `tmpfs` نگه می‌دارند (`options: --tmpfs ...`). SQL Server را نمی‌شود زیاد تنظیم کرد، پس پایهٔ آن زمان کل را تعیین می‌کند.
   - هر dialect **یک fixture مشترک از نوع collection در xUnit** دارد: schema یک‌بار با پیشوند یکتا برای هر اجرا ساخته می‌شود و یک مجموعهٔ دادهٔ کوچک ثابت یک‌بار seed می‌شود. تست‌های خواندن (select، where، join، aggregate) از آن مشترکاً استفاده می‌کنند و DDL برای هر تست ندارند. تست‌های نوشتن (R23) **داخل تراکنشی که rollback می‌شود** اجرا می‌شوند، پس بدون ساخت دوبارهٔ هیچ چیز ایزوله‌اند. تست‌هایی که DDL لازم دارند از جدول موقت با نام یکتا استفاده می‌کنند. کلاس‌های تست جدول‌های جدا دارند تا xUnit بتواند آن‌ها را موازی اجرا کند.
   - کش بستهٔ NuGet با `actions/cache` و با کلید `Directory.Packages.props` نگه داشته می‌شود.
4. **فیلتر مسیر و شبکهٔ ایمنی:** روی PR لایهٔ ۲ فقط وقتی اجرا می‌شود که PR به `src/Naravel.Database*`، `tests/Naravel.Database*`، `Directory.Packages.props` یا workflow دست بزند؛ روی هر push به `main` و در زمان‌بندی شبانه همیشه اجرا می‌شود. یک job پایانی `ci-ok` (با `if: always()`) لایهٔ ۲ ردشده (skipped) را موفق و هر شکست را شکست حساب می‌کند، پس branch protection فقط `ci-ok` را لازم می‌داند و یک job با فیلتر مسیر هرگز با غایب‌بودن جلوی merge را نمی‌گیرد.

**بودجه و تشدید:** R20.T02 زمان واقعی را اندازه می‌گیرد. اگر یک پایه بیش از پنج دقیقه طول کشید، مجموعهٔ contract داخل همان پایه با trait شارد می‌شود. برای صرفه‌جویی در زمان هرگز یک dialect **حذف نمی‌شود** (تصمیم مالک). `scripts/test-local.sh` گزینهٔ `--db` می‌گیرد که سه سرور را از `docker-compose.yml` ریشه بالا می‌آورد تا لایهٔ ۲ محلی هم قابل اجرا باشد.

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

اصلاحیهٔ ۲۰۲۶-۱۰-۱۷ (الف۱ تا الف۶) به درخواست مالک پس از بازبینی معماری نوشته شد؛ همهٔ تصمیم‌های تأییدشده را نگه می‌دارد و محدودیت‌های بالا را اضافه می‌کند. موارد بازش را مالک در مرحله‌ای که پیاده‌شان می‌کند تصمیم می‌گیرد.

مالک در ۲۰۲۶-۱۰-۱۶ تأیید کرد: (الف) Dapper در هستهٔ `Naravel.Database` می‌ماند (گزینهٔ پیشنهادی)؛ (ب) مرزهای مراحل R19 تا R26 پذیرفته شد؛ (ج) مالک بهترین گزینه را خواست و تصمیم این است که **هر چهار پایگاه داده از نسخهٔ اول** باشند. سه کانتینر سرور در job موجود `services` هم‌زمان بالا می‌آیند؛ اگر مدت آن job مشکل شد، تست‌های قرارداد پایگاه داده به job جدا در همان workflow منتقل می‌شوند و dialectی حذف نمی‌شود. بسته‌ها (Dapper، MySqlConnector، Microsoft.Data.SqlClient، Npgsql، Microsoft.Data.Sqlite) تأیید شده‌اند؛ نسخه‌های دقیق در R19.T02 و R20.T01 از متادیتای NuGet راستی‌آزمایی می‌شوند.
