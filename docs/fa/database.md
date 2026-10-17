# پایگاه داده (Query Builder)

`Naravel.Database` یک query builder به سبک Laravel برای .NET است: اتصال‌های نام‌دار، سازندهٔ روان (fluent) و SQL با پارامتر برای SQLite، MySQL، PostgreSQL و SQL Server. EF Core همچنان ابزار موجودیت‌ها و migration است؛ این بسته برای مسیر «فقط یک کوئری اجرا کن» است، با Dapper برای اجرای سریع و **بدون SqlKata**.

تصمیم‌ها: [PDR-022](../pdr/fa/PDR-022-database-query-builder.md). نسخهٔ انگلیسی: [docs/en/database.md](../en/database.md).

> **آنچه امروز وجود دارد.** فقط پایه آماده است: اتصال‌ها، manager، درایور SQLite و درز اتصال (connection seam). سازندهٔ روان `Query` (مثل `Table("users").Where(...).GetAsync()`) در taskهای بعدیِ همین مرحله می‌آید؛ برنامهٔ متد‌به‌متد در [roadmap/QUERY-BUILDER-COVERAGE.md](../../roadmap/QUERY-BUILDER-COVERAGE.md) است. هیچ چیزِ دیگری ادعا نمی‌شود.

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

## چگونه کار می‌کند و چرا

**درز اتصال (PDR-022 A1).** کوئری‌ها هرگز خودشان اتصال باز نمی‌کنند؛ از `IConnectionScope` یک lease می‌گیرند. امروز scope یک pool ساده است (برای هر lease یک اتصال pool‌شدهٔ تازه، بدون تراکنش، با Dispose بسته می‌شود). تراکنش‌ها (scope محیطی و اشتراک با EF Core) بعداً با ثبت یک scope دیگر اضافه می‌شوند، **بدون تغییر هیچ امضایی**. دلیل ساختن اول همین است: سازنده‌ای که خودش اتصال باز کند، بعداً بدون breaking change نمی‌تواند به یک تراکنش بپیوندد.

**چرا `Database:Connections` و نه `Stores`.** مثل `config/database.php` در Laravel خوانده می‌شود. در داخل همان دیکشنری‌ای است که `Manager` مشترک می‌خواند، پس `Extend`، reload و پیام‌های خطا دقیقاً مثل Cache، Queue و Mail رفتار می‌کنند.

**SQLite در حافظه.** SQLite وقتی آخرین اتصال بسته شود دیتابیس حافظه‌ای را حذف می‌کند و سازنده برای هر عملیات یک اتصال باز می‌کند. پس درایور `:memory:` را به یک دیتابیس shared-cache با نام یکتا تبدیل می‌کند و تا Dispose شدن درایور یک اتصال را باز نگه می‌دارد. بدون این، تستی که جدول می‌سازد و بعد کوئری می‌زند، دیتابیس خالی می‌دید.

**امنیت بستهٔ درایور.** `Microsoft.Data.Sqlite` بستهٔ `SQLitePCLRaw.lib.e_sqlite3` نسخهٔ 2.1.11 را می‌آورد که advisory با شناسهٔ CVE-2025-6965 دارد. مخزن نسخهٔ اصلاح‌شدهٔ 2.1.13 را مستقیم pin می‌کند، چون build هشدارها را خطا حساب می‌کند.

## عمداً پورت نشده

- هدایت `DB::__call` به اتصال: به‌جای آن scope (و از task اجرا به بعد `IDatabase`) را inject کنید.
- اتصال‌های جداشدهٔ خواندن/نوشتن، `purge()` و `reconnect()`: اتصال‌ها را provider در pool نگه می‌دارد و با تغییر پیکربندی بازسازی می‌شوند.
