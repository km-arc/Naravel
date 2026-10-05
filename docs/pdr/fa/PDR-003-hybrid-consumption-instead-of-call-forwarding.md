# PDR-003: مصرف ترکیبی به‌جای فوروارد `__call`

- **وضعیت:** پذیرفته‌شده
- **معادل لاراول:** `Manager::__call($method, $params)` هر متدی را به درایور پیش‌فرض فوروارد می‌کند
- **معادل native دات‌نت:** ثبت درایور پیش‌فرض به‌عنوان یک سرویس ساده در DI

## زمینه
در لاراول می‌توان `Cache::get('k')` یا `$manager->get('k')` نوشت؛ manager با متد magic به درایور پیش‌فرض فوروارد می‌کند.
.NET متد magic ندارد. گزینه‌ها:
- **A.** manager خودش اینترفیس درایور را implement و هر عضو را delegate کند (boilerplate به‌ازای هر متد، باید با تغییر اینترفیس هماهنگ بماند، یا source generator لازم است).
- **B.** همیشه `manager.Driver().Method()`.
- **C.** ترکیبی: درایور پیش‌فرض مستقیم در DI به‌عنوان `TDriver` ثبت شود؛ manager فقط برای انتخاب در runtime inject شود.

## تصمیم
**C.** `AddNaravelManager` این‌ها را ثبت می‌کند: manager، registry، options و `TDriver` (که از `manager.Driver()` resolve می‌شود).

## پیامدها
- اغلب کدها `ICacheDriver` را inject می‌کنند و manager را نمی‌بینند. بدون boilerplate delegation؛ type-safety و IntelliSense کامل.
- `TDriver` ساده یک‌بار resolve می‌شود (default در اولین تزریق). کدی که باید از تغییر default پیروی کند یا پویا انتخاب کند، manager را inject می‌کند.
- container درایور پیش‌فرض را هم dispose می‌کند؛ درایورها باید Dispose‌ای idempotent داشته باشند (در راهنمای ماژول‌نویسی آمده).

## گزینه‌های ردشده
- **A:** هزینهٔ نگهداری با هر تغییر اینترفیس بالا می‌رود؛ مسئله‌ای را حل می‌کند که DI از ریشه برطرف کرده.
- **B تنها:** برای ۹۹٪ مواردی که فقط درایور پیش‌فرض لازم است پرحرف است.
