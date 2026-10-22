# پایگاه داده (Query Builder)

`Naravel.Database` یک query builder به سبک Laravel برای .NET است: اتصال‌های نام‌دار، سازندهٔ روان (fluent) و SQL با پارامتر برای SQLite، MySQL، PostgreSQL و SQL Server. EF Core همچنان ابزار موجودیت‌ها و migration است؛ این بسته برای مسیر «فقط یک کوئری اجرا کن» است، با Dapper برای اجرای سریع و **بدون SqlKata**.

تصمیم‌ها: [PDR-022](../pdr/fa/PDR-022-database-query-builder.md). نسخهٔ انگلیسی: [docs/en/database.md](../en/database.md).

> **آنچه امروز وجود دارد.** اتصال‌ها، manager، درایورهای SQLite، MySQL، PostgreSQL و SQL Server، درز اتصال و query builder که برای هر چهار دیالکت SQL را **کامپایل** می‌کند (`ToSql`، `ToRawSql`، `GetBindings`)، و **اجرا** روی هر چهار پایگاه داده، اثبات‌شده با یک suite قرارداد مشترک در CI (`GetAsync`، `FirstAsync`، `SoleAsync`، `ValueAsync`، `FindAsync`، `PluckAsync` و SQL خام از طریق `IDatabase`). قابلیت مشاهده (meter، activity، `IQueryListener`، query log اختیاری) و fake تست یعنی `DatabaseFake` هم کار می‌کنند؛ خانوادهٔ where (مقدار، null، between، ستون، بخش تاریخ، any/all/none، الگو، متن کامل، JSON، exists و زیرکوئری)، شرطی‌ها، join، گروه‌بندی، having و کمک‌های ترتیب همه برای چهار دیالکت کامپایل می‌شوند و در CI روی آن‌ها اجرا می‌شوند، و همین‌طور aggregate و وجود (`CountAsync`، `MinAsync`، `MaxAsync`، `SumAsync`، `AvgAsync`، `ExistsAsync`)، chunk (`ChunkAsync`، `ChunkByIdAsync`، `EachAsync`) و استریم (`CursorAsync`، `LazyAsync`، `LazyByIdAsync`) و نوشتن (insert، update، upsert، delete، truncate و returning)؛ تراکنش بعداً می‌آید. برنامهٔ متد‌به‌متد در [roadmap/QUERY-BUILDER-COVERAGE.md](../../roadmap/QUERY-BUILDER-COVERAGE.md) است. هیچ چیزِ دیگری ادعا نمی‌شود.

## شروع سریع

```csharp
builder.Services.AddNaravelDatabase(builder.Configuration);
builder.Services.AddNaravelSqliteDatabase(builder.Configuration);   // ترتیب این دو فرقی ندارد
```

```json
{ "Database": { "Default": "main", "Connections": { "main": { "Driver": "sqlite", "Path": "app.db" } } } }
```

هر اتصال یا `Path` (یک فایل یا `:memory:`) می‌گیرد یا `ConnectionString` کامل. عمداً **هیچ پیش‌فرض داخلی وجود ندارد**: وقتی چیزی پیکربندی نشده باشد، درخواست اتصال پیش‌فرض خطا می‌دهد و بی‌صدا دیتابیس نمی‌سازد.

## استفاده

- `DatabaseManager.Connection(name)` تعریف اتصال نام‌دار را برمی‌گرداند (`IDatabaseConnection` با `Name`، `Dialect` و `OpenConnectionAsync`). معادل Laravel: `DB::connection($name)`.
- `DatabaseManager.Extend(name, factory)` در زمان اجرا اتصال اضافه می‌کند؛ تغییر `Database:Connections:<name>` در پیکربندی فقط همان اتصال را بازسازی می‌کند و کلیدهای نامرتبط اثری ندارند.
- `IConnectionScope.AcquireAsync(name)` یک `IConnectionLease` (با `Connection`، `Transaction` و `OwnsConnection`) می‌دهد که همهٔ کوئری‌ها از آن عبور می‌کنند. پس از اتمام، lease را Dispose کنید.
- دیتابیس‌های `:memory:` تا زمان اجرای برنامه دادهٔ خود را نگه می‌دارند و دو نام اتصال هرگز یکی را به اشتراک نمی‌گذارند.

## امتحان کنید

`samples/Naravel.Sample` یک دموی اجرایی در `/database` روی SQLite درون‌حافظه دارد: شرط‌هایی که برای هر پارامتر درخواست اضافه می‌شوند، مقدارهای bind‌شده کنار SQL، ردشدن نام ستون نامعتبر در لحظهٔ فراخوانی، `WhereYear`، `FindAsync` و لاگ کوئری. [README نمونه](../../samples/Naravel.Sample/README.md) را ببینید.

## اجرای کوئری

```csharp
public class UserService(IDatabase db)
{
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ActiveAsync(CancellationToken ct) =>
        db.Table("users").Where("active", true).OrderBy("name").GetAsync(ct);

    public Task<UserRow?> ByEmailAsync(string email, CancellationToken ct) =>
        db.Table("users").Where("email", email).FirstAsync<UserRow?>(ct);
}
```

`IDatabase` را `AddNaravelDatabase` برای اتصال پیش‌فرض ثبت می‌کند؛ `db.Connection("reports")` نمونه‌ای متصل به نام دیگر می‌دهد. `db.Table(...)` یک `Query` برمی‌گرداند که از پیش متصل است. کوئریِ ساخته‌شده با `Query.For(dialect)` فقط SQL کامپایل می‌کند و اگر اجرایش کنید `InvalidOperationException` روشنی می‌دهد.

| متد | خروجی | Laravel |
|---|---|---|
| `GetAsync()` / `GetAsync<T>()` | همهٔ ردیف‌ها، به‌صورت dictionary یا `T` | `get` |
| `FirstAsync()` / `FirstAsync<T>()` | اولین ردیف، یا `null` / مقدار پیش‌فرض | `first` |
| `SoleAsync()` / `SoleAsync<T>()` | دقیقاً یک ردیف؛ برای هیچ‌کدام `RecordsNotFoundException` و برای بیش از یکی `MultipleRecordsFoundException` | `sole` |
| `ValueAsync<T>(column)` | یک ستون از اولین ردیف، یا مقدار پیش‌فرض | `value` |
| `FindAsync(id, keyColumn = "id")` / `FindAsync<T>(...)` | ردیف با آن کلید، یا `null` / مقدار پیش‌فرض | `find` |
| `PluckAsync<T>(column)` | یک ستون از همهٔ ردیف‌ها | `pluck` (هنوز بدون آرگومان کلید) |

SQL خام از طریق `IDatabase` و با placeholder `?` اجرا می‌شود (هر `?` یک پارامتر bound می‌شود؛ برای علامت سؤال واقعی `??` بنویسید؛ تعداد نادرست مقدارها پیش از اجرا `ArgumentException` می‌دهد):

```csharp
var rows  = await db.SelectAsync("select * from users where votes > ?", [100], ct);        // Laravel: DB::select
var count = await db.ScalarAsync<long>("select count(*) from users", cancellationToken: ct); // DB::scalar
await db.StatementAsync("update users set votes = ? where id = ?", [0, 7], ct);             // DB::statement
var rowsChanged = await db.AffectingStatementAsync("delete from users where votes = ?", [0], ct); // DB::affectingStatement
await db.UnpreparedAsync("create table t (id integer primary key)", ct);                    // DB::unprepared (بدون placeholder)
```

نکته‌هایی که باید بدانید:
- **دو راه برای خواندن ردیف‌ها.** بدون آرگومان نوع، ردیف‌ها مستقیم از `DbDataReader` در dictionaryهای بدون حساسیت به حروف خوانده می‌شوند (سریع، سازگار با trim و AOT؛ مقدارها نوع خام provider را نگه می‌دارند، مثلاً SQLite عددهای صحیح را `long` و تاریخ‌ها را متن برمی‌گرداند). با `<T>` خود Dapper ردیف را map می‌کند که **با trim و AOT سازگار نیست**. Naravel هیچ type handler سراسریِ Dapper ثبت نمی‌کند؛ Dapper نسخهٔ 2.1.86 به بعد `DateOnly` و `TimeOnly` را روی .NET 8 و 10 خودش map می‌کند.
- **نوع پارامترها.** `bool`، `decimal`، `Guid`، `DateTime`، `DateTimeOffset`، `DateOnly`، `TimeOnly` و `byte[]` با `DbType` صریح bind می‌شوند؛ enum به عدد صحیح زیرینش تبدیل می‌شود؛ `null` به‌صورت `DBNull` bind می‌شود.
- **اتصال و تراکنش.** هر فراخوانی از `IConnectionScope` یک lease می‌گیرد، `command.Transaction` را از آن تنظیم می‌کند و آن را dispose می‌کند. scope پیش‌فرض برای هر فراخوانی یک اتصال pool‌شده باز و بسته می‌کند؛ scope‌ای که اتصالِ دارای تراکنش قرض می‌دهد (با R25 می‌آید) join می‌شود و هرگز بسته یا commit نمی‌شود. هر دو رفتار همین امروز با تست ثابت شده‌اند.
- `FirstAsync`، `SoleAsync`، `ValueAsync` و `FindAsync` روی یک clone کار می‌کنند و `limit` خودشان را اضافه می‌کنند، پس کوئری اصلی تغییر نمی‌کند.
- `SoleAsync` حداکثر دو ردیف می‌گیرد. در SQL Server، `limit` به‌صورت `top (n)` نوشته می‌شود، پس این متدها نیازی به `OrderBy` ندارند.

## مشاهده‌پذیری و تست

**Telemetry.** هر دستور شمرده و زمان‌گیری می‌شود. meter و activity source هر دو `Naravel.Database` نام دارند: شمارنده‌های `naravel.database.queries` و `naravel.database.failures`، هیستوگرام `naravel.database.query.duration` (میلی‌ثانیه) و یک activity با نام `naravel.database.query` برای هر دستور. tagها فقط **نام اتصال و نوع عملیات** (`select`، `scalar`، `statement`) هستند. متن SQL و مقادیر هرگز وارد telemetry نمی‌شوند، پس ارسال آن به collector مشترک امن است.

```csharp
builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter("Naravel.Database")).WithTracing(t => t.AddSource("Naravel.Database"));
```

**Listenerها (`DB::listen`).** `IQueryListener` را پیاده‌سازی و در DI ثبت کنید؛ بعد از هر دستور (حتی ناموفق) یک `QueryExecuted` (اتصال، عملیات، SQL با نشانگرهای `@p0`، مقادیر، مدت، استثنا) می‌گیرد. listenerها روی thread فراخوان اجرا می‌شوند، پس سریع بنویسیدشان. استثنای یک listener بلعیده می‌شود تا logger خراب هرگز کوئری را شکست ندهد. listenerها یک بار، هنگام اولین resolve شدن `IDatabase`، گرفته می‌شوند.

**Query log (`enableQueryLog`).** با `services.AddNaravelDatabaseQueryLog()` فعالش کنید و `QueryLog` را تزریق کنید: `Entries`، `Clear()`، `Enabled`. آخرین ۱۰۰۰ مورد را نگه می‌دارد (قابل تنظیم) و مقادیر bind‌شده را هم دارد؛ پس در توسعه و تست استفاده کنید، نه production.

**`DatabaseFake`.** برای تست‌هایی که پایگاه داده نمی‌خواهند، `new DatabaseFake()` را جای `IDatabase` تزریق کنید. `Table(...)` برای دیالکتی که می‌دهید (پیش‌فرض SQLite) SQL واقعی کامپایل می‌کند، پس می‌توانید روی SQL و مقادیر assert کنید:

```csharp
var fake = new DatabaseFake().ReturnRows([new Dictionary<string, object?> { ["id"] = 1 }], sqlContains: "from \"users\"");
var service = new UserService(fake);
await service.ActiveUsersAsync();
fake.AssertExecuted(q => q.Sql.Contains("votes") && (int)q.Bindings[0]! == 10);
```

نتیجه‌ها را با `ReturnRows`، `ReturnObjects<T>` (برای `GetAsync<T>`)، `ReturnScalar` و `ReturnAffected` تنظیم کنید؛ آخرین تنظیمِ منطبق برنده است و هر چه تنظیم نشده باشد ردیف خالی، مقدار پیش‌فرض یا ۰ ردیف تأثیرپذیرفته برمی‌گرداند. با `AssertExecuted`، `AssertExecutedCount` و `AssertNothingExecuted` بررسی کنید. fake، SQL را اجرا نمی‌کند؛ رفتاری که به موتور واقعی وابسته است تست SQLite یا سرور می‌خواهد.

## query builder

```csharp
var query = Query.For(DatabaseDialect.Postgres)
    .Table("users")
    .Select("id", "name")
    .Where("active", true)
    .Where(g => g.Where("age", ">=", 18).OrWhere("vip", true))
    .OrderByDesc("created_at")
    .Limit(10).Offset(20);

query.ToSql();       // select "id", "name" from "users" where "active" = @p0 and ("age" >= @p1 or "vip" = @p2) order by "created_at" desc limit 10 offset 20
query.GetBindings(); // [true, 18, true]
```

موجود امروز: `Table`، `Select`، `AddSelect`، `SelectRaw`، `Distinct`؛ خانوادهٔ where: `Where`/`OrWhere`/`WhereNot`/`OrWhereNot` (ستون و مقدار، با عملگر، آرایهٔ جفت‌ها یا گروه closure)، `WhereRaw`/`OrWhereRaw`، `WhereIn`/`WhereNotIn` (مقدارها یا callback زیرکوئری)، `WhereBetween`/`WhereNotBetween`، `WhereBetweenColumns`/`WhereNotBetweenColumns`، `WhereNull`/`WhereNotNull`، `WhereColumn`، `WhereDate`/`WhereMonth`/`WhereDay`/`WhereYear`/`WhereTime`، `WhereAny`/`WhereAll`/`WhereNone`، `WhereLike`/`WhereNotLike`، `WhereFullText`، `WhereJsonContains`/`WhereJsonDoesntContain`/`WhereJsonContainsKey`/`WhereJsonDoesntContainKey`/`WhereJsonLength`، `WhereExists`/`WhereNotExists`، `WhereSubquery` (هرکدام با شکل `or`؛ کران‌ها پارامتر هستند و نباید null باشند چون `between null` هرگز تطبیق نمی‌دهد)؛ `When`/`Unless`؛ `Join`/`LeftJoin`/`RightJoin`/`CrossJoin` با `JoinClause`؛ `GroupBy`، `Having` و شکل‌هایش؛ `OrderBy`/`OrderByDesc`/`OrderByRaw`، `Latest`/`Oldest`، `InRandomOrder`، `Reorder`/`ReorderDesc`؛ `Limit`/`Take`، `Offset`/`Skip`؛ `Clone`، `Compile`، `ToSql`، `ToRawSql`، `GetBindings`؛ از R22: `CountAsync`، `MinAsync`، `MaxAsync`، `SumAsync`، `AvgAsync`، `ExistsAsync`، `DoesntExistAsync`، `ChunkAsync`، `ChunkByIdAsync`، `ChunkMapAsync`، `EachAsync`، `EachByIdAsync`، `CursorAsync`، `LazyAsync`، `LazyByIdAsync`، `LazyByIdDescAsync`؛ از R23: `InsertAsync`، `InsertOrIgnoreAsync`، `InsertGetIdAsync`، `InsertUsingAsync`، `UpdateAsync`، `UpdateAllAsync`، `UpdateOrInsertAsync`، `IncrementAsync`، `DecrementAsync`، `IncrementEachAsync`، `DecrementEachAsync`، `AllRows`، `UpsertAsync`، `DeleteAsync`، `DeleteByIdAsync`، `DeleteAllAsync`، `TruncateAsync`، `InsertReturningAsync`، `UpdateReturningAsync`، `DeleteReturningAsync`؛ از R24: `Union`، `UnionAll`، `FromSub`، `SelectSub`، `JoinSub`/`LeftJoinSub`/`RightJoinSub`، `JoinLateral`/`LeftJoinLateral`، `WithExpression`، `WithRecursiveExpression`، `GroupByRaw`، `HavingRaw`/`OrHavingRaw`، `FromRaw` و مقدار `Raw`، `SharedLock`، `LockForUpdate`. تراکنش بعداً می‌آید.

**بخش‌های تاریخ و شرط چندستونی.** `WhereDate`، `WhereMonth`، `WhereDay`، `WhereYear` و `WhereTime` (هرکدام با شکل عملگر و شکل `or`) یک بخش از ستون تاریخ یا زمان را مقایسه می‌کنند. این چهار موتور تابع مشترکی برای این کار ندارند، پس هر گرامر تابع خودش را می‌نویسد؛ مقدار همیشه پارامتر است:

| متد | SQLite | MySQL | PostgreSQL | SQL Server |
|---|---|---|---|---|
| `WhereDate` | `date(c)` | `date(c)` | `cast(c as date)` | `cast(c as date)` |
| `WhereMonth` / `Day` / `Year` | `cast(strftime('%m', c) as integer)` (و `%d`، `%Y`) | `month(c)` / `day(c)` / `year(c)` | `extract(month from c)` | `datepart(month, c)` |
| `WhereTime` | `strftime('%H:%M:%S', c)` | `time(c)` | `cast(c as time)` | `cast(c as time)` |

دلیل تبدیل مقدار در لحظهٔ فراخوانی: `DateTime`، `DateTimeOffset` یا رشتهٔ `yyyy-MM-dd` در `WhereDate` به `DateOnly` تبدیل می‌شود و `WhereTime` یکی از `TimeOnly`، `TimeSpan` (داخل یک روز)، `DateTime` یا رشتهٔ `HH:mm[:ss]` را می‌گیرد؛ پس هر provider پارامتر بومی تاریخ یا زمان می‌گیرد و PostgreSQL هرگز مجبور نمی‌شود تاریخ را با متن مقایسه کند. ماه، روز و سال عدد صحیح‌اند و بازه‌شان بررسی می‌شود (۱ تا ۱۲، ۱ تا ۳۱، ۱ تا ۹۹۹۹). فقط `= < > <= >= <> !=` پذیرفته می‌شود. روی SQLite تاریخ‌ها به‌صورت متن ISO که provider می‌نویسد مقایسه می‌شوند؛ ستونی با قالب متنی دیگر تطبیق نخواهد داد.

`WhereAny`، `WhereAll` و `WhereNone` فهرستی از ستون‌ها، یک عملگر اختیاری و یک مقدار می‌گیرند. هر ستون پارامتر خودش را دارد (همان مقدار تکرار می‌شود)، پس سقف پارامتر آن‌ها را هم می‌شمارد. Any به شکل `(a = @p0 or b = @p1)` است، all با `and` وصل می‌کند و none به شکل `not (a = @p0 or b = @p1)` است. فهرست خالی در لحظهٔ فراخوانی خطا می‌دهد.

**الگو، جست‌وجوی متن کامل و JSON.** `WhereLike` و `WhereNotLike` (با شکل‌های `OrWhere...`) یک الگو را تطبیق می‌دهند؛ الگو همیشه پارامتر است و `%` و `_` همچنان wildcard هستند (escape نمی‌شوند). پیش‌فرض بدون حساسیت به حروف است؛ برای تطبیق دقیق `caseSensitive: true` بدهید. `WhereFullText` یک یا چند ستون ساده را با `FullTextMode.Natural` (کلمات ساده) یا `FullTextMode.Boolean` (نحو بولی خود موتور، که باز هم به‌صورت مقدار bind می‌شود) جست‌وجو می‌کند. هر گرامر SQL خودش را می‌نویسد:

| متد | SQLite | MySQL | PostgreSQL | SQL Server |
|---|---|---|---|---|
| `WhereLike` | `c like p` | `c like p` (با حساسیت: `like binary`) | `c ilike p` (با حساسیت: `like`) | `c like p` (با حساسیت: `collate SQL_Latin1_General_CP1_CS_AS`) |
| `WhereFullText` | `NotSupportedException` | `match(c) against (p in natural language mode)` یا `in boolean mode` | `to_tsvector('english', c) @@ plainto_tsquery('english', p)` (برای بولی `to_tsquery`) | `freetext(c, p)` یا `contains(c, p)` |
| `WhereJsonContains` | `exists (select 1 from json_each(c, path) where value is p)` | `json_contains(c, p, path)` | `c::jsonb @> p::jsonb` | `p in (select value from openjson(c, path))` |
| `WhereJsonContainsKey` | `json_type(c, path) is not null` | `json_contains_path(c, 'one', path)` | `jsonb_path_exists(c::jsonb, path::jsonpath)` | `json_value(c, path)` یا `json_query(c, path)` غیر null |
| `WhereJsonLength` | `json_array_length` | `json_length` | `jsonb_array_length` | `(select count(*) from openjson(...))` |

`WhereJsonContains` و `WhereJsonDoesntContain` روی MySQL و PostgreSQL هر مقدار قابل‌سریال‌سازی به JSON را می‌گیرند؛ SQLite و SQL Server فقط یک مقدار **اسکالر** (رشته، عدد، بولی) را داخل آرایهٔ JSON جست‌وجو می‌کنند و برای مقدار آرایه یا شیء `NotSupportedException` می‌دهند. `WhereJsonContainsKey` و `WhereJsonDoesntContainKey` مسیر می‌خواهند (`data->name`). `WhereJsonLength` تعداد عناصر آرایه را می‌شمارد (SQL Server و MySQL کلیدهای شیء را هم می‌شمارند)؛ طول منفی در لحظهٔ فراخوانی خطاست.

**ستون‌های مسیر JSON.** ستونی که به شکل `data->name`، `data->list[0]` یا `data->a->b` نوشته شود در هر متد where که ستون می‌گیرد (`Where`، `WhereNull`، `WhereIn`، `WhereBetween`، `WhereLike` و متدهای JSON) پذیرفته می‌شود. بخش‌های مسیر باید از `[حرف، رقم، _]` با `[n]` اختیاری باشند؛ مسیر **به‌صورت پارامتر bind** می‌شود (`$."a"."b"[0]` یا آرایهٔ متنی برای PostgreSQL) و هرگز داخل متن SQL نمی‌آید. مسیر در `Select`، `OrderBy`، `Table` و بخش‌های تاریخ پذیرفته نمی‌شود (`NotSupportedException`). مقدار استخراج‌شده یک اسکالر قابل مقایسه است: در SQLite `json_extract`، در MySQL `json_unquote(json_extract(...))`، در SQL Server `json_value` و در PostgreSQL عملگر `#>>` که اگر مقدار مقایسه عدد یا بولی باشد به `numeric` یا `boolean` cast می‌شود. محدودیت‌ها: `null` در JSON روی SQLite، PostgreSQL و SQL Server همان `NULL` است ولی روی MySQL متن `null` است، و مقایسهٔ بولی JSON روی MySQL کار نمی‌کند (متن استخراج‌شدهٔ `true` همان عدد `1` نیست)، پس آنجا به‌صورت رشته مقایسه کنید. جست‌وجوی متن کامل روی MySQL و SQL Server به ایندکس متن کامل نیاز دارد (ایمیج تست SQL Server مؤلفهٔ متن کامل ندارد، پس آن مسیر فقط با تست‌های compile اثبات می‌شود)؛ PostgreSQL از پیکربندی `english` استفاده می‌کند.

**exists، زیرکوئری و شرطی‌ها.** `WhereExists` و `WhereNotExists` (با شکل‌های `Or`) یک callback می‌گیرند که کوئری تازه‌ای دریافت می‌کند: روی آن `Table(...)` را صدا بزنید و ستون و شرط اضافه کنید تا به `exists (select ...)` تبدیل شود. `WhereSubquery(column, op, callback)` یک ستون را با زیرکوئری اسکالر مقایسه می‌کند و `WhereIn` و `WhereNotIn` (و شکل‌های `Or`) callbackی می‌پذیرند که یک ستون را select می‌کند. زیرکوئری داخل همان دستور و با همان شمارندهٔ پارامتر کامپایل می‌شود، پس مقدارها در هر عمقی از تو در تویی به ترتیب متن شماره می‌گیرند و سقف پارامتر دیالکت همه را می‌شمارد؛ زیرکوئری با `Limit` در SQL Server به `top (n)` تبدیل می‌شود. زیرکوئری باید جدول داشته باشد (وگرنه در لحظهٔ فراخوانی `InvalidOperationException`). برای همبستگی از `WhereColumn("posts.user_id", "users.id")` استفاده کنید. `When(condition, callback, otherwise)` و `Unless(...)` بسته به یک `bool` ساده callback را روی همان کوئری اعمال می‌کنند تا فیلترهای اختیاری در یک زنجیره بمانند. چرا `WhereSubquery` نام جدا دارد: Laravel متد `where` را با closure overload می‌کند، اما اینجا `Where("a", null)` یعنی `is null` و overload دومی با callback یک `null` لفظی را بی‌صدا می‌گرفت.

**join.** `Join` (inner)، `LeftJoin`، `RightJoin` و `CrossJoin` یک جدول را join می‌کنند و alias اختیاری دارند (`posts as p`). شکل‌های کوتاه دو ستون (`Join("posts", "users.id", "posts.user_id")`) یا دو ستون و یک عملگر می‌گیرند؛ شکل callback یک `JoinClause` می‌دهد با `On` و `OrOn` (ستون با ستون) و `Where`، `OrWhere`، `WhereNull`، `WhereNotNull`، `WhereIn` (ستون با **مقدار bind‌شده**)، پس مقدار هرگز SQL نمی‌شود. هر join دست‌کم یک شرط می‌خواهد (وگرنه در لحظهٔ فراخوانی `InvalidOperationException`)؛ `CrossJoin` شرط ندارد. joinها درست بعد از `from` و قبل از `where` و به ترتیب فراخوانی نوشته می‌شوند، پس مقدارهای شرط join قبل از مقدارهای `where` شماره می‌گیرند، فرقی ندارد متدها را به چه ترتیبی صدا زده‌اید، و پارامترهای join هم در سقف دیالکت شمرده می‌شوند. شرط‌ها در یک join مثل `where` ترکیب می‌شوند (پیش‌فرض `and`، با شکل `Or` می‌شود `or`، و تقدم معمول SQL یعنی `and` قبل از `or` برقرار است). join داخل زیرکوئری کار می‌کند و `Clone` آن را کپی می‌کند. نکتهٔ دیالکت: `RIGHT JOIN` به SQLite نسخهٔ 3.39 یا بالاتر نیاز دارد (`e_sqlite3` همراه پروژه جدیدتر است؛ SQLite قدیمیِ سفارشی هنگام اجرا خطا می‌دهد)؛ `FULL OUTER JOIN`، join با زیرکوئری و join جانبی (lateral) در R21 نیستند.

**گروه‌بندی، having و کمک‌های ترتیب.** `GroupBy` یک یا چند ستون می‌گیرد (نام‌های qualified مجاز) و `group by` را بعد از `where` و قبل از `having` می‌نویسد. `Having`، `OrHaving`، `HavingBetween`، `OrHavingBetween`، `HavingNull`، `HavingNotNull` و شکل‌های `Or` مثل همتاهای `Where` خود کار می‌کنند؛ هر مقدار پارامتر است. PostgreSQL و SQL Server در `having` فقط **ستون‌های گروه‌بندی‌شده** را می‌پذیرند، نه alias بخش select (`SelectRaw("count(*) as n")` و بعد `Having("n", ...)` فقط روی SQLite و MySQL کار می‌کند)؛ شرط‌های aggregate مثل `count(*) > 1` با `HavingRaw` و متدهای aggregate در مرحله‌های بعد می‌آیند. `Latest(column = "created_at")` و `Oldest(...)` یک `order by` نزولی یا صعودی اضافه می‌کنند. `InRandomOrder()` تابع خود موتور را می‌نویسد (`random()`، `rand()`، `newid()`). `Reorder()` همهٔ ترتیب‌ها را حذف می‌کند، `Reorder(column)` و `ReorderDesc(column)` آن را با یک ترتیب جایگزین می‌کنند؛ ستون نامعتبر ترتیب موجود را دست‌نخورده می‌گذارد.

**تجمیع و وجود.** `CountAsync()`، `CountAsync(column)`، `MinAsync<T>(column)`، `MaxAsync<T>(column)`، `SumAsync(column)`، `AvgAsync(column)`، `ExistsAsync()` و `DoesntExistAsync()` روی کوئری همان‌طور که ساخته شده اجرا می‌شوند، پس هر `where`، join و binding اعمال می‌شود. `Count` مقدار `long` برمی‌گرداند؛ `Count(column)` مقدارهای null را نمی‌شمارد و بعد از `Distinct()` مقدارهای متمایز را می‌شمارد (برای `Sum`، `Avg`، `Min` و `Max` هم همین‌طور). `Sum` روی هیچ ردیفی `0` است و `Avg`، `Min` و `Max` روی هیچ ردیفی `null` (برای `Min`/`Max` از `T` nullable استفاده کنید تا «هیچ» از صفر واقعی جدا شود)، مثل Laravel. `OrderBy` موجود نادیده گرفته می‌شود چون PostgreSQL و SQL Server یک `order by` اضافه را در تجمیع نمی‌پذیرند. کوئری با `GroupBy`، `Having`، `Distinct` بدون ستون، `Limit` یا `Offset` به‌صورت **derived table** شمرده می‌شود (`select count(*) from (select ...) as naravel_aggregate`)، پس عدد برابر ردیف‌هایی است که کوئری برمی‌گرداند؛ برای `Min`، `Max`، `Sum` و `Avg` روی چنین کوئری، ستون با آخرین بخش نامش خوانده می‌شود و باید select شده باشد. `ExistsAsync` عبارت `select case when exists (select 1 ...) then 1 else 0 end` را اجرا می‌کند که هر چهار موتور می‌پذیرند (SQL Server `select exists(...)` ندارد)؛ ستون‌ها، ترتیب و `Distinct` حذف می‌شوند. دو واقعیت موتور: `avg` ستون صحیح در SQL Server صحیح است (اگر کسر می‌خواهید ستون را اول در `SelectRaw` تبدیل کنید) و `sum` یک `bigint` در PostgreSQL از نوع `numeric` است که `SumAsync` به `decimal` تبدیل می‌کند. ستون با گرامر شناسه بررسی می‌شود، پس `Sum("a as b")`، `Sum("*")` و مسیر JSON هنگام فراخوانی رد می‌شوند.

**Chunking.** برای جدول‌هایی که یکجا در حافظه جا نمی‌شوند، `ChunkAsync(count, callback)` صفحه‌ها را با `limit`/`offset` می‌خواند و `callback` را برای هر صفحهٔ غیرخالی صدا می‌زند (یک `IReadOnlyList` از ردیف‌ها، یا از `T` با `ChunkAsync<T>`)؛ callback برای ادامه `true` و برای توقف `false` برمی‌گرداند و فقط صفحهٔ جاری در حافظه است. `ChunkAsync` **به `OrderBy` نیاز دارد** (صفحه‌های کوئری بدون ترتیب پایدار نیستند) و هر `Limit`/`Offset` موجود را جایگزین می‌کند. چون offset جابه‌جا می‌شود، ردیفی که وسط حلقه درج یا حذف شود ممکن است جا بیفتد یا دوبار دیده شود؛ وقتی جدول هم‌زمان با پیمایش تغییر می‌کند (دلیل معمول chunk کردن و به‌روزرسانی)، از `ChunkByIdAsync(count, callback, column: "id")` استفاده کنید که با یک ستون یکتا مرتب می‌کند و `where id > last` می‌خواهد؛ پس صفحه‌های عمیق به اندازهٔ صفحهٔ اول هزینه دارند و تغییرِ پشت cursor اثری ندارد. ستون باید در ستون‌های select‌شده باشد و هرگز null نباشد (وگرنه `InvalidOperationException` با پیام روشن)؛ `ChunkByIdAsync<T>` تابعی می‌گیرد که کلید را از `T` می‌خواند. `ChunkMapAsync(map, count = 1000)` خروجی `map` را برای همهٔ ردیف‌ها جمع می‌کند (حافظه با نتیجه بزرگ می‌شود)، `EachAsync(callback, count = 1000)` و `EachByIdAsync(callback, count = 1000, column: "id")` برای هر ردیف callback می‌زنند و با `false` می‌ایستند. callback در Laravel شمارهٔ صفحه را هم می‌گیرد؛ اینجا فقط صفحه را می‌گیرد چون یک شمارنده در closure همان کار را می‌کند.

**Streaming.** `CursorAsync()` (و `CursorAsync<T>()`) یک `IAsyncEnumerable` روی **یک** دستور برمی‌گرداند: reader باز می‌ماند و هر ردیف همان‌طور که خوانده می‌شود تحویل داده می‌شود، پس حافظه هر بار فقط یک ردیف نگه می‌دارد. اتصال تا پایان حلقه، `break` یا لغو token امانت گرفته می‌ماند و هر کدام reader و اتصال را آزاد می‌کنند. هنگام خواندن روی همان اتصال دستور دیگری اجرا نکنید (SQL Server برای این کار MARS می‌خواهد و داخل یک transaction scope دستورها یک اتصال مشترک دارند). observer برای کل stream یک دستور گزارش می‌کند و آن را وقتی تمام شد گزارش می‌دهد، پس مدت آن شامل زمانی است که بین ردیف‌ها صرف می‌کنید. `LazyAsync(count = 1000)` هم ردیف‌ها را یکی‌یکی تحویل می‌دهد ولی **صفحه‌ها** را با `limit`/`offset` می‌خواند، پس بین صفحه‌ها چیزی نگه داشته نمی‌شود و می‌توانید داخل حلقه دستور دیگری اجرا کنید؛ به `OrderBy` نیاز دارد و همان هشدار offset `ChunkAsync` را دارد. `LazyByIdAsync(count, column: "id")` و `LazyByIdDescAsync(...)` یک ستون یکتا را دنبال می‌کنند (`where id > last` / `where id < last`) و با تغییر ردیف‌های پشت cursor پایدارند؛ `LazyByIdAsync<T>` و `LazyByIdDescAsync<T>` تابعی می‌گیرند که کلید را از `T` می‌خواند. آرگومان‌ها هنگام صدا زدن متد بررسی می‌شوند، دستورها هنگام پیمایش اجرا می‌شوند و توقف زودهنگام صفحهٔ بعدی را نمی‌خواهد.

**Insert.** `InsertAsync(row)` و `InsertAsync(rows)` هر مقدار را پارامتر می‌کنند و تعداد ردیف‌های درج‌شده را برمی‌گردانند (Laravel یک boolean برمی‌گرداند). ردیف هر `IReadOnlyDictionary<string, object?>` است؛ همهٔ ردیف‌های یک batch باید دقیقاً ستون‌های ردیف اول را داشته باشند (ترتیب می‌تواند فرق کند)، ستون باید نام ساده باشد (بدون پیشوند جدول و بدون alias) و ردیف خالی مقدارهای پیش‌فرض ستون‌ها را درج می‌کند (`default values`، و در MySQL `() values ()`). batch طوری تقسیم می‌شود که هیچ statement از سقف پارامتر dialect (و ۱۰۰۰ ردیف در SQL Server) عبور نکند و **statementها در یک تراکنش اجرا می‌شوند** (تراکنش جاری، یا یک تراکنش تازه که در پایان commit می‌شود)، پس batch یا کامل انجام می‌شود یا هیچ. بدون ردیف، هیچ دستوری اجرا نمی‌شود. کوئری insert فقط می‌تواند **جدول** داشته باشد: `where`، join، select، ترتیب یا limit روی آن `InvalidOperationException` می‌دهد، در حالی که Laravel بی‌صدا نادیده می‌گیرد. `InsertOrIgnoreAsync` ردیف‌هایی را که کلید یکتا را نقض می‌کنند رد می‌کند: `insert or ignore` (SQLite)، `insert ignore` (MySQL که بعضی خطاهای دیگر را هم به warning تبدیل می‌کند)، `on conflict do nothing` (PostgreSQL)؛ **SQL Server `NotSupportedException` می‌دهد** چون دستور تکی و اتمیک برای آن ندارد (`MERGE` بدون قفل امن نیست)؛ از `UpsertAsync` (R23.T03) یا مدیریت خطای کلید تکراری استفاده کنید. `InsertGetIdAsync<T>(row, idColumn: "id")` کلید تولیدشده را با `returning` (SQLite، PostgreSQL)، `output inserted` (SQL Server) یا در MySQL با دستور دوم `select last_insert_id()` روی همان اتصال برمی‌گرداند (اگر جدول ستون auto-increment نداشته باشد `0`). `InsertUsingAsync(columns, source)` عبارت `insert into t (columns) select ...` را می‌نویسد؛ کوئری source باید همان dialect باشد و مقدارهایش با همان شماره‌گذاری bind می‌شوند.

**Update.** `UpdateAsync(values)` هر مقدار را پارامتر می‌کند و تعداد ردیف‌های تغییرکرده را برمی‌گرداند؛ `null` مقدار `NULL` می‌نویسد. **به `where` نیاز دارد:** update بدون آن `InvalidOperationException` می‌دهد تا `where` فراموش‌شده یک جدول را بازنویسی نکند (Laravel اجرایش می‌کند). برای تغییر عمدیِ همهٔ ردیف‌ها اول `UpdateAllAsync(values)` یا `AllRows()` را صدا بزنید (از `Clone()` عبور می‌کند). کوئری update فقط می‌تواند جدول، join و where داشته باشد؛ select، group، having، ordering، distinct، limit و offset خطا می‌دهند. نام ستون ساده است (بدون پیشوند جدول و بدون alias). کلیدی مثل `options->theme` یک مسیر JSON داخل ستون را تنظیم می‌کند (`json_set` در SQLite و MySQL، `jsonb_set` در PostgreSQL، `json_modify` در SQL Server)؛ چند مسیر از یک ستون در یک assignment ترکیب می‌شوند، بقیهٔ JSON حفظ می‌شود، کلید آخرِ ناموجود ساخته می‌شود ولی **اشیای والد باید از قبل وجود داشته باشند** (SQLite و MySQL آن‌ها را نمی‌سازند)، ستون `NULL` در SQLite و MySQL `NULL` می‌ماند و تنظیم یک مسیر روی `null` در SQL Server رد می‌شود (آنجا `json_modify` کلید را حذف می‌کند). یک ستون را نمی‌توان در یک update هم کامل و هم با مسیر تنظیم کرد. **Joinها:** MySQL می‌نویسد `update t inner join o on ... set ...`، SQL Server `update t set ... from t inner join o on ...` (هدف update همان alias است)، PostgreSQL و SQLite `update t set ... from o where ...` فقط با **inner join** (left، right و cross join `NotSupportedException` می‌دهند)؛ چون نام ستون‌ها ساده است، ستونی که در هر دو جدول باشد در MySQL و SQL Server مبهم است، پس به جدول alias بدهید یا ستون را تغییر نام دهید. `IncrementAsync(column, amount = 1, extra)`، `DecrementAsync(...)`، `IncrementEachAsync(columns, extra)` و `DecrementEachAsync(...)` عبارت `set col = col + ?` را در یک statement می‌نویسند، پس افزایش‌های هم‌زمان گم نمی‌شوند؛ `extra` ستون‌های دیگر را در همان statement تنظیم می‌کند؛ برای ستون‌های decimal و اعشاری overload با `decimal` هست. `UpdateOrInsertAsync(attributes, values)` ردیفی را که با همهٔ attributeها بخواند می‌جوید و اگر نبود attributes به‌علاوهٔ values را درج می‌کند، وگرنه با `values` به‌روزرسانی می‌کند؛ وقتی **درج** کرد `true` برمی‌گرداند (Laravel همیشه `true` می‌دهد). دو دستور است و **اتمیک نیست**: روی attributeها کلید یکتا بگذارید و اگر دو فراخواننده ممکن است رقابت کنند از `UpsertAsync` (R23.T03) استفاده کنید.

**Upsert.** `UpsertAsync(rows, uniqueBy, update = null)` (یا یک ردیف) ردیف‌ها را درج می‌کند و اگر ردیفی با ردیف موجود برخورد کند، به‌جای درج همان را به‌روزرسانی می‌کند، با یک statement برای هر batch؛ تعداد ردیف‌های تأثیرگرفته را برمی‌گرداند (MySQL ردیف به‌روزشده را دوبار می‌شمارد). `uniqueBy` ستون‌هایی است که ردیف را مشخص می‌کنند (حداقل یکی، همه از ستون‌های ردیف) و در SQLite، PostgreSQL و SQL Server باید یک **unique index** آن‌ها را پوشش دهد؛ MySQL آن را نادیده می‌گیرد و به **هر** کلید یکتای جدول واکنش نشان می‌دهد. `update` ستون‌هایی را که هنگام برخورد بازنویسی می‌شوند فهرست می‌کند (همه از ستون‌های ردیف)؛ `null` همهٔ ستون‌ها را بازنویسی می‌کند و **فهرست خالی فراخوانی را به insert ساده تبدیل می‌کند**، پس برخورد خطا می‌دهد، مثل Laravel. SQLite و PostgreSQL می‌نویسند `on conflict (...) do update set c = excluded.c`؛ MySQL نسخهٔ **8.0.19 یا بالاتر** می‌نویسد `as naravel_upsert on duplicate key update c = naravel_upsert.c` (row alias جای تابع منسوخ `values()` را می‌گیرد؛ MariaDB پشتیبانی نمی‌شود)؛ SQL Server می‌نویسد `merge ... with (holdlock)` که زیر هم‌زمانی اتمیک است، با semicolon پایانی. batch در سقف پارامتر و ردیف شکسته می‌شود و مثل `InsertAsync` در یک تراکنش اجرا می‌شود؛ داخل یک statement ردیف‌ها باید **کلیدهای متمایز** داشته باشند چون PostgreSQL و SQL Server تغییر یک ردیف را دوبار نمی‌پذیرند. کوئری upsert نمی‌تواند alias جدول داشته باشد. پورت نشد: `update` به شکل عبارت ستونی (`['votes' => DB::raw('votes + 1')]`)؛ از `UpdateAsync` یا `IncrementAsync` استفاده کنید.

**Delete و Truncate.** `DeleteAsync()` تعداد ردیف‌های حذف‌شده را برمی‌گرداند. **به `where` نیاز دارد:** delete بدون آن `InvalidOperationException` می‌دهد تا `where` فراموش‌شده یک جدول را خالی نکند (Laravel اجرایش می‌کند). `DeleteByIdAsync(id, keyColumn: "id")` یک ردیف را با کلید حذف می‌کند؛ برای حذف عمدیِ همهٔ ردیف‌ها اول `DeleteAllAsync()` یا `AllRows()` را صدا بزنید. کوئری delete فقط می‌تواند جدول، join و where داشته باشد. **Joinها:** MySQL و SQL Server می‌نویسند `delete t from t inner join o on ... where ...`، PostgreSQL `delete from t using o where ...` فقط با **inner join** (وگرنه `NotSupportedException`)، و SQLite که delete با join ندارد، `delete from t where rowid in (select t.rowid from t inner join ...)` که جدول `WITHOUT ROWID` را نمی‌پذیرد. `TruncateAsync()` جدول را خالی می‌کند و شمارندهٔ identity را از نو شروع می‌کند: `truncate table` در MySQL و SQL Server، `truncate table ... restart identity` در PostgreSQL، و در SQLite `delete from` به‌علاوهٔ حذف ردیف آن جدول از `sqlite_sequence` در یک تراکنش (اگر پایگاه داده آن جدول را داشته باشد). **PostgreSQL `cascade` نمی‌گیرد** (Laravel اضافه می‌کند): جدولی که کلید خارجی به آن اشاره می‌کند truncate نمی‌شود تا جدول ارجاع‌دهنده بی‌خبر خالی نشود. truncate در بیشتر موتورها DDL است: در MySQL برگشت‌پذیر نیست و MySQL و SQL Server آن را برای جدولِ مورد ارجاع کلید خارجی رد می‌کنند. کوئری truncate فقط جدول ساده می‌پذیرد (بدون alias، `where`، join و limit).

**Returning.** `InsertReturningAsync(row | rows, returning)`، `UpdateReturningAsync(values, returning)` و `DeleteReturningAsync(returning)` مثل شکل ساده‌شان می‌نویسند و ستون‌های خواسته‌شدهٔ هر ردیفِ درگیر را به‌صورت فهرستی از dictionaryها برمی‌گردانند (insert تک‌ردیفی خودِ dictionary را برمی‌گرداند). insert و update ردیف را **بعد از** نوشتن برمی‌گردانند و delete ردیف را با مقدار پیش از حذف. SQLite و PostgreSQL می‌نویسند `returning a, b` بعد از statement؛ SQL Server می‌نویسد `output inserted.a` (برای delete `deleted.a`) داخل آن. **MySQL هیچ‌کدام را ندارد و `NotSupportedException` می‌دهد** پیش از ارسال هر دستوری: از `InsertGetIdAsync` یا select بعدی استفاده کنید. محافظ‌های نوشتن برقرارند (برای update و delete `where` لازم است)، ستون‌ها نام ساده‌اند (بدون پیشوند جدول و بدون alias)، و batch insertی که در سقف پارامتر یا ردیف dialect بیش از یک statement بخواهد `InvalidOperationException` می‌دهد چون ردیف‌ها بین چند statement برنمی‌گردند؛ خودتان آن را بشکنید. SQL Server `output` ساده را روی جدولی با trigger فعال رد می‌کند و SQLite نسخهٔ ۳٫۳۵ یا بالاتر می‌خواهد (provider همراه جدیدتر است). پورت نشد: Laravel روی query builder چیزی به نام returning ندارد، پس این یک افزودهٔ Naravel است.

**Union.** `Union(other)` و `UnionAll(other)` عبارت‌های `union` و `union all` را اضافه می‌کنند؛ `other` کوئری دیگری با همان dialect است و هنگام افزودن **کپی** می‌شود، پس تغییرهای بعدی آن دیده نمی‌شود (Laravel مرجع را نگه می‌دارد). **هرچه بعد از نخستین `Union` صدا بزنید به کل union اعمال می‌شود:** `OrderBy`، `Limit` و `Offset` ردیف‌های ترکیب‌شده را مرتب و برش می‌دهند، در حالی که همین فراخوانی‌ها **پیش از** آن با select اول می‌مانند؛ `Select`، `Where`، join و group که بعداً بیایند هم باز به select اول تعلق دارند. بر اساس نام ستون خروجی مرتب کنید (`OrderBy("name")`)، نه نام با پیشوند جدول. SQLite و SQL Server داخل select مرکب پرانتز و ترتیب نمی‌پذیرند، پس termی که ترتیب، limit یا union خودش را دارد، و کل union وقتی ترتیب یا limit دارد، به‌شکل جدول مشتق نوشته می‌شود (`select * from (... union ...) as naravel_union order by ... limit ...`)، با همان شکل روی هر چهار پایگاه داده؛ مقدارهای bind بین termها به ترتیب متن می‌مانند. در SQL Server limit روی union ترتیب نمی‌خواهد (`select top (n)`) ولی offset می‌خواهد. هر term باید تعداد ستون یکسان و سازگار برگرداند (پایگاه داده بررسی می‌کند). کوئری union خواندنی است: `GetAsync`، `PluckAsync`، `CountAsync` و `ExistsAsync` روی ردیف‌های ترکیب‌شده کار می‌کنند (از طریق جدول مشتق) و `ChunkAsync`/`LazyAsync` با ترتیب هم؛ **نوشتن روی آن ممکن نیست** (insert، update، delete و truncate رد می‌کنند) و `ChunkByIdAsync` و `LazyByIdAsync` هم رد می‌کنند چون فیلتر کلیدشان فقط به select اول می‌رسد. union به‌عنوان منبع `InsertUsingAsync` پذیرفته می‌شود.

**زیرکوئری به‌عنوان منبع.** `FromSub(query, alias)` از `(select ...) as alias` می‌خواند (جدول را جایگزین می‌کند و `Table(...)` دوباره جایش می‌نشیند)؛ `SelectSub(query, alias)` یک ستون زیرکوئری اسکالر `(select ...) as alias` به ستون‌های انتخاب‌شده اضافه می‌کند (اگر ستونی انتخاب نشده بود نتیجه فقط همین ستون است)؛ `JoinSub`، `LeftJoinSub` و `RightJoinSub` یک جدول مشتق را join می‌کنند، با `(alias.col, other)`، `(alias.col, operator, other)` یا callback از نوع `JoinClause` مثل `Join`. زیرکوئری یک `Query` با همان dialect است و هنگام افزودن **کپی** می‌شود؛ alias یک شناسهٔ ساده است (بدون نقطه، بدون `as` و بدون `*`). مقدارهای bind به **ترتیب متن** شماره‌گذاری می‌شوند: اول مقدارهای `SelectSub`، بعد `FromSub`، بعد هر join (زیرکوئری‌اش پیش از مقدارهای `on`)، بعد `where`. شکل‌های where که زیرکوئری می‌گیرند از قبل هستند: `WhereIn(column, callback)`، `WhereExists`، `WhereSubquery(column, op, callback)` و شکل‌های `Or` آن‌ها. **joinهای lateral:** `JoinLateral(query, alias)` و `LeftJoinLateral(query, alias)` زیرکوئری را برای هر ردیفِ جدول‌های قبلی اجرا می‌کنند، پس می‌تواند به آن‌ها اشاره کند (`WhereColumn("posts.user_id", "users.id")`)؛ PostgreSQL می‌نویسد `inner join lateral (...) as alias on true` (برای شکل left، `left join lateral`)، MySQL نسخهٔ **8.0.14 یا بالاتر** همان را، SQL Server `cross apply (...) as alias` (برای شکل left، `outer apply`)، و **SQLite هنگام کامپایل `NotSupportedException` می‌دهد**. کوئری ساخته‌شده با `FromSub` یا با join زیرکوئری خواندنی است ولی **نوشتن روی آن ممکن نیست**: insert، update، delete و truncate آن را رد می‌کنند. پورت نشد: شکل closure این متدها (زیرکوئری را به‌صورت `Query` بسازید).

**Common table expression.** `WithExpression(name, query)` و `WithExpression(name, columns, query)` عبارت `with name (columns) as (select ...)` را پیش از statement می‌نویسند؛ نام را مثل جدول به کار ببرید (`Table(name)`، join، زیرکوئری). چند expression می‌شود اضافه کرد و هرکدام می‌تواند از قبلی‌ها استفاده کند؛ نام‌ها شناسهٔ ساده و در یک query یکتا هستند. `WithRecursiveExpression(name, [columns,] query)` ردیف‌های شروع را می‌گیرد که با `UnionAll` به گامی وصل است که از خود نام select می‌کند، و راه پیمایش درخت یا شمارش همین است: MySQL، PostgreSQL و SQLite می‌نویسند `with recursive` (یک بار، وقتی هر expression بازگشتی باشد)، SQL Server `with` ساده می‌نویسد چون کلیدواژهٔ `recursive` ندارد و `UnionAll` می‌خواهد. query از نوع `Query` با همان dialect است و هنگام افزودن **کپی** می‌شود؛ مقدارهای bind به ترتیب متن شماره‌گذاری می‌شوند و expressionها اول می‌آیند. **expressionها را روی بیرونی‌ترین query بگذارید:** SQL Server `with` را جای دیگر نمی‌پذیرد. count یا exists آن‌ها را به ابتدای statement خودش می‌برد، `Union` کل آن‌ها را می‌پوشاند، termی از `Union` که expression خودش را دارد به‌شکل جدول مشتق نوشته می‌شود، و insert، update، delete و منبع `InsertUsingAsync` آن‌ها را رد می‌کنند چون جای `with` بین dialectها و statementها فرق دارد. builder عبارت `option (maxrecursion ...)` را در SQL Server نمی‌نویسد: بازگشتِ بدون پایان خودش در سقف ۱۰۰ سطحِ آن موتور قطع می‌شود. MySQL نسخهٔ ۸ یا بالاتر می‌خواهد. پورت نشد: Laravel در خود query builder متد CTE ندارد (از بسته می‌آید)، پس این‌ها افزودهٔ Naravel هستند.

**SQL خام.** متن SQL فقط از دو راه وارد query می‌شود و جای دیگر نه: متدهای `*Raw` و مقدار `Raw`. متدهای `*Raw` عبارت‌اند از `SelectRaw`، `WhereRaw`/`OrWhereRaw`، `HavingRaw`/`OrHavingRaw`، `OrderByRaw`، `GroupByRaw` و `FromRaw`؛ هرکدام متن را در پارامتری به نام `sql` و مقدارها را در `bindings` می‌گیرند، که هر `?` به ترتیب با یک پارامتر bind جایگزین می‌شود و `??` یک علامت سؤال ساده می‌نویسد. تعداد ناهمخوان `ArgumentException` می‌دهد. `FromRaw(sql, bindings)` متن را بعد از `from` می‌نویسد (تابع جدولی، جدول مشتق با alias خودش) و جدول را جایگزین می‌کند؛ query ساخته‌شده با آن خواندنی است ولی نوشتن روی آن ممکن نیست. `new Raw(sql, bindings)` همان متن است که **به‌عنوان مقدار** به کار می‌رود: `Where("votes", ">", new Raw("avg_votes + ?", 1))`، مقدار update مثل `["votes"] = new Raw("votes + ?", 1)` (معادل `DB::raw` در Laravel)، مقدار insert، کران `between`. bindingهایش هم پارامتر هستند و ترتیب متن را حفظ می‌کنند؛ مقدار خام در insert دسته‌ای هنگام شکستن batch یک پارامتر حساب می‌شود، پس مقدار خامی با چند binding می‌تواند statement را از سقف dialect رد کند که با `InvalidOperationException` گزارش می‌شود. هر رشتهٔ دیگری که متدی می‌گیرد یا **مقدار** است (همیشه bind می‌شود) یا **شناسه** (با دستور زبان ثابت اعتبارسنجی و quote می‌شود)؛ عملگر و جهت از فهرست ثابت می‌آیند. یک تست API عمومی را با reflection می‌خواند و اگر پارامتر رشته‌ای با نامی غیر از نام‌های شناخته‌شدهٔ مقدار و شناسه پیدا شود، یا متن SQL از متدی غیر `*Raw` وارد شود، شکست می‌خورد. متن هرگز بین dialectها ترجمه نمی‌شود و **هرگز نباید از ورودی کاربر ساخته شود**: مقدارهای کاربر را به‌صورت binding بدهید.

**اشکال‌زدایی.** `ToSql()` متن SQL select را با نشانگرهای `@p0`، `@p1`، ... برمی‌گرداند، دقیقاً همان چیزی که query می‌فرستد؛ `GetBindings()` مقدارها را به ترتیب نشانگر می‌دهد (اندیس همان شمارهٔ نشانگر است)؛ هر دو در هر فراخوانی دوباره کامپایل می‌کنند و چیزی را تغییر نمی‌دهند. `ToRawSql()` هر مقدار را به‌شکل literal با escape مخصوص dialect داخل متن می‌نویسد (دو برابر کردن quote، دو برابر کردن backslash در MySQL، `N'...'` در SQL Server، باینری به‌صورت هگزادسیمال، تاریخ ISO، boolean مطابق dialect، و `NULL`) و **فقط برای لاگ و اشکال‌زدایی است: هرگز اجرایش نکنید و از آن دستور نسازید**؛ آنچه اجرا می‌شود همیشه متن پارامتری است و یک تست تضمین می‌کند SQL اجراشده با `ToSql()` برابر است. literalها نمایش وفادارانه‌اند، نه تضمین اینکه هر پایگاه داده آن‌ها را دقیقاً همان‌طور بخواند (جزئیات اعشاری و منطقهٔ زمانی فرق می‌کند). فقط select را می‌توان این‌طور نشان داد؛ متدهای نوشتن هنگام اجرا کامپایل می‌شوند و SQL آن‌ها در query log و در `DatabaseFake.Executed` است. **متن خام نباید `@p` و بعد رقم داشته باشد** (`@p0`، `@p12`)، یعنی نام‌هایی که builder به پارامترهای خودش می‌دهد: چنین متنی ناخواسته به مقدار تولیدشده bind می‌شود یا `ToRawSql` آن را بازنویسی می‌کند، پس هر متد `*Raw` و `Raw` آن را با `ArgumentException` رد می‌کنند؛ به‌جایش `?` بنویسید (نام‌های دیگر با `@` مثل `@param` اشکالی ندارند). `dump`، `dd`، `dumpRawSql` و `ddRawSql` در Laravel پورت نشدند (چاپ می‌کنند و درخواست PHP را متوقف می‌کنند): از `ToRawSql()`، query log یا `DatabaseFake` استفاده کنید.

**قفل‌ها.** `SharedLock()` (معادل `sharedLock` لاراول) و `LockForUpdate()` (معادل `lockForUpdate`) باعث می‌شوند select ردیف‌هایی را که می‌خواند قفل کند تا «خواندن و سپس نوشتن» در یک تراکنش با تراکنش دیگر رقابت نکند. MySQL (نسخهٔ 8.0 یا بالاتر؛ MariaDB پشتیبانی نمی‌شود) و PostgreSQL در **انتهای** select، بعد از ترتیب و limit، عبارت `for share` / `for update` را می‌نویسند؛ SQL Server چنین عبارتی ندارد و بلافاصله بعد از جدول و alias آن یک **table hint** می‌نویسد: `with (rowlock, holdlock)` برای قفل اشتراکی و `with (rowlock, updlock, holdlock)` برای قفل به‌روزرسانی، همان چیزی که لاراول هم می‌نویسد. **قفل تا پایان تراکنش زنده است**، پس آن را داخل تراکنش به کار ببرید: بیرون از تراکنش هر پایگاه داده قفل را با پایان همان دستور آزاد می‌کند و چیزی را محافظت نمی‌کند (API تراکنش با R25 می‌آید؛ به همین دلیل مجموعهٔ contract مشترک فقط ثابت می‌کند که هر پایگاه داده این SQL را می‌پذیرد و ردیف‌ها را برمی‌گرداند). آخرین فراخوانی برنده است، `Clone()` قفل را کپی می‌کند، قفل مقدار bind‌شدهٔ تازه‌ای اضافه نمی‌کند و قفلِ داخل یک زیرپرس‌وجو (`FromSub`، `WhereIn(..., s => ...)`، `JoinSub`) همان‌جا می‌ماند. این موارد **به‌جای نادیده گرفته شدن بی‌صدا** رد می‌شوند: SQLite قفل ردیفی ندارد، پس query قفل‌دار هنگام کامپایل `NotSupportedException` با نام دیالکت می‌دهد؛ قفل روی query دارای `Union`/`UnionAll` یا روی یکی از جمله‌های آن، و قفل روی نوشتن (insert، update، delete، truncate، upsert و returning) `InvalidOperationException` می‌دهد؛ SQL Server برای قفل روی منبع `FromSub` یا `FromRaw` هم `NotSupportedException` می‌دهد، چون جدول مشتق hint نمی‌پذیرد. جایی که قفل می‌نشیند قاعدهٔ پایگاه داده است، نه سازندهٔ query: MySQL و PostgreSQL ردیف‌های **همهٔ** جدول‌هایی را که select می‌خواند قفل می‌کنند، SQL Server فقط جدول `Table(...)` را (هرگز جدول join‌شده را)، و PostgreSQL قفل را همراه تجمیع (پس با `CountAsync` نه)، `Distinct`، `GroupBy` یا سمت nullable یک outer join نمی‌پذیرد. پورت نشده: `lock('for update skip locked')` لاراول (یک رشتهٔ عبارت خام؛ `skip locked` و `nowait` ارائه نمی‌شوند)؛ ردیف‌های لازم را با `where` قفل کنید یا SQL خام را از طریق `IDatabase` اجرا کنید.

**قواعد ایمنی، در لحظهٔ فراخوانی اعمال می‌شوند:**
- مقدارها **همیشه** پارامتر هستند (`@p0`، `@p1`، ...) روی هر دیالکت. هیچ چیزی که به‌عنوان مقدار داده شود نمی‌تواند SQL شود. `ToRawSql()` مقدارها را فقط برای لاگ داخل متن می‌گذارد؛ هرگز اجرایش نکنید.
- شناسه‌ها (جدول، ستون، alias) باید با `[حرف یا _][حرف، رقم، _ یا $]*` بخوانند، حداکثر ۱۲۸ نویسه، در شکل‌های `col`، `table.col`، `schema.table.col`، `*`، `table.*` و `source as alias`. نام فارسی کار می‌کند. هر چیز دیگر فوراً `ArgumentException` می‌دهد. این گرامر با PDR-022 الف۳ قفل شده؛ شل‌کردنش اصلاحیهٔ PDR می‌خواهد.
- عملگرها از یک فهرست مجاز می‌آیند. عملگری که دیالکت دیگری دارد (مثلاً `ilike` روی SQLite) به‌جای تولید SQL غلط `NotSupportedException` می‌دهد.
- متن SQL فقط از متدهای `*Raw` ممکن است (`?` یک پارامتر bind‌شده و `??` یک علامت سؤال لفظی است)، پس کد ناامن به‌راحتی جستجو می‌شود.
- هر دستور با حد پارامتر دیالکت سنجیده می‌شود (SQL Server ‏۲۰۹۸، SQLite ‏۳۲۷۶۶، MySQL و PostgreSQL ‏۶۵۵۳۵). عبور از حد `InvalidOperationException` با نام دیالکت، حد و تعداد می‌دهد.

**تفاوت‌های دیالکت که خودکار مدیریت می‌شوند:** quoting (`"x"` و `` `x` `` و `[x]`)، `limit`/`offset` در برابر `top (n)` و `offset ... fetch` در SQL Server (وقتی `Offset` هست، SQL Server `OrderBy` می‌خواهد و خطا همین را می‌گوید)، و offset بدون limit (`limit -1` در SQLite و یک limit بیشینه در MySQL).

## چگونه کار می‌کند و چرا

**چرا همه‌جا یک سبک `@pN`.** درایورهای SQLite، MySQL، PostgreSQL و SQL Server همه پارامتر نام‌دار می‌پذیرند و Dapper با نام bind می‌کند؛ پس یک سبک، هر چهار grammar را یکسان و هر تست را مقایسهٔ ساده‌ی رشته نگه می‌دارد. سبک `?` موقعیتی برای هر درایور نشانگر جدا می‌خواست.

**چرا فهرست بزرگ `IN` شکسته نمی‌شود.** راه‌حل بدیهی (چند گروه `IN` با `OR`) تعداد پارامتر را کم نمی‌کند، پس دستور باز هم شکست می‌خورد. builder به‌جای آن خطای روشن می‌دهد؛ راهبرد تک‌پارامتری در PDR-022 الف۷ پیشنهاد شده است.

**چرا `Raw` فعلاً پسوند متد است، نه نوع.** هر راه فرار خام یک متد `*Raw` است. نوع *مقدار* خام (مثل `DB::raw` داخل `update` در Laravel) فقط برای نوشتن لازم است و با آن می‌آید.

## چگونه درز اتصال و درایورها کار می‌کنند

**درز اتصال (PDR-022 A1).** کوئری‌ها هرگز خودشان اتصال باز نمی‌کنند؛ از `IConnectionScope` یک lease می‌گیرند. امروز scope یک pool ساده است (برای هر lease یک اتصال pool‌شدهٔ تازه، بدون تراکنش، با Dispose بسته می‌شود). تراکنش‌ها (scope محیطی و اشتراک با EF Core) بعداً با ثبت یک scope دیگر اضافه می‌شوند، **بدون تغییر هیچ امضایی**. دلیل ساختن اول همین است: سازنده‌ای که خودش اتصال باز کند، بعداً بدون breaking change نمی‌تواند به یک تراکنش بپیوندد.

**چرا `Database:Connections` و نه `Stores`.** مثل `config/database.php` در Laravel خوانده می‌شود. در داخل همان دیکشنری‌ای است که `Manager` مشترک می‌خواند، پس `Extend`، reload و پیام‌های خطا دقیقاً مثل Cache، Queue و Mail رفتار می‌کنند.

**SQLite در حافظه.** SQLite وقتی آخرین اتصال بسته شود دیتابیس حافظه‌ای را حذف می‌کند و سازنده برای هر عملیات یک اتصال باز می‌کند. پس درایور `:memory:` را به یک دیتابیس shared-cache با نام یکتا تبدیل می‌کند و تا Dispose شدن درایور یک اتصال را باز نگه می‌دارد. بدون این، تستی که جدول می‌سازد و بعد کوئری می‌زند، دیتابیس خالی می‌دید.

**امنیت بستهٔ درایور.** `Microsoft.Data.Sqlite` بستهٔ `SQLitePCLRaw.lib.e_sqlite3` نسخهٔ 2.1.11 را می‌آورد که advisory با شناسهٔ CVE-2025-6965 دارد. مخزن نسخهٔ اصلاح‌شدهٔ 2.1.13 را مستقیم pin می‌کند، چون build هشدارها را خطا حساب می‌کند.

## درایورهای سرور

| بسته | مقدار `Driver` | provider | ثبت |
|---|---|---|---|
| `Naravel.Database.Sqlite` | `sqlite` | Microsoft.Data.Sqlite | `AddNaravelSqliteDatabase` |
| `Naravel.Database.MySql` | `mysql` | MySqlConnector | `AddNaravelMySqlDatabase` |
| `Naravel.Database.Postgres` | `pgsql` | Npgsql | `AddNaravelPostgresDatabase` |
| `Naravel.Database.SqlServer` | `sqlserver` | Microsoft.Data.SqlClient | `AddNaravelSqlServerDatabase` |

```json
{ "Database": { "Default": "main", "Connections": {
  "main": { "Driver": "pgsql", "ConnectionString": "Host=localhost;Database=app;Username=app;Password=..." },
  "reports": { "Driver": "mysql", "ConnectionString": "Server=localhost;Database=reports;User ID=app;Password=..." } } } }
```

فقط بسته‌هایی را نصب کنید که استفاده می‌کنید، هر کدام را بعد از `AddNaravelDatabase` ثبت کنید و نام اتصال‌ها را آزادانه بگذارید: یک برنامه می‌تواند هم‌زمان چند دیتابیس داشته باشد (`db.Table("t", "reports")`). رمز را بیرون از کد نگه دارید: متغیر محیطی `Database__Connections__main__ConnectionString` را بگذارید. pooling کار provider است و lease که Dispose شود اتصال را به pool برمی‌گرداند.

### رشتهٔ اتصال

| درایور | نمونه | نکته |
|---|---|---|
| `sqlite` | `Path` = `app.db` یا `:memory:` | مسیر فایل است، نه رشتهٔ اتصال. `:memory:` به یک پایگاه داده درون‌حافظهٔ مشترک تبدیل می‌شود که تا زنده بودن درایور می‌ماند. |
| `mysql` | `Server=localhost;Port=3306;Database=app;User ID=app;Password=...` | MySqlConnector. روی MySQL 8 بدون TLS مقدار `AllowPublicKeyRetrieval=true` را اضافه کنید (فقط محلی و CI). |
| `pgsql` | `Host=localhost;Port=5432;Database=app;Username=app;Password=...` | Npgsql. |
| `sqlserver` | `Server=localhost,1433;Database=app;User Id=app;Password=...;TrustServerCertificate=true` | Microsoft.Data.SqlClient. `TrustServerCertificate=true` برای سرور محلی یا تست با گواهی خودامضاست؛ روی production استفاده نکنید. |

### ماتریس dialect

یک فراخوانی یکسان builder روی هر پایگاه داده متفاوت کامپایل می‌شود. هر چه پایین آمده با suite قرارداد روی هر چهار پایگاه داده اثبات شده است.

| | SQLite | MySQL | PostgreSQL | SQL Server |
|---|---|---|---|---|
| نقل‌قول شناسه | `"name"` | `` `name` `` | `"name"` | `[name]` |
| نشانگر پارامتر | `@p0` | `@p0` | `@p0` | `@p0` |
| `Limit(n)` | `limit n` | `limit n` | `limit n` | `top (n)`، یا با `Offset` به‌شکل `fetch next n rows only` |
| `Offset` بدون `Limit` | `limit -1 offset n` | `limit 18446744073709551615 offset n` | `offset n` | `offset n rows`، نیاز به `OrderBy` |
| بولین در `ToRawSql` | `1` / `0` | `1` / `0` | `true` / `false` | `1` / `0` |
| بیشترین پارامتر در هر دستور | 32766 | 65535 | 65535 | 2098 (حد واقعی 2100) |
| عملگرهای اضافه | ندارد | `<=>`، `regexp`، `rlike` و شکل‌های `not` | `ilike`، `~`، `~*`، `!~`، `!~*`، `similar to` و شکل‌های `not` | ندارد |

عملگری که فقط در یک dialect هست روی بقیه `NotSupportedException` می‌دهد، نه SQLی که در زمان اجرا خطا بدهد. `Offset` روی SQL Server بدون `OrderBy` یک `InvalidOperationException` می‌دهد، چون `OFFSET ... FETCH` به `ORDER BY` نیاز دارد.

### ماتریس دیالکت R21

خانوادهٔ where، join، گروه‌بندی و کمک‌های ترتیب روی هر پایگاه داده چه می‌کنند. `yes` یعنی همان فراخوانی همه‌جا کامپایل و اجرا می‌شود؛ خانه‌ای که `throws` دارد هنگام کامپایل `NotSupportedException` با نام دیالکت می‌دهد. فایل `tests/Naravel.Database.Tests/DialectMatrixTests.cs` همین جدول را assert می‌کند: هر دو را با هم تغییر دهید.

| قابلیت | SQLite | MySQL | PostgreSQL | SQL Server |
|---|---|---|---|---|
| `WhereLike` / `WhereNotLike` | yes (بدون حساسیت به حروف ASCII) | yes | yes (`ilike`) | yes |
| `WhereLike` با حساسیت به حروف | throws | `like binary` | `like` | `collate SQL_Latin1_General_CP1_CS_AS` |
| `WhereFullText` | throws | yes، ایندکس متن کامل می‌خواهد | yes، پیکربندی `english` | yes، ایندکس متن کامل می‌خواهد |
| `WhereJsonContains` مقدار اسکالر در آرایه | yes | yes | yes | yes |
| `WhereJsonContains` مقدار آرایه یا شیء | throws | yes | yes | throws |
| `WhereJsonContainsKey`، `WhereJsonLength` | yes | yes | yes (طول آرایه) | yes |
| ستون مسیر JSON (`data->a->b[0]`) در where | yes | yes (بولی JSON مقایسه نمی‌شود) | yes (بسته به مقدار مقایسه به numeric/boolean cast می‌شود) | yes |
| `WhereDate`، `WhereMonth`، `WhereDay`، `WhereYear`، `WhereTime` | yes | yes | yes | yes |
| `WhereAny`، `WhereAll`، `WhereNone`، `WhereBetween`، `WhereColumn` | yes | yes | yes | yes |
| `WhereExists`، where با زیرکوئری، `WhereIn` با callback | yes | yes | yes | yes (داخلش `top (n)`) |
| `Join`، `LeftJoin`، `CrossJoin` | yes | yes | yes | yes |
| `RightJoin` | yes، SQLite نسخهٔ 3.39 یا بالاتر | yes | yes | yes |
| `GroupBy`، `Having` روی ستون‌های گروه‌بندی‌شده | yes | yes | yes | yes |
| `Having` روی alias بخش select | yes | yes | no (قاعدهٔ پایگاه داده) | no (قاعدهٔ پایگاه داده) |
| `Latest`، `Oldest`، `Reorder` | yes | yes | yes | yes |
| `InRandomOrder` | `random()` | `rand()` | `random()` | `newid()` |

### ماتریس دیالکت R24

فراخوانی‌های R24 روی هر پایگاه داده چه می‌کنند. خانه‌ای که `throws` دارد، استثنای نام‌برده را هنگام کامپایل query (و برای نوشتن‌ها هنگام ساخت دستور) می‌دهد؛ فایل `tests/Naravel.Database.Tests/DialectMatrixTests.cs` خانه‌های `NotSupportedException` را بررسی می‌کند: هر دو را با هم تغییر دهید.

| قابلیت | SQLite | MySQL | PostgreSQL | SQL Server |
|---|---|---|---|---|
| `SharedLock` | `NotSupportedException` می‌دهد | `for share` (8.0 یا بالاتر) | `for share` | table hint `with (rowlock, holdlock)` |
| `LockForUpdate` | `NotSupportedException` می‌دهد | `for update` | `for update` | table hint `with (rowlock, updlock, holdlock)` |
| قفل چه چیزی را می‌پوشاند | - | همهٔ جدول‌هایی که select می‌خواند | همهٔ جدول‌هایی که select می‌خواند | فقط جدول `Table(...)` |
| قفل روی منبع `FromSub` یا `FromRaw` | `NotSupportedException` می‌دهد | بله | بله | `NotSupportedException` می‌دهد |
| قفل روی query دارای `Union` یا روی یک نوشتن | `InvalidOperationException` می‌دهد | `InvalidOperationException` می‌دهد | `InvalidOperationException` می‌دهد | `InvalidOperationException` می‌دهد |
| قفل همراه تجمیع، `Distinct` یا `GroupBy` | - | سازنده اجازه می‌دهد | خطای پایگاه داده | سازنده اجازه می‌دهد |

### نوع‌ها

پارامترها با مقدار و، جایی که providerها اختلاف دارند، با نوع صریح bind می‌شوند. مقدارهای تکیِ خوانده‌شده (`ValueAsync<T>`، `ScalarAsync<T>`، `PluckAsync<T>` و `GetAsync<T>` برای `T` ساده) را خود Naravel تبدیل می‌کند، پس یک فراخوانی روی هر پایگاه داده همان مقدار CLR را می‌دهد:

| نوع CLR | نکته |
|---|---|
| `bool` | از `bit`، `tinyint(1)`، `boolean` و عدد صحیح SQLite خوانده می‌شود. |
| `int`، `long`، `double`، `decimal`، `string` | پهنای عدد تبدیل می‌شود، پس `ScalarAsync<long>("select count(*) ...")` روی همه کار می‌کند. |
| `Guid` | `char(36)` در MySQL، `uuid` در PostgreSQL، `uniqueidentifier` در SQL Server، متن در SQLite. |
| `DateTime` | در SQL Server به‌شکل `datetime2` bind می‌شود. `timestamp` در PostgreSQL نوع غیر UTC می‌خواهد و `timestamptz` نوع UTC. |
| `DateTimeOffset` | **در MySQL موجود نیست** (چنین نوع ستونی ندارد؛ `DateTime` با UTC ذخیره کنید). PostgreSQL فقط offset صفر می‌پذیرد. `DateTime` خوانده‌شده از ستون آگاه از منطقهٔ زمانی UTC فرض می‌شود. |
| `DateOnly`، `TimeOnly` | روی هر سرور بومی‌اند؛ در SQLite متن. |
| `byte[]` | `blob`، `varbinary`، `bytea`. |
| enum | به‌شکل عدد پایه نوشته می‌شود؛ با `ValueAsync<MyEnum>` یا به‌شکل عدد خوانده می‌شود. |
| `null` | به‌شکل `NULL` نوشته و به‌شکل `null` خوانده می‌شود (برای نوع مقداری در `ValueAsync<T>` مقدار پیش‌فرض). |

نگاشت به کلاس خودتان (`GetAsync<MyRow>()`) با Dapper انجام می‌شود.

### اجرای تست‌های پایگاه داده

CI، suite قرارداد را در job جدای `database` اجرا می‌کند: برای هر سرور یک leg موازی (کانتینر موقت با تنظیمات مخصوص تست) که فقط trait `Dialect` خودش را اجرا می‌کند. روی `main`، تگ‌های نسخه و اجرای شبانه همیشه اجرا می‌شود، و روی pull request فقط وقتی کد پایگاه داده، تست‌ها، کد مشترک تست، نسخهٔ بسته‌ها، فایل compose یا workflow تغییر کرده باشد. اگر تست‌های یک leg skip شوند آن leg شکست می‌خورد، پس نبودن رشتهٔ اتصال بی‌صدا رد نمی‌شود. اولین اجرا حدود ۴۵ ثانیه برای هر leg اندازه گرفت، خیلی کمتر از بودجهٔ پنج‌دقیقه‌ای PDR-022 A6.

محلی، `scripts/test-local.sh --db` (یا `test-local.ps1 -Db`) همان سه سرور را از `docker-compose.yml` بالا می‌آورد (SQL Server حدود ۲ گیگابایت RAM می‌خواهد) و `NARAVEL_TEST_MYSQL`، `NARAVEL_TEST_POSTGRES` و `NARAVEL_TEST_SQLSERVER` را تنظیم می‌کند. بدون این متغیرها تست‌های سرور skip می‌شوند، یعنی **اجرا نشده‌اند**، و فقط SQLite بررسی می‌شود. برای یک dialect: `scripts/test-local.sh --db -- --filter "Dialect=Postgres"`.

## عمداً پورت نشده

- هدایت `DB::__call` به اتصال: به‌جای آن `IDatabase` (یا scope) را inject کنید.
- `DB::select` که شیء `stdClass` برمی‌گرداند: ردیف‌ها dictionary یا نوع map‌شده با Dapper هستند. `pretend()` با `DatabaseFake` و `listen()` با `IQueryListener` جایگزین شده است.
- اتصال‌های جداشدهٔ خواندن/نوشتن، `purge()` و `reconnect()`: اتصال‌ها را provider در pool نگه می‌دارد و با تغییر پیکربندی بازسازی می‌شوند.
