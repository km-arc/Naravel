# اتصال‌های Redis

`Naravel.Redis` اتصال‌های Redis **نام‌دار و مشترک** می‌دهد. خودِ `IConnectionMultiplexer` واقعی StackExchange.Redis را برمی‌گرداند و هرگز دستورها را نمی‌پیچد (PDR-010). `Naravel.Cache.Redis` و `Naravel.Queue.Redis` اتصالشان را از آن قرض می‌گیرند، پس یک سرور یک اتصال است و یک مالک آن را می‌بندد.

## شروع سریع

```json
{ "Redis": { "Default": "main", "Stores": { "main": { "ConnectionString": "localhost:6379" } } },
  "Cache": { "Stores": { "redis": { "Driver": "redis", "Connection": "main", "Prefix": "app" } } },
  "NaravelQueue": { "Stores": { "jobs": { "Driver": "redis", "Connection": "main" } } } }
```

```csharp
builder.Services.AddNaravelRedis(builder.Configuration);   // AddNaravelRedisCache و AddRedisDriver هم آن را برایتان صدا می‌زنند
var multiplexer = redis.Connection("main").Multiplexer;    // redis یک RedisManager تزریق‌شده است؛ همان IConnectionMultiplexer واقعی
```

## نحوهٔ استفاده

### اتصال‌های نام‌دار

هر store زیر `Redis:Stores:<name>` فقط یک `ConnectionString` است: رشتهٔ StackExchange.Redis همهٔ گزینه‌های client را دارد (TLS، رمز، `defaultDatabase`، timeoutها)، پس Naravel نام گزینهٔ جدید اختراع نمی‌کند. `redis.Connection()` اتصال پیش‌فرض (`Redis:Default`) را می‌دهد و `IRedisConnection` همان را تزریق می‌کند. نام ناشناخته خطا می‌دهد و نام‌های موجود را فهرست می‌کند؛ store بدون `ConnectionString` با نام همان store خطا می‌دهد.

### Cache و Queue

store کش یا صف با `Connection` نام یک اتصال را می‌دهد. کلید قدیمی `ConnectionString` همچنان کار می‌کند: رشته‌های برابر با hash مقایسه می‌شوند و یک multiplexer مشترک می‌گیرند، پس یک store و قفلش، یا دو store روی یک سرور، دیگر چند اتصال باز نمی‌کنند. برای این بهبود هیچ تغییری در تنظیمات لازم نیست.

### مالکیت

manager در اولین استفاده multiplexer را می‌سازد و هنگام خاموش شدن میزبان همه را dispose می‌کند. مصرف‌کننده‌ها قرض می‌گیرند و نباید dispose کنند. `RedisCacheStore`، `RedisLock` و `RedisQueueDriver` پارامتر اختیاری `ownsConnection` دارند: `true` (پیش‌فرض store کش و قفل، مثل قبل) اتصالی را که گرفته‌اند dispose می‌کند، `false` (پیش‌فرض درایور صف، مثل قبل، و مقداری که ثبت‌ها می‌دهند) آن را به مالکش می‌سپارد. چون ثبت‌ها قرض می‌گیرند، اتصالی که درایور صف باز می‌کرد و هرگز نمی‌بست حالا با manager بسته می‌شود.

### بارگذاری مجدد و اتصال‌های زمان اجرا

عوض شدن رشتهٔ اتصال در تنظیمات باعث می‌شود `Connection(name)` بعدی یک multiplexer جدید باز کند؛ قدیمی برای درخواست‌هایی که هنوز از آن استفاده می‌کنند باز می‌ماند و با manager dispose می‌شود. تغییرهای نامربوط تنظیمات هیچ اثری ندارند. بعداً با `redis.Extend("name", provider => ...)` اتصال اضافه کنید.

### تست

fake پروتکل Redis وجود ندارد: رفتاری (اسکریپت‌ها، انقضا) را جعل می‌کرد که سرور تعیین می‌کند. درز آزمون یک `Func<string, IConnectionMultiplexer>` است که پیش از `AddNaravelRedis` در DI ثبت می‌شود؛ آزمون‌ها با آن اتصال‌ها را می‌شمارند یا جایگزین برمی‌گردانند. آزمون روی سرور واقعی همچنان env-gated است (`NARAVEL_TEST_REDIS`).

## مثال کامل

یک connection مشترک برای کد خودتان، یک connection نام‌دار دوم و یک connection زمان اجرا. cache و queue همچنان همان connection با نام `main` را قرض می‌گیرند.

```csharp
// Program.cs: one Redis server, borrowed by the cache, the queue and your own code.
builder.Services.AddNaravelRedis(builder.Configuration);        // reads Redis:Default and Redis:Stores (see the quickstart JSON)

app.MapPost("/visits", async (RedisManager redis, CancellationToken ct) =>
{
    var db = redis.Connection().Multiplexer.GetDatabase();      // the real StackExchange.Redis IDatabase
    var count = await db.StringIncrementAsync("visits");
    return Results.Ok(new { count });
});

// A second, named connection (for example another database or server), declared in configuration:
//   "Redis": { "Default": "main", "Stores": { "main": { "ConnectionString": "localhost:6379" },
//                                              "sessions": { "ConnectionString": "localhost:6379,defaultDatabase=2" } } }
app.MapGet("/sessions/{id}", async (string id, RedisManager redis) =>
{
    var db = redis.Connection("sessions").Multiplexer.GetDatabase();
    var value = await db.StringGetAsync($"session:{id}");
    return value.HasValue ? Results.Ok(value.ToString()) : Results.NotFound();
});

// A connection added at run time (Laravel: Redis::extend). Do not dispose the multiplexer yourself: the manager owns it.
var redis = app.Services.GetRequiredService<RedisManager>();
redis.Extend("reports", provider => new ReportsRedisConnection(provider));   // your IRedisConnection implementation
```

درز تست (بخش «تست» بالا را ببینید):

```csharp
// Count connections without a server: register a stand-in before AddNaravelRedis.
var opened = 0;
services.AddSingleton<Func<string, IConnectionMultiplexer>>(_ => { opened++; return stub; });   // stub: your own IConnectionMultiplexer stand-in
services.AddNaravelRedis(configuration);
// ... resolve two stores that point at the same server, then: opened.Should().Be(1);
```

## مشاهده‌پذیری

مورد اختصاصی ندارد: client شمارنده‌های خودش را می‌دهد. رشتهٔ اتصال و رمز هرگز توسط Naravel لاگ نمی‌شوند، نام نمی‌گیرند و در پیام استثنا نمی‌آیند؛ اتصال ضمنی `implicit-` به‌علاوهٔ یک hash نام می‌گیرد.

## نحوهٔ ساخت (پیاده‌سازی)

- `RedisManager` یک `Manager<IRedisConnection, RedisOptions>` از Foundation است؛ هر اتصال یک driver است، پس `Extend`، بارگذاری مجدد و dispose از کلاس پایه می‌آید (PDR-004، PDR-005).
- `FromConnectionString` در اولین استفاده driver‌ای با نام hash ثبت می‌کند. رشته فقط درون factory ای که اتصال را باز می‌کند نگه داشته می‌شود.
- multiplexer با `ConnectionMultiplexer.Connect` (همگام) باز می‌شود، چون Cache و Queue storeهایشان را همگام resolve می‌کنند.

## شامل نیست (با تصمیم)

forward کردن دستورها (`Redis::get()`)، fake پروتکل Redis، قابلیت‌های Cluster یا Sentinel بیش از آنچه رشتهٔ اتصال اجازه می‌دهد، و اسکریپت‌های Lua (در بسته‌های Cache و Queue می‌مانند).

## نگاشت به Laravel

| Laravel | Naravel |
|---|---|
| `redis.*` در `config/database.php` | `Redis:Stores:<name>:ConnectionString` |
| `Redis::connection('cache')` | `redisManager.Connection("cache")` |
| `connection => 'cache'` در store کش | `"Connection": "cache"` روی store |
| `Redis::extend()` | `redisManager.Extend(name, factory)` |
