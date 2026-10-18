# پایگاه داده (Query Builder)

`Naravel.Database` یک query builder به سبک Laravel برای .NET است: اتصال‌های نام‌دار، سازندهٔ روان (fluent) و SQL با پارامتر برای SQLite، MySQL، PostgreSQL و SQL Server. EF Core همچنان ابزار موجودیت‌ها و migration است؛ این بسته برای مسیر «فقط یک کوئری اجرا کن» است، با Dapper برای اجرای سریع و **بدون SqlKata**.

تصمیم‌ها: [PDR-022](../pdr/fa/PDR-022-database-query-builder.md). نسخهٔ انگلیسی: [docs/en/database.md](../en/database.md).

> **آنچه امروز وجود دارد.** اتصال‌ها، manager، درایور SQLite، درز اتصال و query builder که برای هر چهار دیالکت SQL را **کامپایل** می‌کند (`ToSql`، `ToRawSql`، `GetBindings`)، و **اجرا** روی SQLite (`GetAsync`، `FirstAsync`، `SoleAsync`، `ValueAsync`، `FindAsync`، `PluckAsync` و SQL خام از طریق `IDatabase`). قابلیت مشاهده (meter، activity، `IQueryListener`، query log اختیاری) و fake تست یعنی `DatabaseFake` هم کار می‌کنند؛ join، گروه‌بندی و خانوادهٔ where گسترده بعد از آن. برنامهٔ متد‌به‌متد در [roadmap/QUERY-BUILDER-COVERAGE.md](../../roadmap/QUERY-BUILDER-COVERAGE.md) است. هیچ چیزِ دیگری ادعا نمی‌شود.

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

موجود امروز: `Table`، `Select`، `AddSelect`، `SelectRaw`، `Distinct`، `Where`/`OrWhere`/`WhereNot`/`OrWhereNot` (ستون و مقدار، با عملگر، آرایهٔ جفت‌ها یا گروه closure)، `WhereRaw`/`OrWhereRaw`، `WhereIn`/`WhereNotIn` و شکل‌های `or` آن‌ها، `OrderBy`/`OrderByDesc`/`OrderByRaw`، `Limit`/`Take`، `Offset`/`Skip`، `Clone`، `Compile`، `ToSql`، `ToRawSql`، `GetBindings`.

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

**وضعیت:** این درایورها نوشته شده‌اند و ثبت، reload و `Extend` آن‌ها تست واحد دارد. اجرای کوئری واقعی روی هر سرور را مجموعهٔ contract مشترکِ task بعدی و job پایگاه‌دادهٔ CI اثبات می‌کنند که هنوز وجود ندارند؛ پس اجرای MySQL، PostgreSQL و SQL Server را تا آن زمان **هنوز راستی‌آزمایی‌نشده** بدانید. واقعیت‌های هر سرور: SQL Server هر دستور را تا ۲۱۰۰ پارامتر می‌پذیرد (grammar روی ۲۰۹۸ می‌ایستد) و با `Offset` به `OrderBy` نیاز دارد؛ PostgreSQL عملگرهای `ilike` و regex دارد، MySQL عملگرهای `<=>` و `regexp` و `rlike`، و استفادهٔ هر کدام روی دیالکت دیگر `NotSupportedException` می‌دهد.

## عمداً پورت نشده

- هدایت `DB::__call` به اتصال: به‌جای آن `IDatabase` (یا scope) را inject کنید.
- `DB::select` که شیء `stdClass` برمی‌گرداند: ردیف‌ها dictionary یا نوع map‌شده با Dapper هستند. `pretend()` با `DatabaseFake` و `listen()` با `IQueryListener` جایگزین شده است.
- اتصال‌های جداشدهٔ خواندن/نوشتن، `purge()` و `reconnect()`: اتصال‌ها را provider در pool نگه می‌دارد و با تغییر پیکربندی بازسازی می‌شوند.
