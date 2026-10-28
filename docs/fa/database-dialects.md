# تفاوت دیالکت‌های پایگاه داده (قرارداد)

یک فراخوانی query builder **تضمین نمی‌شود** روی همهٔ موتورها یک معنی داشته باشد. این صفحه قرارداد عمومی تفاوت‌هاست. رفتاری که اینجا نیامده یا تست ندارد را قابل‌حمل فرض نکنید.

نسخهٔ انگلیسی (مرجع جدول کامل): [docs/en/database-dialects.md](../en/database-dialects.md).

## سطح شواهد

- **Executed:** سناریویی در `ContractScenarios.cs` در CI روی سرور واقعی اجرا می‌شود.
- **Compile-only:** فقط SQL تولید و بررسی می‌شود، روی آن موتور اجرا نمی‌شود (مثلاً full-text در SQL Server).
- **Refused:** فراخوانی روی آن موتور استثنای مستند پرتاب می‌کند.

## یک فراخوانی، معنی‌های متفاوت

- `UpsertAsync`: در MySQL پارامتر `uniqueBy` نادیده گرفته می‌شود و **هر کلید یکتای جدول** باعث به‌روزرسانی می‌شود (و ردیف به‌روزشده دو بار شمرده می‌شود)؛ SQLite و PostgreSQL به ایندکس یکتا روی `uniqueBy` نیاز دارند؛ SQL Server از `merge ... with (holdlock)` استفاده می‌کند و برخورد با کلید یکتای دیگر خطا می‌دهد.
- `InsertOrIgnoreAsync`: در SQL Server `NotSupportedException`.
- `*ReturningAsync`: در MySQL `NotSupportedException`؛ SQLite و PostgreSQL `returning`، SQL Server `output inserted`؛ روی دسته‌های تقسیم‌شده `InvalidOperationException`.
- `UpdateOrInsertAsync`: دو دستور است و **اتمیک نیست**.
- قفل ردیف: در SQLite رد می‌شود. `WhereFullText`: در SQLite رد می‌شود؛ در SQL Server فقط compile-only.
- JSON contains برای آرایه، شیء، `Guid`، `DateTime`، `DateTimeOffset`، `char` یا enum: در SQLite و SQL Server `NotSupportedException`؛ مقدار `null` روی هر موتور `ArgumentNullException`؛ مقایسهٔ اسکالر در SQLite با نوع (`1` با `"1"` یکی نیست) و در SQL Server به‌صورت متن (`"1"` می‌تواند با `1` یکی شود). مقایسهٔ بولین JSON و `DateTimeOffset` در MySQL پشتیبانی کامل ندارد.
- `Having` روی نام مستعار select: PostgreSQL و SQL Server (خودِ موتور) نمی‌پذیرند.
- حساسیت `WhereLike` به حروف: در SQLite با سه موتور دیگر فرق دارد (سناریوی حساس به حروف روی SQLite اجرا نمی‌شود).

## قرارداد خطا (روی هر دیالکت، بدون اتصال)

عملگر ناشناس یا شبیه SQL: `ArgumentException`؛ عملگر مختص دیالکت دیگر: `NotSupportedException`؛ جهت مرتب‌سازی نامعتبر: `ArgumentException`؛ نام ستون/جدول مخرب: `ArgumentException` پیش از ارسال هر دستور؛ update/delete بدون `where`: `InvalidOperationException`؛ `WhereIn` با رشته: `ArgumentException`؛ کران null در `WhereBetween`: `ArgumentNullException`؛ `WhereIn` خالی: SQL معتبر (هرگز `in ()`)؛ هر مقدار همیشه binding است، نه بخشی از متن SQL.

## فهرست «اجرا نمی‌شود روی»

جدول کامل سناریوهایی که مجموعهٔ مشترک روی یک موتور رد می‌کند در [نسخهٔ انگلیسی](../en/database-dialects.md#not-run-on-ledger) است؛ هر سناریوی دیگر روی هر چهار موتور اجرا می‌شود.

## «سازگار با Laravel» یعنی چه

ادعا به **زیرمجموعهٔ تست‌شده** محدود است: متدهای فهرست پوشش، با رفتاری که مجموعهٔ قرارداد روی موتورهای بالا اجرا می‌کند، به‌علاوهٔ انحراف‌های عمدی (ردیف‌ها دیکشنری یا Dapper، بدون facade ایستا، پسوند `Async`، `where` الزامی در update/delete، رد شناسه‌های مخرب). وعدهٔ یکسان‌بودن متن SQL یا رفتار لبه‌ای با Laravel یا میان موتورها نیست.
