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
