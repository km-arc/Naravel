# PDR-020 — صفحه‌بندی (`Naravel.Pagination`)

**داوری با اولویت .NET بومی:** تا حدی بومی است. LINQ و EF Core کار اصلی را انجام می‌دهند (`Skip`/`Take` برای صفحه‌بندی offset و `Where` روی آخرین کلید دیده‌شده برای keyset) و EF Core **هیچ** نوع صفحه‌بندی ندارد. چیزی که .NET ندارد همان بخشی است که Laravel به آن معروف است: یک نتیجهٔ تایپ‌دار که شمارهٔ صفحه‌ها و لینک‌ها را حمل می‌کند، به قالب JSON پایدار تبدیل می‌شود که front end بتواند به آن تکیه کند، و cursor که دادنش به کلاینت امن است. نتیجهٔ پیشنهادی: **نگه‌داشتن، کوچک**: یک هستهٔ بدون وابستگی با سه نوع نتیجه، به‌علاوهٔ adapter برای EF Core. `Skip`/`Take` دستی سه خط است؛ چیزی که بسته اضافه می‌کند metadata، cursor و پیش‌فرض‌های امن است.

وضعیت: **پیشنهادشده، در انتظار تأیید مالک.** تا پاسخ مالک چیزی ساخته نمی‌شود؛ بخش *پرسش‌ها از مالک* را ببینید.

## زمینه

Laravel سه paginator دارد (منبع: [Laravel 12 pagination](https://laravel.com/docs/12.x/pagination)):

| Laravel | کوئری‌هایی که اجرا می‌کند | آنچه می‌دهد |
|---|---|---|
| `paginate` ← `LengthAwarePaginator` | یک `count` و سپس `limit`/`offset` | شمارهٔ صفحه، مجموع، صفحهٔ آخر |
| `simplePaginate` ← `Paginator` | یک کوئری `limit`/`offset` بدون count | فقط بعدی و قبلی |
| `cursorPaginate` ← `CursorPaginator` | `where` روی مقدار ستون‌های مرتب‌شده، بدون offset | cursor بعدی و قبلی؛ بدون شمارهٔ صفحه |

cursor در Laravel به `orderBy`، دست‌کم یک ستون مرتب‌سازی یکتا و نبودن مقدار `null` در ستون‌های مرتب‌سازی نیاز دارد. وقتی paginator از route برگردانده شود به JSON تبدیل می‌شود؛ مجموعهٔ API resource آن را در `data`، `links` (`first`، `last`، `prev`، `next`) و `meta` (`current_page`، `from`، `last_page`، `path`، `per_page`، `to`، `total`) می‌پیچد ([Eloquent resources](https://laravel.com/docs/12.x/eloquent-resources)). آن صفحه JSON برای paginator ساده یا cursor نشان نمی‌دهد، پس شکل آن‌ها در ادامه تصمیم Naravel است.

آنچه EF Core مستند کرده ([EF Core pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)): صفحه‌بندی offset دو ضعف دارد؛ پایگاه داده باز هم ردیف‌های رد‌شده را طی می‌کند و درج یا حذف هم‌زمان صفحه‌ها را جابه‌جا می‌کند (ردیفی جا می‌افتد یا دوبار دیده می‌شود). صفحه‌بندی keyset («seek») روی آخرین کلید دیده‌شده فیلتر می‌کند، با index مناسب سریع است و از تغییرهای پشت cursor اثر نمی‌گیرد، اما نمی‌تواند به صفحهٔ ۱۷ بپرد. مرتب‌سازی باید کاملاً یکتا باشد. EF Core شکل row-value یعنی `(a, b) > (x, y)` را در LINQ نمی‌تواند بیان کند، پس keyset چندستونی به شکل `a > x or (a = x and b > y)` نوشته می‌شود. EF Core نوع کمکی داخلی ندارد.

## تصمیم پیشنهادی

دو بسته، نه بیشتر:

- **`Naravel.Pagination`**: بدون EF Core، بدون ASP.NET Core و بدون وابستگی پایگاه داده. انواع نتیجه، codec مربوط به cursor، `PaginationOptions` و قرارداد JSON. بدون I/O، پس می‌تواند به query builder هم خدمت کند.
- **`Naravel.Pagination.EntityFrameworkCore`**: `PaginateAsync`، `SimplePaginateAsync` و `CursorPaginateAsync` روی `IQueryable<T>`. فقط همین پروژه به EF Core ارجاع می‌دهد.

متدهای query builder (`Query.PaginateAsync` و مشابه‌ها، R26) از همین انواع هسته استفاده می‌کنند و **جزو این PDR نیستند**.

### شروع سریع (طبق roadmap، PDR پیش از هر کدی آن را اعلام می‌کند)

```csharp
var page = await db.Posts.OrderBy(p => p.Id).PaginateAsync(pageNumber, perPage: 15, ct);   // دو کوئری: count و صفحه
return page.WithPath("/posts");                                                           // به data / links / meta سریال می‌شود
```

### انواع

- `Paginated<T>` (با طول): `Items`، `CurrentPage`، `PerPage`، `Total`، `LastPage`، `From`، `To`، به‌علاوهٔ `HasMorePages`، `OnFirstPage`، `OnLastPage`، `Map<TResult>(...)` (همان `through` در Laravel) و `WithPath`/`WithQuery` برای ساخت لینک.
- `SimplePaginated<T>`: همان بدون `Total`/`LastPage`؛ برای دانستن وجود صفحهٔ بعد `perPage + 1` ردیف می‌خواند.
- `CursorPaginated<T>`: `Items`، `PerPage`، `NextCursor`، `PreviousCursor` (هرکدام وقتی وجود ندارد `null`).

هر سه تغییرناپذیرند. `Items` یک `IReadOnlyList<T>` است. معادل `ArrayAccess`، `Collection` یا `Arrayable` وجود ندارد (مفاهیم Laravel که در .NET معنا ندارند).

### قرارداد JSON (نام‌ها با `[JsonPropertyName]` ثابت می‌شوند تا سیاست camel-case در ASP.NET نتواند آن‌ها را عوض کند)

با طول، مطابق مجموعهٔ resource در Laravel تا front end هدف‌گرفته‌شده برای Laravel بدون تغییر کار کند:

```json
{ "data": [ ... ],
  "links": { "first": "/posts?page=1", "last": "/posts?page=7", "prev": "/posts?page=1", "next": "/posts?page=3" },
  "meta":  { "current_page": 2, "from": 16, "last_page": 7, "path": "/posts", "per_page": 15, "to": 30, "total": 100 } }
```

ساده: همان، با `links.first`، `links.prev`، `links.next` و `meta` شامل `current_page`، `from`، `to`، `per_page`، `path` (بدون `last`، `total` و `last_page`).
cursor (مخصوص Naravel، چون Laravel شکلی مستند نکرده): `{ "data": [...], "links": { "prev": ..., "next": ... }, "meta": { "path": "/posts", "per_page": 15, "next_cursor": "...", "prev_cursor": null } }`.

تا وقتی مسیر با `WithPath` داده نشود لینک‌ها `null` هستند؛ بسته هرگز `HttpContext` را نمی‌خواند. `WithQuery(IEnumerable<KeyValuePair<string, string?>>)` مقدارهای دیگر query string را به همهٔ لینک‌ها می‌رساند (`withQueryString` در Laravel)؛ تبدیل `Request.Query` یک خط است.

### رفتارهایی که از اول مشخص‌اند

- **لبه‌های صفحه.** `page < 1` مثل ۱ رفتار می‌شود. صفحه‌ای بعد از انتها `Items` خالی با `Total`/`LastPage` درست برمی‌گرداند (مثل Laravel) نه خطا. مجموعهٔ خالی `LastPage = 1` و `From`/`To = null` دارد.
- **سقف per-page.** `perPage` به `PaginationOptions.MaxPerPage` (پیش‌فرض ۱۰۰) محدود می‌شود تا کلاینت نتواند یک میلیون ردیف بخواهد. `perPage < 1` در لحظهٔ فراخوانی `ArgumentOutOfRangeException` می‌دهد.
- **شمارش.** `PaginateAsync` اول count و بعد صفحه را اجرا می‌کند؛ این دو دستور جدا هستند، پس نوشتن هم‌زمان می‌تواند `Total` را یک ردیف با صفحه ناهمخوان کند. این موضوع مستند می‌شود، پنهان نمی‌شود. برای جدول‌های بزرگ مستندات به `SimplePaginateAsync` یا cursor هدایت می‌کنند.
- **مرتب‌سازی الزامی است.** اگر کوئری `OrderBy` نداشته باشد `PaginateAsync`/`SimplePaginateAsync` با `InvalidOperationException` رد می‌کنند، چون صفحهٔ بدون ترتیب پایدار نیست (هشدار خود EF Core). ترتیب پیش‌فرضی ساخته نمی‌شود.

### صفحه‌بندی cursor: طراحی‌ای که آن را امن می‌کند

فراخوان کلیدها را یک بار اعلام می‌کند و helper هم `OrderBy` و هم `Where` را اعمال می‌کند، پس ترتیب و فیلتر نمی‌توانند با هم ناسازگار شوند:

```csharp
var page = await db.Posts.CursorPaginateAsync(k => k.By(p => p.CreatedAt).Then(p => p.Id), perPage: 15, cursor, ct);
```

- کلیدها باید روی هم یکتا باشند (آخرین کلید بهتر است کلید اصلی باشد). مستندات این را می‌گویند؛ helper نمی‌تواند راستی‌آزمایی کند.
- keyset چندستونی به شکل `a > x or (a = x and b > y)` تولید می‌شود (و `<` برای کلیدهای نزولی)، که شکل قابل‌حمل و قابل‌ترجمه برای EF Core است.
- مقدار کلید `null` پشتیبانی نمی‌شود، مثل Laravel. با `InvalidOperationException` که نام کلید را می‌گوید شکست می‌خورد، به‌جای اینکه بی‌صدا ردیف‌ها را رد کند.
- cursor رشتهٔ مات base64url است که مقدارهای کلید را به‌صورت JSON و یک پرچم جهت نگه می‌دارد. **cursor یک موقعیت است، نه راز و نه مجوز:** به‌طور پیش‌فرض امضا نمی‌شود. مقدارهای آن فقط به‌صورت **پارامتر bind‌شده** و هرگز متن SQL به پایگاه داده می‌رسند و نام ستون‌ها از اعلام کلید توسط فراخوان می‌آیند نه از cursor؛ پس cursor جعلی فقط می‌تواند موقعیت را جابه‌جا کند. هر مقدار به نوع CLR اعلام‌شدهٔ کلیدش تبدیل می‌شود؛ شکل غلط، تعداد غلط، جهت ناشناخته یا cursor بلندتر از ۱ کیلوبایت `InvalidCursorException` تایپ‌دار می‌دهد که برنامه آن را به `400` تبدیل می‌کند.
- پایدار در برابر درج: ردیف‌های تازه پشت یا جلوی cursor صفحه‌ها را جابه‌جا نمی‌کنند (قراردادی که تست‌ها باید روی SQLite ثابتش کنند).

## قراردادها و کیفیت

- **S2 (سرعت).** روی مسیر صفحه‌بندی جز expression treeهایی که فراخوان به EF Core می‌دهد reflection نیست؛ codec مربوط به cursor برای پاکت ثابتش از source generation در `System.Text.Json` استفاده می‌کند. T04 یک benchmark برای مقایسهٔ offset و keyset در عمق اضافه و اعداد را ثبت می‌کند؛ تا اجرای آن هیچ ادعای سرعتی نمی‌شود.
- **انحراف از S3، صریح.** roadmap از ماژول‌های I/O یک `Meter` و یک Fake می‌خواهد. این انواع I/O ندارند و کوئری‌هایی که راه می‌اندازند از قبل با telemetry پایگاه داده و EF Core زمان‌گیری می‌شوند؛ پس meter دوم فقط تکرار است و «paginator ساختگی» همان `new Paginated<T>(...)` است که constructor اجازه می‌دهد. PDR از مالک می‌خواهد این انحراف را بپذیرد (پرسش ۴).
- هیچ‌چیز از آیتم‌ها، cursorها یا query stringها لاگ نمی‌شود.
- تست‌ها (پس از تأیید): snapshot سریال‌سازی برای هر سه شکل؛ لبه‌های صفحه (اول، آخر، بعد از انتها، خالی، `perPage` بالاتر از سقف)؛ خطای نبود `OrderBy`؛ رفت‌وبرگشت cursor، cursor دستکاری‌شده و بریده‌شده، نوع کلید غلط، ترتیب نزولی و چندستونی، پایداری در برابر درج و کلید `null`؛ همه روی SQLite (OD-01)، و یک اجرای تست فقط-هسته بدون ارجاع به EF برای اثبات مرز وابستگی.

## گزینه‌های بررسی‌شده

۱. **مستندسازی `Skip`/`Take` و اضافه‌نکردن چیزی.** صادقانه و کوچک، اما هر تیم باید قالب JSON، حساب لینک‌ها و بدتر از همه یک cursor خانگی ناامن را دوباره اختراع کند. به‌عنوان مسیر اصلی رد شد؛ مستندات همچنان نسخهٔ سه‌خطی را برای خوانندگانی که بیشتر نمی‌خواهند نشان می‌دهند.
۲. **استفاده از کتابخانهٔ keyset شخص ثالث** (صفحهٔ EF Core از `MR.EntityFrameworkCore.KeysetPagination` نام می‌برد). رد شد: وابستگی جدید PDR جدا می‌خواهد (OD-01/OD-02)، قالب JSON مثل Laravel نمی‌دهد و به query builder خدمت نمی‌کند.
۳. **JSON با camel-case، پیش‌فرض ASP.NET.** به‌عنوان پیش‌فرض رد شد چون front endهای هدف‌گرفته‌شده برای Laravel و هدف مرحله (`data`/`links`/`meta` شبیه Laravel) را می‌شکند. پرسش ۱ به مالک اجازهٔ انتخاب می‌دهد.
۴. **استنتاج کلیدهای cursor از `OrderBy` کوئری.** رد شد: بازرسی expression tree کوئری‌های دلخواه می‌خواهد و اجازه می‌دهد فیلتر و ترتیب از هم دور شوند. اعلام یک‌بارهٔ کلیدها کوچک‌تر و امن‌تر است.
۵. **امضای هر cursor.** عقب افتاد: key ring (Data Protection) می‌خواهد و سود کمی دارد، چون cursor موقعیت است و مقدارهایش پارامتر bind‌شده‌اند. بعداً بدون تغییر قالب می‌تواند به‌صورت گزینه اضافه شود.

## پرسش‌ها از مالک

۱. **نام‌های JSON:** `data`/`links`/`meta` با snake_case مثل Laravel (پیشنهادی) یا camel-case ASP.NET؟
۲. **بسته‌ها:** هستهٔ `Naravel.Pagination` به‌علاوهٔ `Naravel.Pagination.EntityFrameworkCore` مثل بالا (پیشنهادی)، یا یک بسته؟
۳. **cursor:** بدون امضا با اعتبارسنجی سخت (پیشنهادی)، یا از ابتدا با امضای Data Protection؟
۴. **انحراف از S3:** پذیرش «بدون Meter و Fake چون I/O ندارد» (پیشنهادی)؟
۵. **سقف:** `MaxPerPage` پیش‌فرض ۱۰۰ (پیشنهادی)؟

## تأیید

در انتظار. تا پاسخ مالک مرحله مسدود می‌ماند و هیچ کدی نوشته نمی‌شود.
