# روتینگ و middleware مسیر (`Naravel.Routing`)

یک لایهٔ نازک به سبک لاراول روی روتینگ ASP.NET Core. از router و pipeline بومی استفاده می‌کند و چیزهایی را اضافه می‌کند که برنامه‌نویس‌های لاراول
کم دارند: گروه‌های تودرتو (`prefix`، `name`، `domain`، `middleware`)، قید `where()`، مسیرهای نام‌دار با ساخت URL شبیه `route()`، route model binding،
مسیرهای resource و یک سیستم کامل **middleware مسیر** (alias، group، پارامتر، اولویت، `withoutMiddleware`، terminable).
طراحی: [PDR-009](../pdr/fa/PDR-009-routing-and-http-middleware.md). نسخهٔ انگلیسی: [../en/routing.md](../en/routing.md).

> وضعیت و راستی‌آزمایی روتینگ در [ROADMAP.md](../../ROADMAP.md) (R05) پیگیری می‌شود. این راهنما رفتار پیاده‌شده را شرح می‌دهد.

## راه‌اندازی

```csharp
builder.Services.AddNaravelRouting(o =>
{
    o.Middleware.Alias<EnsureAge>("age");                 // alias به type
    o.Middleware.Group("api", "bindings", "age:18");      // group از aliasها (با آرگومان)
    o.Pattern("id", "[0-9]+");                            // مثل Route::pattern
});

var app = builder.Build();
app.UseNaravelRouting();                                  // اجرای middleware مسیر (بعد از routing)
app.MapNaravel(r => { /* مسیرها */ });
```

`WebApplication` خودش `UseRouting()` را اول pipeline اضافه می‌کند. اگر خودتان `UseRouting()` را صدا می‌زنید، `UseNaravelRouting()` را بعد از آن بیاورید.

## تعریف مسیرها

```csharp
app.MapNaravel(r =>
{
    r.Get("/", () => "home").Name("home");
    r.Post("/orders", (OrderDto dto) => Results.Created("/orders/1", dto)).Middleware("auth", "throttle:60,1");
    r.Match(new[] { "GET", "POST" }, "/form", handler);
    r.Any("/hook", handler);
    r.Redirect("/old", "/new", permanent: true);
    r.Fallback(() => Results.NotFound());
});
```

`Get` مثل لاراول به `HEAD` هم جواب می‌دهد. handlerها delegateهای معمولی Minimal API هستند. تا وقتی callback تمام نشود چیزی map نمی‌شود؛ پس ترتیب
`.Name()` و `.Where()` و `.Middleware()` مهم نیست و خطاهای پیکربندی **هنگام استارت** پرتاب می‌شوند.

### گروه‌ها

```csharp
r.Prefix("admin").Name("admin.").Middleware("auth").Group(admin =>
{
    admin.Get("users/{user}", (string user) => user).Name("users.show");      // GET /admin/users/{user}، نام admin.users.show
    admin.Domain("api.example.com").Group(api => api.Get("ping", () => "pong"));
});
```

`Prefix` و `Name` و `Domain` و `Middleware` و `WithoutMiddleware` به هر ترتیب زنجیر می‌شوند و با `.Group(...)` تمام می‌شوند. گروه داخلی به گروه بیرونی اضافه می‌شود.

### قیدها

```csharp
r.Get("/items/{id}", handler).Where("id", "[0-9]+");   // regex، خودکار anchor می‌شود
```
`Where` برای پارامتری که در URI نیست هنگام استارت خطا می‌دهد. الگوی سراسری: `o.Pattern("id", "[0-9]+")`. (`{id:int}` بومی ASP.NET Core هم کار می‌کند.)

### مسیر نام‌دار و URL

```csharp
IUrlGenerator urls = ...;                              // تزریق می‌شود
urls.Route("admin.users.show", new { user = 5, tab = "x" });   // "/admin/users/5?tab=x"
urls.AbsoluteRoute(httpContext, "home");                       // "https://host/"
```
نام ناشناخته یا نبودن مقدار لازم، `RouteNotFoundException` پرتاب می‌کند (هرگز `null` برنمی‌گرداند).

### مسیرهای resource

```csharp
r.Resource("photos", new ResourceHandlers
{
    Index = () => ..., Create = () => ..., Store = () => ...,
    Show = (string photo) => ..., Edit = ..., Update = ..., Destroy = ...,
});
r.ApiResource("photos", handlers);                    // بدون create و edit
r.Resource("photos", handlers, o => { o.Only = new[] { "index", "show" }; o.Parameter = "id"; });
```
فقط handlerهایی که set کرده‌اید مسیر می‌شوند. نام‌ها: `photos.index`، `photos.show` و ...؛ نام پارامتر مفرد آخرین بخش است (`categories` می‌شود `category`) مگر `Parameter` بدهید.
گروه‌های `.Prefix()/.Name()/.Middleware()` را مثل همیشه دور آن‌ها بگذارید.

برای controllerهای متعارف MVC، هر هفت action را با `MapNaravelControllerResource<TController>` نگاشت کنید:

```csharp
builder.Services.AddControllers();
var app = builder.Build();
app.MapControllers();
app.MapNaravelControllerResource<PhotosController>("photos");
```

نام actionهای کنترلر به‌ترتیب `Index`، `Create`، `Store`، `Show`، `Edit`، `Update` و `Destroy` است. پارامتر مسیر item پیش‌فرض `id` و نام مسیرها `photos.index`، `photos.show` و مانند آن است. actionها باید از conventional routing استفاده کنند، نه route template اختصاصی.

### Route model binding

```csharp
o.Bind<User>("user", async (value, ctx) => await db.FindUserAsync(value));   // null یعنی 404
r.Get("/users/{user}", (HttpContext c) => c.GetRouteModel<User>("user")!.Name).Middleware("bindings");
```
alias داخلی `bindings` هر پارامتری را که binding دارد resolve می‌کند. binding ضمنی با type hint مکانیزم reflection در PHP است و **پورت نشده**؛
در .NET به type خودتان `BindAsync`/`TryParse` ایستا بدهید (بومی Minimal API).

## Middleware

### Middlewareهای آماده

aliasهای داخلی route عبارت‌اند از `throttle`، `signed`، `maintenance`، `cache.headers`، `trim`، `convert.empty` و `guest`.

`throttle` تلاش‌ها را در یک `IThrottleStore` می‌شمارد. store پیش‌فرض همان limiter پنجرهٔ ثابت و process-local در `System.Threading.RateLimiting` است؛ بستهٔ جداگانهٔ `Naravel.Routing.Cache` storeای اضافه می‌کند که همهٔ instanceها مشترکاً به کار می‌برند (پایین را ببینید). شکل مستقیم `throttle:60,1` یعنی سقف درخواست و پنجره بر حسب دقیقه؛ policy نام‌دار را هنگام setup ثبت کنید:

```csharp
builder.Services.AddNaravelRouting(o => o.ConfigureThrottlePolicy("login", 5, TimeSpan.FromMinutes(1)));
app.MapNaravel(r => r.Post("/login", Login).Middleware("throttle:login"));
```

partition بر اساس شناسهٔ کاربر احراز‌شده (یا نام identity) ساخته می‌شود و اگر موجود نباشد IP درخواست را به کار می‌برد؛ endpoint و policy نیز جداکننده‌اند. پاسخ‌ها `X-RateLimit-Limit` و `X-RateLimit-Remaining` دارند؛ درخواست ردشده 429 و `Retry-After` می‌گیرد. پنجرهٔ ثابت ممکن است در بازه‌ای کوتاه پیرامون مرز دو پنجره تا ۲ برابر سقف را بپذیرد. با store پیش‌فرض شمارنده‌ها در حافظهٔ هر process هستند؛ پیش از اجرای بیش از یک instance بخش بعد را بخوانید.

#### throttle پشت بیش از یک سرور

store پیش‌فرض در حافظهٔ هر process می‌شمارد:

- **هر instance سهمیهٔ جداگانه دارد.** با N instance هر فراخوان N بار جدا شمرده می‌شود. پشت load balancer از نوع round-robin یک فراخوان می‌تواند در یک پنجره تا N برابر سقف درخواست بفرستد و از یک سرور 429 بگیرد در حالی که سرور دیگر هنوز 200 می‌دهد. سقف عملی **شل‌تر** از مقدار تنظیم‌شده است، نه سخت‌گیرانه‌تر.
- **restart همهٔ سهمیه‌ها را صفر می‌کند.** شمارنده‌ها ذخیره نمی‌شوند: deploy، crash، scale-in یا scale-out باعث می‌شود instance مربوط با شمارندهٔ خالی شروع کند و فراخوان‌ها پنجرهٔ تازه بگیرند.

store هر چه باشد، فراخوان به یک شکل شناسایی می‌شود:

- **فراخوان‌های پشت proxy.** فراخوان ناشناس با `RemoteIpAddress` شناسایی می‌شود. پشت load balancer یا reverse proxy این همان آدرس proxy است، مگر اینکه forwarded headers در ASP.NET Core فعال باشد (`UseForwardedHeaders` و proxy در `KnownProxies` یا `KnownNetworks`)؛ بدون آن همهٔ فراخوان‌های ناشناس یک bucket مشترک دارند و سقف بسیار سخت‌گیرانه می‌شود. Naravel این را دوباره پیاده نمی‌کند (PDR-009).

##### سهمیهٔ مشترک بین instanceها (`Naravel.Routing.Cache`)

`Naravel.Routing.Cache` تلاش‌های `throttle` را در یک store از [Naravel.Cache](cache.md) می‌شمارد. هر instance که از همان store استفاده کند برای هر policy و هر فراخوان یک سهمیهٔ مشترک دارد و instance ری‌استارت‌شده هم سهمیه را حفظ می‌کند. خود Routing به Cache ارجاع نمی‌دهد، پس کسی که فقط throttle درون‌process می‌خواهد آن را نصب نمی‌کند. این بسته جدید است و در بسته‌های 0.1.1 نیست؛ با نسخهٔ بعدی منتشر می‌شود ([CHANGELOG.md](../../CHANGELOG.md) را ببینید).

```csharp
// appsettings.json: "Cache": { "Stores": { "shared": { "Driver": "redis", "ConnectionString": "localhost:6379", "Prefix": "myapp" } } }
builder.Services.AddNaravelCache(builder.Configuration);
builder.Services.AddNaravelRedisCache(builder.Configuration);
builder.Services.AddNaravelRouting(o =>
{
    o.ConfigureThrottlePolicy("api", 120, TimeSpan.FromMinutes(1));
    o.ConfigureThrottlePolicy("login", 5, TimeSpan.FromMinutes(1), ThrottleFailureMode.FailClosed);
});
builder.Services.AddNaravelSharedThrottle("shared");
```

policyها، `throttle:login`، `throttle:60,1`، هدرهای `X-RateLimit-*` و پاسخ 429 همان‌طور می‌مانند. `AddNaravelSharedThrottle` را می‌توان پیش یا پس از `AddNaravelRouting` فراخواند؛ آخرین فراخوانی برنده است. نام store ناشناخته در اولین استفادهٔ throttle `DriverNotRegisteredException` می‌دهد (نام storeهای موجود را فهرست می‌کند).

نحوهٔ شمارش:

- **یک فراخوانی اتمیک، بدون lock.** هر درخواست یک فراخوانی `ICacheStore.IncrementAsync` روی store نام‌برده می‌زند. چیزی روی lock منتظر نمی‌ماند، پس burst یک فراخوان پشت خودش صف نمی‌بندد. تلاش ردشده هم شمرده می‌شود.
- **کلید هش‌شده.** کلید `throttle:{SHA-256 of policy|endpoint|subject}:{window index}` است: طول ثابت، پذیرفته‌شده در همهٔ providerها (از جمله Memcached) و بدون آدرس کلاینت. شمارنده با پنجرهٔ خودش منقضی می‌شود.
- **پنجرهٔ هم‌تراز با ساعت.** شاخص پنجره `floor(UtcNow / window)` است؛ پس پنجره‌های یک‌دقیقه‌ای در همهٔ instanceها از ابتدای هر دقیقه شروع می‌شوند، نه از اولین درخواست هر فراخوان. `Retry-After` زمان باقی‌ماندهٔ همان پنجره است.
- **هزینهٔ این طراحی.** فراخوان می‌تواند سقف را در انتهای یک پنجره و دوباره در ابتدای پنجرهٔ بعد مصرف کند: در بازه‌ای کوتاه تا ۲ برابر سقف، مثل store پیش‌فرض. پنجرهٔ لغزان و token bucket در کار نیست. ساعت instanceها باید در حد skew معمول NTP به هم نزدیک باشد؛ skew چند میلی‌ثانیه‌ای مرز پنجرهٔ همان instance را به همان اندازه جابه‌جا می‌کند و چیز دیگری را تغییر نمی‌دهد. طول پنجرهٔ یک policy را وقتی instanceهای قدیم و جدید کنار هم کار می‌کنند عوض نکنید: طول پنجره جزو کلید نیست، پس مدتی کوتاه هر دو یک شمارنده را با معنای متفاوت می‌خوانند.
- **Memory محلی process است.** `AddNaravelSharedThrottle("memory")` مثل store پیش‌فرض در هر process جدا می‌شمارد و چیزی را به اشتراک نمی‌گذارد؛ برای توسعه و تست مناسب است. هیچ بررسی runtime وجود ندارد که store مشترک هست یا نه (قرارداد Cache این را نمی‌گوید)، پس storeای را نام ببرید که مشترک است. برای سهمیهٔ حساس Redis انتخاب امن‌تر است؛ پیش از آن جدول backendها را در [cache.md](cache.md) بخوانید.
- **maintenance mode هنوز محلی process است.** فقط `throttle` store مشترک دارد؛ state مربوط به `maintenance` به اشتراک گذاشته نمی‌شود.

##### وقتی store مشترک خراب می‌شود

throttle روی مسیر درخواست قرار دارد؛ پس backend کند یا از دسترس خارج نباید برنامه را از کار بیندازد و نباید بی‌صدا سقف را هم بردارد (Laravel اجازه می‌دهد exception کش بالا بیاید که یعنی 500). هر فراخوانی store با `Throttle.StoreTimeout` (پیش‌فرض ۲۵۰ میلی‌ثانیه) محدود می‌شود. فراخوانی‌ای که exception بدهد یا از این زمان بگذرد «خرابی» است و یک breaker برای store باز می‌کند که بین همهٔ policyها مشترک است و به مدت `Throttle.BreakerPeriod` (پیش‌فرض ۵ ثانیه) باز می‌ماند: در این مدت store فراخوانی نمی‌شود. سپس یک درخواست store را امتحان می‌کند (probe)؛ اگر پاسخ داد breaker بسته می‌شود، وگرنه برای یک دورهٔ دیگر باز می‌ماند. کلاینتی که درخواست را رها کرده «خرابی» نیست. تا وقتی breaker باز است هر درخواست طبق `ThrottleFailureMode` policy خودش رفتار می‌کند:

| حالت | درخواست در زمان خرابی store | کاربرد |
|---|---|---|
| `DegradeToLocal` (پیش‌فرض) | limiter درون‌process همین instance با اعداد policy. هرگز بی‌حد و هرگز 500 نیست | بیشتر routeها |
| `FailClosed` | `503` همراه `Retry-After` (مدتی که breaker باز می‌ماند، دست‌کم ۱ ثانیه) و بدون هدرهای `X-RateLimit-*` | endpointهای credential: پذیرفتن تلاش بیش از سقف مشترک بدتر از رد کردن است |
| `FailOpen` | بدون هیچ سقفی پذیرفته می‌شود | فقط با opt-in صریح؛ تا وقتی store خراب است سقف را برمی‌دارد |

حالت برای هر policy تعیین می‌شود (مثل قطعه‌کد بالا)؛ شکل مستقیم `throttle:60,1` همیشه به limiter محلی می‌رود. هر دو عدد زمانی گزینه‌های `RoutingOptions.Throttle` هستند و یک‌بار در startup خوانده می‌شوند:

```csharp
builder.Services.AddNaravelRouting(o =>
{
    o.Throttle.StoreTimeout = TimeSpan.FromMilliseconds(100);
    o.Throttle.BreakerPeriod = TimeSpan.FromSeconds(10);
});
```

هر بار که breaker باز می‌شود یک warning همراه exception ثبت می‌شود. meter با نام `Naravel.Routing` این‌ها را می‌شمارد: `naravel.routing.throttle` (tagهای `policy` و `outcome`: `allowed`، `rejected` یا `degraded`؛ ردشدن با `FailClosed` به‌صورت `rejected` و پذیرش با `FailOpen` به‌صورت `allowed` شمرده می‌شود) و `naravel.routing.throttle.store_failures` (tagهای `policy` و `reason`: `error` یا `timeout`). tag هرگز فراخوان، آدرس یا کلید نیست. store پیش‌فرض درون‌process که نمی‌تواند خراب شود هم شمرده می‌شود.

اولین استفاده از یک store آن را می‌سازد. بسته این کار را خارج از thread درخواست انجام می‌دهد، پس Redisی که در همان لحظه در دسترس نیست حداکثر `StoreTimeout` از یک درخواست می‌گیرد.

##### کدام را به کار ببرید

1. پشت بیش از یک instance، `AddNaravelSharedThrottle` را ثبت کنید. store پیش‌فرض را جایی نگه دارید که تقریباً N برابر سقف قابل‌قبول است (کند کردن scraperها، محافظت از CPU یک instance)؛ مرز امنیتی نیست.
2. endpointهای credential (loginها، کدهای یک‌بارمصرف، بازیابی رمز) را به `FailClosed` ببرید.
3. برای شمارندهٔ صریح تلاش در handler (loginهای ناموفق به‌ازای هر حساب) `IRateLimiter` مبتنی بر Cache همچنان ابزار درست است: مثال تست‌شدهٔ login در [cache.md](cache.md). این limiter برای هر subject یک lock می‌گیرد و چند فراخوانی store دارد، پس برای شمارندهٔ تلاش‌ها مناسب است نه برای throttle سراسری روی routeای پرترافیک.
4. یا سقف را در لبه (reverse proxy یا API gateway) اعمال کنید، جایی که همهٔ ترافیک دقیقاً یک بار عبور می‌کند.

`IUrlGenerator.SignedRoute(name, values)` و `TemporarySignedRoute(name, expiresAt, values)` URL نسبی امضاشده می‌سازند. برای اعتبارسنجی، `.Middleware("signed")` را وصل کنید. امضا path و query را پوشش می‌دهد، نه host را؛ URL موقت پس از انقضا رد می‌شود. برای تولید و اعتبارسنجی لینک در چند instance، کلیدهای Data Protection باید پایدار و مشترک باشند؛ در production از key ring در برابر ذخیره‌سازی ناامن محافظت کنید.

`MaintenanceModeService` یک کلید process-local است که از DI گرفته می‌شود. در حالت maintenance، درخواست با 503 و `Retry-After` پاسخ می‌گیرد. bypass اختیاری با header `X-Naravel-Maintenance-Bypass` و secret متناظر انجام می‌شود. CLI ندارد و state را بین instanceها هماهنگ نمی‌کند.

`cache.headers:public,max-age=60` directiveهای معتبر `Cache-Control` را اعمال می‌کند. `trim` مقدارهای query و form را trim می‌کند؛ `convert.empty` مقدارهای خالی query/form را به null تبدیل می‌کند. JSON body عمداً تغییر نمی‌کند. `guest` درخواست anonymous را عبور می‌دهد و برای user احراز‌شده 403 می‌دهد؛ مقصد redirect از Auth نمی‌گیرد.

benchmark مسیر اجرای middleware (یک `DefaultHttpContext` و یک route middleware عبوری) را در Release اجرا کنید:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*InvokeRouteMiddlewarePipeline*' --job short
```

### نوشتن middleware

```csharp
public sealed class EnsureAge : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        var min = arguments.Int(0, 18);                       // "age:21" یعنی 21
        if (!AgeOk(context, min)) { context.Response.StatusCode = 403; return; }   // قطع زنجیره
        await next(context);
    }
}
```
اگر در DI ثبت شده باشد از آنجا می‌آید، وگرنه با `ActivatorUtilities` ساخته می‌شود (تزریق در constructor کار می‌کند). scope هر درخواست رعایت می‌شود.

### همهٔ انواع، از لاراول به Naravel

| لاراول | Naravel |
|---|---|
| middleware سراسری | همان `app.Use*` (برای 404 هم اجرا می‌شود) |
| middleware مسیر | `.Middleware("auth", "throttle:60,1")`، `.Middleware(typeof(X), "arg")`، `.Middleware(async (ctx, next, args) => ...)` |
| Aliasها | `o.Middleware.Alias("auth", typeof(X))` یا `Alias("x", async (ctx, next, args) => ...)` |
| Groupها | `o.Middleware.Group("api", ...)`، `PrependToGroup`، `AppendToGroup`، `ReplaceInGroup`، `RemoveFromGroup` |
| پارامترها | `name:a,b` به `MiddlewareArguments` تبدیل می‌شود (`At(i)`، `Int(i, fallback)`) |
| middleware کنترلر | `[Middleware("auth", Only = new[]{"Index"}, Except = ...)]` روی کنترلر یا action |
| `withoutMiddleware` | `.WithoutMiddleware("auth")` / `(typeof(X))` روی مسیر یا گروه؛ `[WithoutMiddleware("auth")]` روی کنترلر |
| اولویت | `o.Middleware.Priority(typeof(A), typeof(B))` |
| Terminable | علاوه بر آن `ITerminableMiddleware` را پیاده کنید؛ بعد از ارسال پاسخ با `OnCompleted` اجرا می‌شود |
| `IMiddleware` خود فریم‌ورک | به‌عنوان هدف alias قابل استفاده است (آرگومان‌ها نادیده گرفته می‌شوند) |
| endpointهای بومی | `app.MapGet(...).WithNaravelMiddleware("auth")` / `.WithoutNaravelMiddleware("auth")` |

قواعد مهم:
- نام group بر alias هم‌نام برتری دارد؛ groupها تودرتو می‌شوند (حداکثر ۱۶ سطح، چرخه خطا می‌دهد).
- حذف بر اساس **type** است: `WithoutMiddleware("throttle")` همهٔ `throttle:*` و هر aliasِ هم‌type را حذف می‌کند.
- تکراری‌ها (type و آرگومان یکسان) یک‌بار اجرا می‌شوند؛ آرگومان متفاوت جدا اجرا می‌شود.
- اولویت فقط typeهای فهرست‌شده را و فقط داخل slotهایی که از قبل اشغال کرده‌اند مرتب می‌کند (الگوریتم لاراول کمی فرق دارد).
- alias یا group ناشناخته هنگام map کردن مسیرها خطا می‌دهد، نه در اولین درخواست.
- callbackهای terminable بعد از ارسال پاسخ اجرا می‌شوند؛ اگر چندتا باشند ترتیبشان تضمین‌شده نیست.
- رشتهٔ نام کلاس (`'App\Http\Middleware\X'`) پشتیبانی نمی‌شود؛ alias یا type بدهید.

## مثال کامل

یک بلوک راه‌اندازی (alias با آرگومان، گروه middleware، سیاست throttle، pattern سراسری و model binding) و مسیرهایی که از آن استفاده می‌کنند. `IBookStore` دسترسی به دادهٔ خودتان است. alias با نام `auth` که در قطعه‌های بالا آمده داخلی نیست؛ alias خودتان را مثل `role` در همین مثال ثبت کنید.

```csharp
// 1. A route middleware with an argument: "role:admin".
public sealed class RequireRole : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        var role = arguments.At(0) ?? "user";
        if (context.Request.Headers["X-Role"] != role)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;      // short-circuit
            return;
        }
        await next(context);
    }
}

public sealed record Book(int Id, string Title);

// 2. Program.cs: aliases, a group, a throttle policy and a model binding, declared once.
builder.Services.AddNaravelRouting(o =>
{
    o.Middleware.Alias<RequireRole>("role");
    o.Middleware.Group("api", "bindings", "throttle:60,1");
    o.ConfigureThrottlePolicy("login", 5, TimeSpan.FromMinutes(1));
    o.Pattern("id", "[0-9]+");
    o.Bind<Book>("book", async (value, ctx) => await ctx.RequestServices.GetRequiredService<IBookStore>().FindAsync(int.Parse(value)));   // null -> 404
});

var app = builder.Build();
app.UseNaravelRouting();

// 3. Routes: a group with a prefix, a name prefix and middleware; a resource; named URLs.
app.MapNaravel(r =>
{
    r.Get("/", () => "home").Name("home");
    r.Post("/login", (HttpContext c) => Results.Ok()).Middleware("throttle:login");

    r.Prefix("api").Name("api.").Middleware("api").Group(api =>
    {
        api.Get("books/{book}", (HttpContext c) => c.GetRouteModel<Book>("book")).Name("books.show");     // GET /api/books/{book}

        api.ApiResource("authors", new ResourceHandlers
        {
            Index   = () => Results.Ok(new[] { "Ada", "Alan" }),
            Show    = (string author) => Results.Ok(author),
            Destroy = (string author) => Results.NoContent(),
        });                                                                                                  // names: api.authors.index, ...

        api.Prefix("admin").Name("admin.").Middleware("role:admin").Group(admin =>
            admin.Get("stats", () => Results.Ok("stats")).Name("stats"));                                    // GET /api/admin/stats, 403 without X-Role: admin
    });

    r.Get("/links", (IUrlGenerator urls) => Results.Ok(new
    {
        book  = urls.Route("api.books.show", new { book = 7 }),                                              // "/api/books/7"
        stats = urls.Route("api.admin.stats"),
    })).Name("links");

    r.Fallback(() => Results.NotFound());
});
```

## middlewareهای بومی

CORS، هدرهای proxy، host filtering، محدودیت اندازهٔ request و رمزنگاری cookie قابلیت‌های بومی ASP.NET Core هستند و دوباره پیاده نمی‌شوند (نگاه کنید به PDR-009). middlewareهای وابسته به Auth و Session هم بومی هستند: مجوزدهی route با `RequireAuthorization` از طریق `NaravelRoute.Configure` و session با `AddSession` انجام می‌شود (PDR-016 و PDR-015، هر دو `DECLINED`؛ [auth.md](auth.md) و [session.md](session.md) را ببینید). بستهٔ Console در Naravel وجود ندارد (PDR-017 آن را به نفع `System.CommandLine` رد کرده است)، پس دستورهای `route:list` و `down`/`up` برای maintenance هم وجود ندارند. store مشترک برای `throttle` بستهٔ جداگانهٔ `Naravel.Routing.Cache` است (PDR-024)؛ پس Routing به Cache یا Redis ارجاعی ندارد.

## تست‌ها چه چیزی را پوشش می‌دهند (`tests/Naravel.Routing.Tests` و `tests/Naravel.Routing.Cache.Tests`)

- «مثال کامل» راهنما (group با prefix و name و middleware، model binding، API resource، alias با آرگومان، policy محدودکننده و URLهای نام‌دار): `DocsExampleTests.cs`. تغییر یکی یعنی تغییر دیگری.
- resolution (بدون HTTP): alias، آرگومان، group (تودرتو، چرخه، mutatorها)، حذف، حذف تکراری، اولویت، cache هر endpoint، attributeهای کنترلر.
- end-to-end روی `TestServer`: verbها، گروه و پیشوند نام، ساخت URL، `Where` و الگوی سراسری (anchor)، domain، fallback، redirect، ترتیب middleware،
  قطع زنجیره، `WithoutMiddleware`، delegate درجا و alias، آداپتر `IMiddleware`، terminable، اولویت، endpoint و گروه بومی، attribute کنترلر
    (`Only`/`Except`/`WithoutMiddleware`)، model binding، resource در Minimal API و MVC، throttle، signed URL، maintenance، cache headers، نرمال‌سازی ورودی و guest.
- seam مربوط به store در throttle و مدیریت خرابی آن (`ThrottleStoreTests.cs` و `ThrottleFailureTests.cs`): یک test double از `IThrottleStore` که فراخوانی‌ها را می‌شمارد، exception می‌دهد یا معلق می‌ماند، و یک `TimeProvider` دستی؛ پس هیچ تستی منتظر دورهٔ breaker نمی‌ماند.
- store مشترک (`tests/Naravel.Routing.Cache.Tests`): یک قرارداد store روی store درون‌process، store Memory، `CacheFake` و Redis؛ پنجرهٔ هم‌تراز با ساعت و مرز ۲ برابری با ساعت دستی؛ یک increment برای هر درخواست و بدون lock؛ کلیدهای هش‌شده؛ دو host با یک سهمیهٔ مشترک؛ خرابی backend طبق policy؛ افت تعداد entryهای Memory؛ و قطعه‌کدهای «سهمیهٔ مشترک بین instanceها» (`SharedThrottleDocsExampleTests.cs`، با store Memory به‌جای Redis؛ تغییر یکی یعنی تغییر دیگری). موارد Redis به `NARAVEL_TEST_REDIS` نیاز دارند: در job مربوط به providerها اجرا می‌شوند و بدون آن skipped دیده می‌شوند، نه passed.

## محدودیت‌های این مرحله

- نگاشت resource کنترلر نام actionهای متعارف MVC و routeهای conventional را انتظار دارد؛ route template اختصاصی کنترلر با resource map ترکیب نمی‌شود.
- دستور `route:list` نداریم: بستهٔ Console در Naravel وجود ندارد (PDR-017) و اینکه روی metadata endpointها چنین دستوری اضافه شود یا نه، هنوز تصمیم‌گیری نشده است.
- state مربوط به maintenance محلی هر process است. throttle هم با store پیش‌فرض محلی است: با N instance تا N برابر سقف را می‌پذیرد و هر restart آن را صفر می‌کند؛ `Naravel.Routing.Cache` سهمیهٔ throttle را به اشتراک می‌گذارد، نه state مربوط به maintenance را (بخش «throttle پشت بیش از یک سرور»).
- signed URL برای چند instance به Data Protection key ring پایدار و مشترک نیاز دارد و به path/query متصل است، نه host.
- ترتیب metadata برای middlewareِ وصل‌شده به `MapGroup` بومی از ترتیب metadata خود ASP.NET Core پیروی می‌کند؛ حذف (exclusion) به ترتیب وابسته نیست.
- پیکربندی مسیرها یک‌بار هنگام استارت خوانده می‌شود (بدون hot reload).
