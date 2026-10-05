# روتینگ و middleware مسیر (`Naravel.Routing`)

یک لایهٔ نازک به سبک لاراول روی روتینگ ASP.NET Core. از router و pipeline بومی استفاده می‌کند و چیزهایی را اضافه می‌کند که برنامه‌نویس‌های لاراول
کم دارند: گروه‌های تودرتو (`prefix`، `name`، `domain`، `middleware`)، قید `where()`، مسیرهای نام‌دار با ساخت URL شبیه `route()`، route model binding،
مسیرهای resource و یک سیستم کامل **middleware مسیر** (alias، group، پارامتر، اولویت، `withoutMiddleware`، terminable).
طراحی: [PDR-009](../pdr/fa/PDR-009-routing-and-http-middleware.md). نسخهٔ انگلیسی: [../en/routing.md](../en/routing.md).

> وضعیت: در مرحلهٔ ۴a پیاده شد و در **۲۰۲۶-۱۰-۰۴ راستی‌آزمایی شد**: هر ۶۴ تست موفق بود و پروژه بدون خطای XML-doc و بدون هشدار CS1734 build می‌شود. `PROGRESS-ROUTING.md` را ببینید.
> CI (یا `dotnet test Naravel.slnx` روی ماشین شما) اولین build واقعی است؛ `PROGRESS-ROUTING.md` را ببینید.

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

### Route model binding

```csharp
o.Bind<User>("user", async (value, ctx) => await db.FindUserAsync(value));   // null یعنی 404
r.Get("/users/{user}", (HttpContext c) => c.GetRouteModel<User>("user")!.Name).Middleware("bindings");
```
alias داخلی `bindings` هر پارامتری را که binding دارد resolve می‌کند. binding ضمنی با type hint مکانیزم reflection در PHP است و **پورت نشده**؛
در .NET به type خودتان `BindAsync`/`TryParse` ایستا بدهید (بومی Minimal API).

## Middleware

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

## middlewareهای آماده

`Naravel.Routing` موتور middleware مسیر (ثبت، alias، group، پارامتر، ترتیب و اجرا) را فراهم می‌کند. middlewareهای آماده مانند throttle، signed URL،
maintenance mode و TrimStrings برای مرحلهٔ ۴b در همین بسته برنامه‌ریزی شده‌اند؛ این مرحله هنوز شروع نشده و این پیاده‌سازی‌ها فعلاً موجود نیستند. CORS،
هدرهای proxy، host filtering، محدودیت اندازهٔ post و رمزنگاری cookie قابلیت‌های بومی ASP.NET Core هستند و دوباره پیاده نمی‌شوند (نگاه کنید به PDR-009).

## تست‌ها چه چیزی را پوشش می‌دهند (`tests/Naravel.Routing.Tests`، ۶۴ تست، **همه در ۲۰۲۶-۱۰-۰۴ موفق**)

- resolution (بدون HTTP): alias، آرگومان، group (تودرتو، چرخه، mutatorها)، حذف، حذف تکراری، اولویت، cache هر endpoint، attributeهای کنترلر.
- end-to-end روی `TestServer`: verbها، گروه و پیشوند نام، ساخت URL، `Where` و الگوی سراسری (anchor)، domain، fallback، redirect، ترتیب middleware،
  قطع زنجیره، `WithoutMiddleware`، delegate درجا و alias، آداپتر `IMiddleware`، terminable، اولویت، endpoint و گروه بومی، attribute کنترلر
  (`Only`/`Except`/`WithoutMiddleware`)، model binding و مسیرهای resource.

## محدودیت‌های این مرحله

- نگاشت `Resource` برای کنترلر در این مرحله نیست (فقط Minimal API)؛ کنترلرها attribute میدلور دارند و attribute routing معمولی را استفاده می‌کنند.
- هنوز دستور `route:list` نداریم (به ماژول Console نیاز دارد).
- ترتیب metadata برای middlewareِ وصل‌شده به `MapGroup` بومی از ترتیب metadata خود ASP.NET Core پیروی می‌کند؛ حذف (exclusion) به ترتیب وابسته نیست.
- پیکربندی مسیرها یک‌بار هنگام استارت خوانده می‌شود (بدون hot reload).
