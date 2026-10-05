# مرور کلی: چرا Naravel و Naravel.Foundation وجود دارند

## ناراول
هدف ناراول سادگی و APIهای آشنای لاراول در کنار سرعت دات‌نت است: همان مفاهیم (Cache، Queue، Mail، Filesystem، Session،
Events، Notifications ...) در ماژول‌های آشنا، اما با تایپ قوی، `async/await`، تزریق وابستگی و کتابخانه‌های پربازدهٔ بومی .NET.

**قانون parity:** ویژگی لاراول فقط وقتی پیاده می‌شود که در .NET آورده داشته باشد. در غیر این‌صورت از مکانیزم native دات‌نت
استفاده می‌شود و دلیلش در یک PDR ثبت می‌شود (`docs/pdr/fa`).

## مسئله‌ای که Foundation حل می‌کند
تقریباً هر ماژول لاراول یک شکل دارد: *یک قرارداد، چند درایور جایگزین‌پذیر، و یک مقدار کانفیگ که پیش‌فرض را انتخاب می‌کند.*
لاراول این را یک‌بار در `Illuminate\Support\Manager` پیاده کرده و همهٔ ماژول‌ها (`CacheManager`، `QueueManager`، `MailManager` ...) از آن ارث می‌برند.
بدون معادل آن، هر ماژول ناراول باید همان منطق را دوباره بنویسد و دوباره دیباگ کند: resolve درایور با نام، ساخت یک‌باره، cache،
ثبت درایور سفارشی و واکنش به تغییر کانفیگ.

`Naravel.Foundation` همین منطق مشترک است که فقط یک‌بار نوشته شده (DRY):

| دغدغه | لاراول | Naravel.Foundation |
|---|---|---|
| resolve درایور با نام، ساخت یک‌باره و cache | `Manager::driver()` | `Manager<TDriver,TOptions>.Driver / DriverAsync` |
| ثبت درایور سفارشی در runtime | `Manager::extend()` | `Manager.Extend`، `IDriverRegistry<TDriver>` |
| فراموش کردن درایورهای cache‌شده | `forgetDrivers()` | `Forget`، `ForgetAll` |
| درایور پیش‌فرض از کانفیگ | `getDefaultDriver()` + `config('x.default')` | `ManagerOptions.Default` از طریق `IOptionsMonitor` |
| شکل کانفیگ | `config/x.php` (`default`، `stores`) | `ManagerOptions` (`Default`، `Stores`) |

## Foundation چه چیزی *نیست*
- جایگزین DI container نیست. `Illuminate\Container` لاراول معادل `Microsoft.Extensions.DependencyInjection` است که دست‌نخورده می‌ماند (PDR-002).
- مجیک `Manager::__call` را پورت نمی‌کند؛ DI دات‌نت آن را غیرضروری می‌کند (PDR-003).

## چه کسی استفاده می‌کند
نویسندگان ماژول‌های ناراول (و درایورهای شخص ثالث). توسعه‌دهندهٔ اپلیکیشن معمولاً مستقیم با Foundation کار ندارد و از APIهای ماژول
مثل `ICacheDriver` یا `CacheManager` استفاده می‌کند.
