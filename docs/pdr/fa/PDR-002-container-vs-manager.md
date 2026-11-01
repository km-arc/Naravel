# PDR-002: Container در برابر Manager — Foundation چه چیزی را جایگزین می‌کند (و چه چیزی را نه)

- **وضعیت:** پذیرفته‌شده
- **معادل لاراول:** `Illuminate\Container\Container` (IoC) و `Illuminate\Support\Manager` (کارخانهٔ درایور)
- **معادل native دات‌نت:** `Microsoft.Extensions.DependencyInjection` (+ Keyed Services از .NET 8)

## زمینه
پیشنهاد Foundation به‌صورت «استخراج منطق از `illuminate/container` + `illuminate/support`» مطرح شد. این‌ها دو لایهٔ متفاوت‌اند:
- **Container** انتزاع را به پیاده‌سازی وصل می‌کند و مفهومی از «N پیاده‌سازی نام‌دار از یک قرارداد که در runtime با رشته انتخاب می‌شود» ندارد.
- **Manager** لایه‌ای است که لاراول دقیقاً برای همین روی container ساخته است.

container دات‌نت لایهٔ اول را پوشش می‌دهد. Keyed Services بخشی از لایهٔ دوم را پوشش می‌دهد، اما:
۱. ثبت keyed فقط **قبل از build شدن** provider ممکن است؛ ناراول به `extend()` در runtime نیاز دارد.
۲. DI برای ساخت **async** درایور تضمین ساخت یک‌باره نمی‌دهد.
۳. منطق default مبتنی بر کانفیگ و invalidate وجود ندارد.

## تصمیم
- container دات‌نت دست‌نخورده می‌ماند. container ناراول **نمی‌سازیم**.
- `Manager<TDriver,TOptions>` + `IDriverRegistry<TDriver>` را در `Naravel.Foundation` فقط برای پر کردن همین شکاف‌ها می‌سازیم.
- Keyed DI منبع حقیقت نیست؛ registry منبع حقیقت است.

## پیامدها
- آوردهٔ Foundation متمرکز است در: `Extend` در runtime، ساخت یک‌باره و async، default و invalidate مبتنی بر کانفیگ، و یک قرارداد مشترک برای همهٔ ماژول‌ها.
- اگر نیاز به runtime-extend نبود، Keyed DI ساده + `IOptions` بخش زیادی از این را پوشش می‌داد.

## گزینه‌های ردشده
- *IoC container سفارشی:* تکرار یک جزء بالغ و کاملاً یکپارچه.
- *فقط Keyed DI:* نیاز runtime-extend را برآورده نمی‌کند.
- *managerهای ad-hoc برای هر ماژول:* یک منطق را N بار تکرار و واگرا می‌کند.


## الحاقیه — ثبت driver داخلی با دسترسی به provider (۲۰۲۶-۱۱-۰۱)

گاهی یک ماژول هنگام ثبت factoryهای driver داخلی به سرویس‌های container ساخته‌شده نیاز دارد (Filesystem مقدار زندهٔ `IOptionsMonitor<FilesystemOptions>.CurrentValue` را می‌خواند). این ثبت‌ها همچنان باید در همان `IDriverRegistry<TDriver>` مشترک Foundation انجام شوند؛ ماژول نباید registry خصوصی بسازد و ثبت‌های `AddNaravelDriver` را دور بزند.

- overload عمومی فعلی `ConfigureNaravelDrivers<TDriver>(Action<IDriverRegistry<TDriver>>)` بدون تغییر حفظ شود.
- overload جدید `ConfigureNaravelDrivers<TDriver>(Action<IServiceProvider, IDriverRegistry<TDriver>>)` برای ثبت‌های deferred که به `IServiceProvider` ریشه نیاز دارند اضافه شود.
- هر دو overload callbackها را در یک توالی مرتب ثبت می‌کنند. callbackها یک‌بار، هنگام ساخته‌شدن اولین registry singleton اجرا می‌شوند؛ در برخورد نام، ثبت دیرتر برنده است.
- provider ارسال‌شده، provider ریشه است؛ از resolve کردن سرویس‌های scoped از آن خودداری کنید.
- Filesystem از این overload برای خواندن `IOptionsMonitor<FilesystemOptions>.CurrentValue` هنگام ساخت registry استفاده می‌کند؛ در نتیجه هر دو ورودی `IConfiguration` و `Action<FilesystemOptions>` حفظ می‌شوند و registry دومی ساخته نمی‌شود.

**تأیید مالک:** overload عمومی افزایشی در ۲۰۲۶-۱۱-۰۱ تأیید شد و تأیید آن در [PR #200](https://github.com/km-arc/Naravel/pull/200) ثبت شده است. overload یک‌آرگومانی قبلی برای سازگاری source بدون تغییر می‌ماند.
