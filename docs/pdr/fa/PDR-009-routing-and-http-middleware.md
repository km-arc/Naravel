# PDR-009 — روتینگ و میدلورهای HTTP (`Naravel.Routing`)

وضعیت: **پذیرفته‌شده.** مالک طرح را در گفتگو تأیید کرد (معماری، دامنهٔ پکیج، دامنهٔ قابلیت‌ها، ترتیب مرحله‌ها) و بعد از دیدن سه مورد تأیید
زیر دستور ادامه داد؛ این به‌منزلهٔ تأیید آن‌ها ثبت می‌شود. در ۲۰۲۶-۱۰-۰۴، مالک middlewareهای آمادهٔ HTTP را در `Naravel.Routing` ادغام کرد؛
مرحلهٔ ۴b همین پروژه را گسترش می‌دهد (وضعیت جاری: [ROADMAP.md](../../../ROADMAP.md)، R05).

PDR-007 (Cache) و PDR-008 (Filesystem) تأیید و پیاده‌سازی شده‌اند (نگاه کنید به `PROGRESS.md`)؛ برای ثابت ماندن شماره‌ها این سند 009 شد.

## زمینه

لایهٔ روتینگ لاراول این‌ها را می‌دهد: ثبت fluent مسیرها، گروه‌های تودرتو (`prefix`، `name`، `domain`، `middleware`)، مسیرهای نام‌دار و
ساخت URL، route model binding، مسیرهای resource و سیستم middleware (سراسری، گروهی، مسیری، کنترلری، پارامتردار، terminable،
alias، اولویت‌دار، قابل‌استثنا).

ASP.NET Core از قبل روتینگ (Minimal API، کنترلر، `MapGroup`، `RequireHost`، route constraint)، pipeline، endpoint filter،
`System.Threading.RateLimiting`، CORS، forwarded headers، host filtering، Antiforgery و Data Protection دارد. قانون صفر برقرار است:
فقط چیزی که ارزش اضافه دارد پورت می‌شود.

## گزینه‌های بررسی‌شده

**الف. لایهٔ نازک روی ASP.NET Core (انتخاب‌شده).** از router و pipeline بومی استفاده می‌شود و ergonomics لاراولیِ کم‌شده اضافه می‌شود.
**ب. روتر و pipeline HTTP مستقل.** ردشد: Kestrel/routing را تکرار می‌کند، اکوسیستم (OpenAPI، auth، MVC) را از دست می‌دهد و هزینهٔ زیادی دارد.
**ج. فقط middleware و روتینگ بومی.** ردشد: alias، group و `withoutMiddleware` باید به مسیرها وصل شوند؛ پس این دو یک طراحی‌اند.

## تصمیم

### پکیج‌ها
| پکیج | محتوا | وابستگی |
|---|---|---|
| `Naravel.Routing` | registrar روان، گروه‌ها، مسیر نام‌دار + `IUrlGenerator`، model binding، مسیرهای resource، موتور middleware و middlewareهای آمادهٔ HTTP (فهرست پایین) | ارجاع framework به `Microsoft.AspNetCore.App` |

روتینگ driver-based نیست؛ پس از `Manager<TDriver,TOptions>` استفاده نمی‌کند و به `Naravel.Foundation` ارجاع نمی‌دهد. اگر store
مربوط به throttle بعد از `Naravel.Cache` به driver تبدیل شد، در PDR جدا تصمیم گرفته می‌شود.

### نگاشت قابلیت‌ها
| لاراول | Naravel / .NET | وضعیت |
|---|---|---|
| `Route::get/post/put/patch/delete/options`، `match`، `any` | متدهای `IRouteRegistrar` که روی `IEndpointRouteBuilder` map می‌شوند | Adapted |
| `Route::group`، `prefix`، `name`، `domain`، `middleware` (تودرتو) | `r.Prefix(..).Name(..).Domain(..).Middleware(..).Group(g => ..)` | Adapted |
| `->name()` / `route('name', [...])` | `.Name()` + `IUrlGenerator.Route(name, values)` (پوشش `LinkGenerator`) | Adapted |
| `->where()` / `Route::pattern()` | `.Where(param, regex)` + الگوهای سراسری، به‌صورت route constraint با anchor | Adapted |
| route model binding صریح | `RoutingOptions.Bind` + middleware `bindings`؛ بدون وابستگی به ORM | Adapted |
| route model binding ضمنی | `BindAsync`/`TryParse` در Minimal API | Native |
| `Route::resource` / `apiResource` | اول Minimal API (`ResourceHandlers`)؛ نگاشت کنترلر پیگیری بعدی | Adapted |
| `Route::fallback`، `redirect`، `permanentRedirect` | `Fallback`، `Redirect` | Adapted |
| `Route::view` | Blade نداریم | Rejected |
| `route:cache`، `route:list` | لازم نیست / `route:list` بعد از ماژول Console | Rejected / Open |
| handler رشته‌ای `'UserController@show'` | delegate و متد کنترلر تایپ‌دار | Rejected (مکانیزم PHP) |
| facade ایستای `Route::` | ارائه نمی‌شود؛ registrar آرگومان `MapNaravel` است. Facadeها **Open** می‌مانند (PDR جدا) | Rejected در این فاز |

### انواع middleware (نیاز «همهٔ انواع مثل لاراول»)
| نوع در لاراول | شکل در Naravel |
|---|---|
| middleware سراسری | `app.Use*` بومی (برای همهٔ درخواست‌ها حتی 404) |
| middleware مسیر | `.Middleware("auth", "throttle:60,1")`، یک type یا lambda درجا |
| aliasها (`$middlewareAliases`) | `o.Middleware.Alias("auth", typeof(..))` |
| گروه‌ها (`web`، `api`؛ prepend/append/replace/remove) | `o.Middleware.Group("api", ..)` و mutatorهای متناظر |
| پارامتر (`name:a,b`) | `MiddlewareArguments` که به `InvokeAsync` داده می‌شود |
| middleware کنترلر | attribute به‌شکل `[Middleware("auth", Only = .., Except = ..)]` |
| `withoutMiddleware` | `.WithoutMiddleware(..)` روی مسیر یا گروه (اعضای گروه را هم حذف می‌کند)، `[WithoutMiddleware]` روی کنترلر |
| اولویت (`$middlewarePriority`) | `o.Middleware.Priority(..)`؛ فقط typeهای فهرست‌شده، داخل slotهایی که اشغال کرده‌اند مرتب می‌شوند |
| Terminable (`terminate()`) | `ITerminableMiddleware.TerminateAsync` با `Response.OnCompleted` |
| کلاس‌های `IMiddleware` خود فریم‌ورک | آداپتر، تا middlewareهای موجود ASP.NET هم alias شوند |

سازوکار: یک `app.UseNaravelRouting()` metadata هر endpoint را می‌خواند، pipeline مرتب را یک‌بار برای هر endpoint resolve و cache می‌کند و اجرا می‌کند.
برای Minimal API و کنترلر کار می‌کند. قرارداد:
`IRouteMiddleware.InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)`.

### فهرست middlewareهای آمادهٔ HTTP (مرحلهٔ ۴b، درون `Naravel.Routing` پیاده می‌شود)
| لاراول | وضعیت |
|---|---|
| `bindings` (SubstituteBindings) | **انجام‌شده در مرحلهٔ ۴a**، در `Naravel.Routing` |
| `throttle` (`throttle:60,1`، limiterهای نام‌دار، `X-RateLimit-*`، `Retry-After`) | **الان (مرحلهٔ ۴b)**، روی `System.Threading.RateLimiting` (در حافظه) |
| `signed` (ValidateSignature) + ساخت signed URL | **الان (مرحلهٔ ۴b)**، روی Data Protection |
| Maintenance mode (middleware + سرویس) | **الان (مرحلهٔ ۴b)**؛ دستورهای `down`/`up` منتظر ماژول Console |
| `TrimStrings`، `ConvertEmptyStringsToNull` | **الان (مرحلهٔ ۴b)** برای query و form؛ بدنهٔ JSON در نسخهٔ اول خارج از دامنه |
| `cache.headers` | **الان (مرحلهٔ ۴b)** |
| `guest` | **الان (مرحلهٔ ۴b)** (فقط از `HttpContext.User`) |
| `HandleCors`، `TrustProxies`، `TrustHosts`، `ValidatePostSize`، `EncryptCookies` | **Native**: CORS، `ForwardedHeaders`، `HostFiltering`، محدودیت‌های Kestrel، Data Protection. چیزی پورت نمی‌شود |
| `auth`، `auth.basic`، `can`، `verified`، `password.confirm` | **بعداً**: ماژول Auth |
| `StartSession`، `VerifyCsrfToken`، `ShareErrorsFromSession` | **بعداً**: ماژول Session (تا آن موقع CSRF همان Antiforgery بومی است) |
| throttle مبتنی بر Redis | **بعداً**: بعد از `Naravel.Cache.Redis` |
| `AddQueuedCookiesToResponse`، Precognition | **Rejected**: ارزش کم در .NET |

### ارتباط با `Naravel.Queue`
`IJobMiddleware` (pipeline جاب) جدا می‌ماند چون context دیگری را wrap می‌کند. ادغام نمی‌شود. اشتراک منطق limiter ممکن است در PDR بعدی بیاید.

## موارد تأیید (تأییدشده)
۱. `FrameworkReference` به `Microsoft.AspNetCore.App` (پکیج NuGet نیست، ولی فراتر از `Microsoft.Extensions.*` در قانون سخت ۸ است).
۲. در این فاز facade ایستای `Route` نداریم.
۳. مسیرهای resource: اول Minimal API، بعد کنترلر.

## پیوست - تصمیم‌های حین پیاده‌سازی مرحلهٔ ۴a
۱. **گروه‌ها را registrar می‌سازد، نه `MapGroup`.** ترتیب metadata بین سطوح تودرتوی `MapGroup` چیزی نیست که بشود به آن تکیه کرد؛ پس
   `Prefix/Name/Domain/Middleware` داخل `MapNaravel` ترکیب می‌شوند و هر مسیر یک‌بار با یک `RouteMiddlewareMetadata` map می‌شود. `MapGroup` بومی هنوز با
   `WithNaravelMiddleware` کار می‌کند؛ حذف (exclusion) در آن حالت به ترتیب وابسته نیست.
۲. **registrar آرگومان `MapNaravel(r => ...)` است.** مسیرها وقتی callback تمام شد map می‌شوند؛ این کار `.Where()` و فراخوانی‌های fluent بدون ترتیب را ممکن می‌کند
   و خطاهای پیکربندی (alias ناشناخته، `Where` برای پارامتر ناموجود) هنگام استارت دیده می‌شوند.
۳. **binding ضمنی بومی است، پورت نشده** (لاراول type hintهای PHP را می‌خواند). `Bind` صریح و middleware `bindings` ارائه شده است.
۴. **نگاشت `Resource` برای کنترلر عقب افتاد.** resource برای Minimal API انجام شده؛ کنترلرها از قبل `[Middleware]` و `[WithoutMiddleware]` دارند.
۵. **وابستگی فقط-تست:** `Microsoft.AspNetCore.TestHost` (نسخهٔ مرکزی در `Directory.Packages.props`). هیچ پکیج منتشرشده‌ای به آن ارجاع نمی‌دهد.
۶. پیکربندی در زمان کد است (`AddNaravelRouting(o => ...)`) و یک‌بار خوانده می‌شود؛ hot reload ندارد.
۷. **تغییر دامنهٔ پکیج در ۲۰۲۶-۱۰-۰۴:** middlewareهای آمادهٔ HTTP باید در پکیج موجود `Naravel.Routing` باشند، نه در پکیجی جدا. پیاده‌سازی مرحلهٔ ۴b هنوز باقی است.

## پیامدها
- یک پروژهٔ runtime روتینگ و پروژهٔ تست آن در `Naravel.slnx`؛ مرحلهٔ ۴b همین پروژه‌ها را گسترش می‌دهد. Foundation، Queue، Filesystem و Cache تغییر نمی‌کنند.
- روی typeهای عمومی XML doc (هدف، معادل لاراول، دلیل، چیزهای پورت‌نشده) و مستندات EN و FA.
- تست در هر مرحله اجباری است؛ اگر `dotnet` قابل اجرا نبود، گزارش مرحله باید صریح بگوید تست‌ها اجرا نشده‌اند.
