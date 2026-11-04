# تفاوت دیالکت‌های پایگاه داده (قرارداد)

یک فراخوانی query builder **تضمین نمی‌شود** روی همهٔ موتورها یک معنی داشته باشد. این صفحه قرارداد عمومی جاهایی است که تفاوت دارند. رفتاری که اینجا نیامده یا تست ندارد را قابل‌حمل فرض نکنید.

نسخهٔ انگلیسی: [docs/en/database-dialects.md](../en/database-dialects.md). مالکیت هر متد: [roadmap/QUERY-BUILDER-COVERAGE.md](../../roadmap/QUERY-BUILDER-COVERAGE.md).

## سطح شواهد

| سطح | معنی |
|---|---|
| **Executed** | سناریویی در `tests/Naravel.Database.Tests/ContractScenarios.cs` در CI روی سرور واقعی (SQLite، MySQL، PostgreSQL، SQL Server) اجرا می‌شود. |
| **Compile-only** | SQL تولید و بررسی می‌شود، اما روی آن موتور هرگز اجرا نمی‌شود. |
| **Refused** | فراخوانی روی آن موتور استثنای مستند پرتاب می‌کند (و خود این رد کردن به‌صورت تست اجرا می‌شود). |

## تفاوت‌هایی که بیشتر غافلگیر می‌کنند

- **کلیدهای upsert.** در MySQL پارامتر `uniqueBy` نادیده گرفته می‌شود و *هر* کلید یکتای جدول می‌تواند به‌روزرسانی را راه بیندازد؛ SQL Server در برخورد با کلید یکتای دیگر خطا می‌دهد.
- **Returning.** `*ReturningAsync` در MySQL موجود نیست (`NotSupportedException`).
- **درج یا نادیده‌گرفتن.** `InsertOrIgnoreAsync` در SQL Server موجود نیست (`NotSupportedException`).
- **قفل ردیف و full text.** SQLite، `LockForUpdate`، `SharedLock` و `WhereFullText` را رد می‌کند.
- **`Having` روی نام مستعار.** PostgreSQL و SQL Server، `Having` روی نام مستعار select را رد می‌کنند (خودِ موتور آن را نمی‌پذیرد).

جدول زیر همهٔ تفاوت‌ها را موتور به موتور دارد.

## یک فراخوانی، معنی‌های متفاوت

| فراخوانی | SQLite | MySQL | PostgreSQL | SQL Server |
|---|---|---|---|---|
| `UpsertAsync(rows, uniqueBy, update)` | `on conflict (uniqueBy)`؛ به ایندکس یکتا روی آن ستون‌ها نیاز دارد | `on duplicate key update` (از 8.0.19 با row alias)؛ **`uniqueBy` نادیده گرفته می‌شود: هر کلید یکتای جدول به‌روزرسانی را راه می‌اندازد**؛ ردیف به‌روزشده دو بار در تعداد ردیف‌های تأثیرگرفته شمرده می‌شود | `on conflict (uniqueBy)`؛ به ایندکس یکتا نیاز دارد | `merge ... with (holdlock)` روی `uniqueBy`؛ برخورد با کلید یکتای دیگر به‌جای به‌روزرسانی خطا می‌دهد |
| `UpsertAsync` با فهرست به‌روزرسانی خالی | درج ساده | درج ساده | درج ساده | درج ساده |
| `InsertOrIgnoreAsync` | پشتیبانی می‌شود | پشتیبانی می‌شود | پشتیبانی می‌شود | **`NotSupportedException`** |
| `*ReturningAsync` | `returning` | **`NotSupportedException`** | `returning` | `output inserted` |
| Returning روی دستهٔ تقسیم‌شده | `InvalidOperationException` | ندارد | `InvalidOperationException` | `InvalidOperationException` |
| Update/delete همراه join | فقط inner join | پشتیبانی می‌شود | فقط inner join؛ delete، left/right/cross را رد می‌کند | پشتیبانی می‌شود |
| `UpdateOrInsertAsync` | دو دستور، **اتمیک نیست** | همان | همان | همان |
| قفل ردیف (`LockForUpdate`، `SharedLock`) | **رد می‌شود** | پشتیبانی می‌شود | پشتیبانی می‌شود | پشتیبانی می‌شود |
| `WhereFullText` | **رد می‌شود** | اجرا می‌شود | اجرا می‌شود | **compile-only**، هرگز اجرا نمی‌شود |
| JSON contains برای کل آرایه، شیء، `Guid`، `DateTime`، `DateTimeOffset`، `char` یا مقدار enum | **`NotSupportedException`** | پشتیبانی می‌شود (به JSON سریالایز می‌شود) | پشتیبانی می‌شود (به JSON سریالایز می‌شود) | **`NotSupportedException`** |
| JSON contains برای مقدار اسکالر (رشته، عدد، bool) | عنصر آرایه (یا مقدار یک شیء) در آن مسیر، **با نوع** مقایسه می‌شود (`1` با `"1"` یکی نیست) | `json_contains`: عنصر آرایه در آن مسیر | `@>`: عنصر آرایه در آن مسیر | عنصر آرایه (یا مقدار یک شیء) در آن مسیر، **به‌صورت متن** مقایسه می‌شود (`"1"` می‌تواند با `1` یکی شود) |
| JSON contains با مقدار `null` | `ArgumentNullException` | همان | همان | همان |
| JSON contains بدون مسیر (`WhereJsonContains("col", v)`) | کل سند را جست‌وجو می‌کند | همان | همان | همان |
| حساسیت `WhereLike` به حروف | با موتورهای دیگر فرق دارد (سناریوی حساس به حروف روی SQLite اجرا نمی‌شود) | اجرا می‌شود | اجرا می‌شود | اجرا می‌شود |
| `Having` روی نام مستعار select | پشتیبانی می‌شود | پشتیبانی می‌شود | **خودِ موتور رد می‌کند** | **خودِ موتور رد می‌کند** |
| مقدار `DateTimeOffset` | ذخیره می‌شود | **رفت‌وبرگشت درست ندارد** | ذخیره می‌شود | ذخیره می‌شود |
| مقایسهٔ مقدار بولین JSON | پشتیبانی می‌شود | **پشتیبانی نمی‌شود** | پشتیبانی می‌شود | پشتیبانی می‌شود |

وضعیت «Executed» هر خانه همان فهرست پایین است: خانه‌ای که در فهرست «اجرا نمی‌شود روی» آمده، سناریویی است که مجموعهٔ تست روی آن موتور رد می‌کند.

## قرارداد خطا (قابل‌حمل، بدون اتصال، روی هر دیالکت)

| ورودی | نتیجه |
|---|---|
| عملگر ناشناس، یا عملگری که شبیه SQL است | `ArgumentException` در همان فراخوانی |
| عملگری که فقط روی دیالکت دیگر هست | `NotSupportedException` |
| جهت `OrderBy` غیر از asc/desc | `ArgumentException` |
| نام ستون یا جدول مخرب | `ArgumentException` پیش از ارسال هر دستور |
| update یا delete بدون `where` | `InvalidOperationException` |
| `WhereIn` با رشته | `ArgumentException` |
| `WhereBetween` با کران null | `ArgumentNullException` |
| `WhereIn` / `WhereNotIn` خالی | SQL معتبر (هرگز `in ()`) |
| هر مقدار | همیشه binding است، هرگز بخشی از متن SQL نیست |

این‌ها را `QueryHostileInputTests`، `IdentifierSafetyTests` و `DialectMatrixTests` بررسی می‌کنند.

## فهرست «اجرا نمی‌شود روی»

سناریوهایی که مجموعهٔ مشترک روی یک موتور رد می‌کند (آنجا رد یا پشتیبانی‌نشده‌اند، یا عمداً فرق دارند):

| سناریو | اجرا نمی‌شود روی |
|---|---|
| `where_like_case_sensitive` | SQLite |
| `where_json_boolean_value` | MySQL |
| `where_json_contains_a_whole_array` | SQLite، SQL Server |
| `where_full_text_natural` | SQLite، SQL Server |
| `where_full_text_is_unsupported_on_sqlite` | MySQL، PostgreSQL، SQL Server |
| `group_by_with_having_on_a_select_alias` | PostgreSQL، SQL Server |
| `type_datetimeoffset` | MySQL |
| `write_insert_or_ignore` | SQL Server |
| `write_returning_insert_update_delete`، `write_returning_batch_insert` | MySQL |
| `write_returning_is_refused_on_mysql` | SQLite، PostgreSQL، SQL Server |
| `subquery_join_lateral_and_left_join_lateral` | SQLite |
| `subquery_lateral_is_refused_on_sqlite` | MySQL، PostgreSQL، SQL Server |
| `locks_read_the_same_rows` | SQLite |
| `locks_are_refused_on_sqlite` | MySQL، PostgreSQL، SQL Server |

هر سناریوی دیگر روی هر چهار موتور اجرا می‌شود. `write_upsert_key_semantics_differ_by_dialect` همه‌جا اجرا می‌شود و ردیف upsert بالا را تأیید می‌کند.

## «سازگار با Laravel» یعنی چه

ادعا به **زیرمجموعهٔ تست‌شده** محدود است: متدهای فهرست پوشش، با رفتاری که مجموعهٔ قرارداد روی موتورهای بالا اجرا می‌کند، به‌علاوهٔ انحراف‌های عمدی (ردیف‌ها دیکشنری یا نوع‌های Dapper هستند، facade ایستا نداریم، پسوند `Async`، `where` صریح برای update/delete، و شناسه‌های مخرب رد می‌شوند). وعدهٔ یکسان‌بودن متن SQL یا رفتار لبه‌ای با Laravel یا میان موتورها نیست.
