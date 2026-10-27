# صفحه‌بندی (Pagination)

بستهٔ `Naravel.Pagination` صفحه‌هایی با شکل Laravel (`data`، `links`، `meta`) برمی‌گرداند و `Naravel.Pagination.EntityFrameworkCore` آن‌ها را از یک query در EF Core پر می‌کند ([PDR-020](../pdr/fa/PDR-020-pagination.md)). هسته به EF Core، ASP.NET Core یا دیتابیس وابسته نیست؛ فقط آداپتر به EF Core ارجاع دارد.

> ساخت و تست‌ها به صورت محلی اجرا نشده‌اند (NOT RUN، چون در محیط نگارش .NET SDK نیست)؛ CI معیار است.

## شروع سریع

```sh
dotnet add package Naravel.Pagination.EntityFrameworkCore   # Naravel.Pagination را هم می‌آورد
```

```csharp
using Naravel.Pagination;

// با شمارش کل (Laravel: paginate): دو دستور، یک شمارش و یک صفحه.
Paginated<Post> page = await db.Posts.OrderBy(p => p.Id).PaginateAsync(page: 2, perPage: 15);

// ساده (Laravel: simplePaginate): یک دستور، بدون شمارش، فقط می‌داند «صفحهٔ بعد هست یا نه».
SimplePaginated<Post> simple = await db.Posts.OrderBy(p => p.Id).SimplePaginateAsync(2, 15);

// Cursor (Laravel: cursorPaginate): صفحه‌بندی keyset، هزینهٔ هر صفحه یکسان.
CursorPaginated<Post> next = await db.Posts.CursorPaginateAsync(k => k.By(p => p.CreatedAt, descending: true).Then(p => p.Id), 15, cursor);
```

## چه چیزی می‌گیرید

| Laravel | Naravel | هزینه |
|---|---|---|
| `paginate($n)` | `PaginateAsync(page, perPage)` ← `Paginated<T>` | `count(*)` و `limit/offset` |
| `simplePaginate($n)` | `SimplePaginateAsync(page, perPage)` ← `SimplePaginated<T>` | یک دستور، `perPage + 1` ردیف می‌خواند |
| `cursorPaginate($n)` | `CursorPaginateAsync(keys, perPage, cursor)` ← `CursorPaginated<T>` | یک دستور، `where` روی کلیدها، بدون offset |
| `through($fn)` | `Map(fn)` | ندارد |
| `withPath`، `appends`، `getPageName` | `WithPath`، `WithQuery`، `WithPageName` / `WithCursorName` | ندارد |

شمارهٔ صفحه را خودتان می‌دهید (`Request.Query["page"]`)؛ Naravel درخواست جاری را نمی‌خواند.

## JSON

با شمارش کل:

```json
{"data":[...],
 "links":{"first":"/posts?page=1","last":"/posts?page=3","prev":null,"next":"/posts?page=2"},
 "meta":{"current_page":1,"from":1,"last_page":3,"path":"/posts","per_page":10,"to":10,"total":25}}
```

صفحهٔ ساده `last` و `total` ندارد؛ صفحهٔ cursor در `meta` این‌ها را دارد: `path`، `per_page`، `next_cursor`، `prev_cursor` و در `links` فقط `prev` و `next`. نام‌ها با attribute ثابت شده‌اند و به تنظیم JSON برنامه وابسته نیستند، و `null` همیشه نوشته می‌شود (حذف نمی‌شود).

## لبه‌های صفحه

| حالت | نتیجه |
|---|---|
| `page` کمتر از ۱ | مثل ۱ |
| صفحه بعد از آخر | `data` خالی، `total` و `last_page` درست |
| جدول خالی | `last_page` برابر ۱، `from` و `to` برابر `null` |
| `perPage` بیشتر از سقف (۱۰۰) | به سقف کم می‌شود؛ با `new PaginationOptions { MaxPerPage = 50 }` عوض کنید |
| `perPage` کمتر از ۱ | `ArgumentOutOfRangeException` |
| query بدون `OrderBy` (در `PaginateAsync` و `SimplePaginateAsync`) | `InvalidOperationException`: صفحه از query بدون ترتیب پایدار نیست |

دلیل سقف: اندازهٔ صفحه معمولاً از query string می‌آید و درخواست یک میلیون ردیف حملهٔ انکار سرویس است. Laravel این را به عهدهٔ فراخواننده می‌گذارد؛ Naravel نه.

`PaginateAsync` دو دستور اجرا می‌کند، پس نوشتنی بین آن‌ها می‌تواند total را یک ردیف با صفحه ناهمخوان کند. صفحه‌بندی offset هر ردیف ردشده را هم می‌خواند، پس صفحه‌های عمیق کندتر می‌شوند.

## صفحه‌بندی با cursor

کلیدهای مرتب‌سازی را **یک بار** اعلام می‌کنید. helper هم `ORDER BY` و هم `WHERE` را از همان اعلام می‌نویسد، پس با هم نمی‌توانند ناسازگار شوند:

```csharp
k => k.By(p => p.CreatedAt, descending: true).Then(p => p.Id)
```

قواعد (همه اعمال می‌شوند):

- **کلیدها روی هم باید یکتا باشند.** با کلید اصلی تمام کنید. اگر دو ردیف همهٔ کلیدها را مشترک داشته باشند، یکی ممکن است جا بیفتد یا تکرار شود؛ helper این را نمی‌تواند بررسی کند.
- کلید باید دسترسی ساده به عضو باشد (`p => p.Id` یا `p => p.Author.Id`)؛ غیر از این `ArgumentException` است.
- نوع کلید: عددها، `DateTime`، `DateTimeOffset`، `TimeSpan`، `DateOnly`، `TimeOnly`، `string` (رشته‌ها با collation دیتابیس مقایسه می‌شوند). `Guid`، enum و `bool` هنگام اعلام `NotSupportedException` می‌دهند، نه در اولین درخواستِ دارای cursor.
- مقدار کلید نباید `null` باشد؛ `null` در آیتمِ مرز صفحه `InvalidOperationException` با نام کلید است. ستونی انتخاب کنید که هرگز null نیست.
- `OrderBy` موجود روی query جایگزین می‌شود: کلیدهای اعلام‌شده ترتیب اصلی‌اند. فیلترها می‌مانند.

شرط چندکلیدی: `a > x or (a = x and b > y)`، برای کلید نزولی و برای حرکت به عقب برعکس. مقدارهای cursor به صورت **پارامتر** به دیتابیس می‌روند؛ نام ستون فقط از اعلام شما می‌آید، هرگز از cursor.

### خود cursor

یک رشتهٔ مات base64url از `{"d":"n"|"p","k":[...]}` (جهت و مقدار کلیدها)، حداکثر ۱۰۲۴ نویسه. cursor یک **موقعیت** است، نه راز و نه مجوز: امضا نمی‌شود و کلاینت می‌تواند آن را دست‌کاری کند، و نتیجه‌اش فقط جابه‌جا شدن موقعیت است. چیزی در cursor نگذارید که کلاینت اجازهٔ دیدنش را ندارد و برای کنترل دسترسی از آن استفاده نکنید (در query فیلتر کنید). خواندن سخت‌گیرانه است: base64 خراب، شکل JSON غلط، تعداد مقدار غلط، مقداری که با نوع کلید نمی‌خواند، null یا طول زیاد، همه `InvalidCursorException` می‌دهند (پیام هرگز خود cursor را ندارد). آن را به HTTP 400 نگاشت کنید:

```csharp
try { return Ok(await query.CursorPaginateAsync(keys, 15, cursor)); }
catch (InvalidCursorException) { return BadRequest(); }
```

`PreviousCursor` در صفحهٔ اول `null` است و `NextCursor` در صفحهٔ آخر؛ اگر دقیقاً `perPage` ردیف مانده باشد صفحهٔ بعد ساخته نمی‌شود، چون یک ردیف اضافه خوانده می‌شود تا معلوم شود. حرکت به عقب با `PreviousCursor` صفحهٔ قبلی را با ترتیب عادی برمی‌گرداند.

### چرا keyset

| | Offset (`PaginateAsync`) | Keyset (`CursorPaginateAsync`) |
|---|---|---|
| هزینهٔ صفحهٔ N | با N زیاد می‌شود (ردیف‌های ردشده خوانده می‌شوند) | برای همهٔ صفحه‌ها یکسان، با ایندکس روی کلیدها |
| ردیف درج‌شده پشت خواننده | یک ردیف را به صفحهٔ بعد می‌راند (دوبار دیده می‌شود) | اثری ندارد |
| پرش به صفحهٔ N | بله | نه، فقط بعدی و قبلی |
| تعداد کل | بله | نه |

اندازه‌گیری مقایسه در R16.T04 ثبت می‌شود؛ تا آن موقع هیچ عددی ادعا نمی‌شود.

## مثال کامل

سه endpoint فهرست روی یک EF Core context: length-aware، ساده و cursor. JSON هر کدام همان شکل Laravel است که بالاتر آمد.

```csharp
using Naravel.Pagination;

public sealed class Post
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Post> Posts => Set<Post>();
}

// Program.cs: AppDb is a normal EF Core context (provider of your choice).
builder.Services.AddDbContext<AppDb>(o => o.UseSqlite("Data Source=app.db"));

// Length-aware: GET /posts?page=2&perPage=10  (page comes from you; Naravel does not read the request)
app.MapGet("/posts", async (AppDb db, HttpRequest request, int page = 1, int perPage = 15, CancellationToken ct) =>
{
    var result = await db.Posts.OrderBy(p => p.Id).PaginateAsync(page, perPage, ct);
    return Results.Ok(result
        .WithPath("/posts")
        .WithQuery(request.Query.Where(q => q.Key != "page").Select(q => KeyValuePair.Create(q.Key, (string?)q.Value))));
});

// Simple: one statement, no total. Good for "older / newer" buttons.
app.MapGet("/posts/simple", async (AppDb db, int page = 1, CancellationToken ct) =>
    Results.Ok((await db.Posts.OrderBy(p => p.Id).SimplePaginateAsync(page, 15, ct)).WithPath("/posts/simple")));

// Cursor: keyset paging for a feed. Keys together must be unique, so end with the primary key.
app.MapGet("/posts/feed", async (AppDb db, string? cursor, CancellationToken ct) =>
{
    try
    {
        var feed = await db.Posts.CursorPaginateAsync(
            k => k.By(p => p.CreatedAt, descending: true).Then(p => p.Id), 15, cursor, ct);
        return Results.Ok(feed.WithPath("/posts/feed"));
    }
    catch (InvalidCursorException)
    {
        return Results.BadRequest("invalid cursor");           // a client edited or truncated it
    }
});

// Shape the rows without a second query (Laravel: through).
// var dto = page.Map(p => new PostDto(p.Id, p.Title));
```

## پورت نشده

- خواندن صفحه از درخواست HTTP جاری و رندر HTML در `links()`: لینک‌ها را با `WithPath`/`WithQuery` بسازید و در view رندر کنید.
- `paginate` روی query builder خود Naravel (`Naravel.Database`): در R26 و بعد از R22 برنامه‌ریزی شده است.
- Meter و Fake ندارد: این نوع‌ها I/O ندارند، پس چیزی برای اندازه‌گیری یا جعل نیست (انحراف از DoD S3 که در PDR-020 گفته شده).
