# معماری

## اجزا (همه در namespace `Naravel.Foundation`)
```
IDriverRegistry<TDriver>  ── منبع حقیقت: نام -> factory (sync یا async)، همیشه باز در runtime، thread-safe
DriverRegistry<TDriver>   ── پیاده‌سازی پیش‌فرض (ConcurrentDictionary، case-insensitive)، رویداد Registered(name)
IManagerOptions           ── Default + Stores (دیکشنری از IConfigurationSection خام)
ManagerOptions            ── کلاس پایهٔ آمادهٔ bind برای options ماژول‌ها
Manager<TDriver,TOptions> ── resolve + ساخت یک‌باره + cache + invalidate + dispose
ServiceCollectionExtensions ── AddNaravelManager / AddNaravelDriver / ConfigureNaravelDrivers
DriverNotRegisteredException، ManagerOptionsExtensions.GetStore
```

## جریان درخواست `manager.DriverAsync("redis")`
۱. نام resolve می‌شود (`null` ⇒ `DefaultDriverName` که هر بار تازه از `IOptionsMonitor.CurrentValue` خوانده می‌شود).
۲. اگر registry factory نداشته باشد، فوراً `DriverNotRegisteredException`.
۳. زیر یک lock کوتاه، `Entry` (شامل `TaskCompletionSource`) گرفته یا درج می‌شود. فقط درج‌کننده ساخت را شروع می‌کند؛ پس
   **فراخوانی‌های هم‌زمان دقیقاً یک بار factory را صدا می‌زنند.**
۴. ساخت بیرون از lock اجرا می‌شود: `registry.CreateAsync(name, provider, lifetimeToken)`.
۵. فراخواننده‌ها با `WaitAsync(ct)` منتظر Task مشترک می‌مانند: لغو یک فراخواننده فقط انتظار خودش را قطع می‌کند نه ساخت مشترک را.
۶. در خطا، entry حذف می‌شود (**خطا هرگز cache نمی‌شود**)؛ در موفقیت درایور cache می‌ماند.

مسیر sync (`Driver`) از همین cache استفاده می‌کند؛ factoryهای sync را inline اجرا می‌کند و برای factory فقط-async **exception می‌دهد**
به‌جای block کردن (بدون deadlock ناشی از sync-over-async). اگر درایور async قبلاً ساخته شده باشد، `Driver` همان را برمی‌گرداند.

## Invalidate شدن
- **تغییر factory** (`Extend` / `registry.Register`): نمونهٔ cache‌شده با آن نام بازنشسته می‌شود.
- **تغییر کانفیگ:** `IOptionsMonitor.OnChange` با *هر* reload کانفیگ (حتی کلیدهای بی‌ربط) فعال می‌شود. Manager یک fingerprint از `Stores`
  را مقایسه می‌کند و فقط در تغییر واقعی همهٔ درایورها را بازنشسته می‌کند. تغییر فقط `Default` نیاز به rebuild ندارد چون هر بار خوانده می‌شود. (PDR-004)
- **دستی:** `Forget(name)`، `ForgetAll()`.

## طول عمر و Dispose
درایورهای بازنشسته **فوراً dispose نمی‌شوند** (ممکن است درخواستی هنوز استفاده کند). در لیست «بازنشسته» می‌مانند و همراه درایورهای cache‌شده
هنگام dispose شدن Manager بسته می‌شوند. درایورها بر اساس reference یکتا می‌شوند و یک‌بار dispose می‌شوند؛ خطاها بعد از تلاش برای همه در
`AggregateException` جمع می‌شوند. ساختی که بعد از dispose تمام شود، نتیجه‌اش را dispose می‌کند و فراخواننده `ObjectDisposedException` می‌گیرد.

## Thread safety
همهٔ اعضای عمومی thread-safe هستند. وضعیت با یک lock کوتاه محافظت می‌شود و هیچ factory‌ای زیر lock اجرا نمی‌شود.

## وابستگی‌ها
`Microsoft.Extensions.DependencyInjection.Abstractions`، `Microsoft.Extensions.Options`،
`Microsoft.Extensions.Options.ConfigurationExtensions`، `Microsoft.Extensions.Configuration.Abstractions`. هیچ وابستگی به ماژول‌ها ندارد.
