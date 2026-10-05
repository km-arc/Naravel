# PDR-004: شکل کانفیگ، hot reload، invalidate و سیاست dispose

- **وضعیت:** پذیرفته‌شده
- **معادل لاراول:** `config/cache.php` (`default`، `stores`)، `env()`، `config:cache`، `forgetDrivers()`
- **معادل native دات‌نت:** `IConfiguration` + الگوی Options (`IOptionsMonitor<T>`)

## تصمیم‌ها
۱. **شکل:** `Default` + دیکشنری `Stores` (شبیه لاراول) روی `ManagerOptions`. مقادیر store همان `IConfigurationSection` خام‌اند و هر درایور options تایپ‌دار خودش را bind می‌کند.
   Named Options رد شد چون مجموعهٔ نام storeها را کاربر تعیین می‌کند و در compile-time معلوم نیست.
۲. **options زنده:** managerها `IOptionsMonitor<TOptions>` می‌خوانند؛ `Default` در هر فراخوانی خوانده می‌شود.
۳. **invalidate با fingerprint، نه «پاک کردن در هر تغییر».** *به‌صورت تجربی تایید شد:* تغییر یک کلید بی‌ربط (`Other:X`) و `Reload()` هم `IOptionsMonitor.OnChange` را فعال می‌کند
   چون change token یک section همان token کل ریشهٔ کانفیگ است. پاک کردن با هر callback باعث ساخت مجدد اتصال‌ها (Redis، HTTP client ...) با ویرایش‌های بی‌ربط می‌شد.
   Manager یک fingerprint از همهٔ `Stores` (نام + تک‌تک کلید/مقدارها) نگه می‌دارد و فقط با تغییر آن درایورها را بازنشسته می‌کند. تغییر فقط `Default` هرگز چیزی را rebuild نمی‌کند.
۴. **بازنشسته کن، فوراً dispose نکن.** درایورهای خارج‌شده از cache به لیست بازنشسته می‌روند و با dispose شدن manager بسته می‌شوند چون ممکن است درخواستی هنوز از آن‌ها استفاده کند.
   بده‌بستان: حافظه به‌ازای هر تغییر واقعی کانفیگ یک درایور رشد می‌کند؛ چون تغییر واقعی نادر است قابل‌قبول است.
۵. **مالکیت:** manager آنچه factoryها برمی‌گردانند را مالک است و dispose می‌کند؛ dispose بر اساس reference یکتا می‌شود؛ خطاها بعد از تلاش برای همهٔ درایورها جمع می‌شوند.
۶. **راه فرار:** `Forget(name)` / `ForgetAll()` برای درایورهایی که به کانفیگ خارج از بخش store خودشان وابسته‌اند (الگوی `connection => 'cache'` لاراول).
۷. **`config:cache` پورت نمی‌شود:** در .NET هزینهٔ boot به‌ازای هر درخواست برای بهینه‌سازی وجود ندارد.
۸. **نام‌ها case-insensitive‌اند** (مثل کلیدهای کانفیگ .NET).

## گزینه‌های ردشده
- *`IOptions<T>` ثابت:* reload ندارد؛ برای مدل request-per-process لاراول قابل‌قبول است اما برای hostهای طولانی‌مدت .NET و ConfigMap کوبرنتیز ضعیف است.
- *fingerprint به‌ازای هر store:* وابستگی‌های بین‌بخشی را از دست می‌دهد (مورد ۶).
- *dispose فوری هنگام reload:* خطر `ObjectDisposedException` برای درخواست‌های در حال اجرا.
- *reference counting/lease:* برای فایدهٔ آن بیش از حد پیچیده است.
