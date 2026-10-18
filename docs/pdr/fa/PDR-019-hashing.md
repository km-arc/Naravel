# PDR-019 — هش رمز عبور (`Hasher`)

**داوری با اولویت .NET بومی:** بله، .NET ساده این کار را در سه خط انجام می‌دهد: `PasswordHasher<TUser>` از `Microsoft.Extensions.Identity.Core` هش می‌کند، راستی‌آزمایی می‌کند و می‌گوید چه زمانی هش ذخیره‌شده باید ارتقا یابد. wrapper در Naravel فقط API دومی برای همین سه فراخوانی اضافه می‌کند. **نتیجهٔ پیشنهادی: `DECLINED` همراه با یک دستور‌العمل مستند.**

وضعیت: **پیشنهادشده، در انتظار تأیید مالک.** تا پاسخ مالک چیزی ساخته نمی‌شود؛ بخش *تأیید* را ببینید.

## زمینه

facade با نام `Hash` در Laravel (`Illuminate\Contracts\Hashing\Hasher`) سه فراخوانی برای برنامه دارد: `Hash::make($value, $options)`، `Hash::check($plain, $hashed)` و `Hash::needsRehash($hashed)`. سه درایور دارد (`bcrypt` که پیش‌فرض است، `argon` و `argon2id`)، گزینه‌های هزینهٔ کار می‌گیرد (`rounds`؛ `memory`، `time`، `threads`) و `check` وقتی هش با الگوریتمی غیر از الگوریتم انتخاب‌شده ساخته شده باشد خطا می‌دهد (هنگام مهاجرت با `HASH_VERIFY=false` خاموش می‌شود). منبع: [Laravel 12 hashing](https://laravel.com/docs/12.x/hashing).

آنچه کتابخانهٔ پایهٔ .NET و ASP.NET Core از قبل دارند:

- `PasswordHasher<TUser>` (در `Microsoft.Extensions.Identity.Core`، بدون وابستگی وب): `HashPassword(user, password)` و `VerifyHashedPassword(user, hash, password)` که `Failed`، `Success` یا `SuccessRehashNeeded` برمی‌گرداند.
- قالب «Identity V3»: PBKDF2 با HMAC-SHA512، نمک تصادفی ۱۶ بایتی، کلید ۳۲ بایتی و سرآیندی که نشانگر قالب و تعداد تکرار را ذخیره می‌کند. پیش‌فرض ۱۰۰٬۰۰۰ تکرار است (`PasswordHasherOptions.IterationCount`). چون تعداد داخل هش ذخیره می‌شود، هشی که با تعداد قدیمی و کمتر ساخته شده هنوز تأیید می‌شود و با `SuccessRehashNeeded` گزارش می‌شود. منابع: [PasswordHasher.cs](https://source.dot.net/Microsoft.Extensions.Identity.Core/PasswordHasher.cs.html)، [PasswordHasherOptions.cs](https://source.dot.net/Microsoft.Extensions.Identity.Core/PasswordHasherOptions.cs.html).
- آرگومان `user` پذیرفته می‌شود ولی پیاده‌سازی آن را نمی‌خواند؛ پس نوعش فقط باید class باشد.
- bcrypt یا Argon2 در BCL وجود ندارد. هر دو کتابخانهٔ شخص ثالث می‌خواهند.

## دستور‌العملی که جای بسته را می‌گیرد

```csharp
// dotnet add package Microsoft.Extensions.Identity.Core
services.Configure<PasswordHasherOptions>(o => o.IterationCount = 250_000);   // بخش «هزینهٔ کار» را ببینید
services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

var hash   = hasher.HashPassword(user, password);                              // Hash::make
var result = hasher.VerifyHashedPassword(user, hash, password);                // Hash::check + Hash::needsRehash
if (result == PasswordVerificationResult.SuccessRehashNeeded) hash = hasher.HashPassword(user, password); // ذخیره کنید
```

| Laravel | .NET |
|---|---|
| `Hash::make` | `HashPassword` |
| `Hash::check` | `VerifyHashedPassword` مقدار `Success` یا `SuccessRehashNeeded` برمی‌گرداند |
| `Hash::needsRehash` | `SuccessRehashNeeded`. هنگام راستی‌آزمایی گزارش می‌شود که تنها لحظه‌ای است که رمز ساده برای هش دوباره در دسترس است. |
| `rounds` | `PasswordHasherOptions.IterationCount` |
| `argon`، `argon2id` | در BCL نیست. ارائه نمی‌شود (گزینهٔ ۲ را ببینید). |
| بررسی الگوریتم (`HASH_VERIFY`) | لازم نیست: هش Identity نشانگر قالب خودش را دارد و نشانگر ناشناخته `Failed` برمی‌گرداند. |

## هزینهٔ کار (تنها نکته‌ای که باید صریح گفته شود)

پیش‌فرض ۱۰۰٬۰۰۰ تکرار کمتر از راهنمای فعلی OWASP برای PBKDF2-HMAC-SHA512 است (۲۲۰٬۰۰۰؛ و ۶۰۰٬۰۰۰ برای HMAC-SHA256). OWASP الگوریتم Argon2id را اول می‌گذارد و PBKDF2 را وقتی ترجیح می‌دهد که FIPS-140 لازم باشد. بنابراین دستور‌العمل تعداد را صریح تنظیم می‌کند. عدد را با اندازه‌گیری روی سخت‌افزار تولید انتخاب کنید (معمولاً چند صد میلی‌ثانیه) و بعداً بالا ببرید: هش‌های موجود همچنان تأیید می‌شوند و در ورود بعدی ارتقا می‌یابند. منبع: [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html).

## تصمیم پیشنهادی

۱. بستهٔ `Naravel.Hashing` اضافه نشود. `DECLINED` با همین PDR به‌عنوان دلیل ثبت شود.
۲. دستور‌العمل بالا در `docs/en|fa/hashing.md` (EN و FA با هم) مستند شود، با نکتهٔ هزینهٔ کار و الگوی ارتقا هنگام ورود.
۳. ردیف ماتریس parity به *Native*، و changelog و backlog به‌روز شوند.
۴. کد، تست و benchmark اضافه نمی‌شود: رفتار تازه‌ای برای تست وجود ندارد و تست `PasswordHasher` یعنی تست کد مایکروسافت.

## گزینه‌های بررسی‌شده

۱. **`IHasher` نازک با `Make`، `Check`، `NeedsRehash` روی `IPasswordHasher`.** رد شد. سه فراخوانی را فقط تغییر نام می‌دهد، یک بسته و شکل سومی از API در کنار `IPasswordHasher` اضافه می‌کند که کاربران ASP.NET Core Identity از قبل می‌شناسند و رفتاری ندارد که دستور‌العمل نداشته باشد. قاعدهٔ صفر را رد می‌کند.
۲. **افزودن bcrypt یا Argon2id با بستهٔ شخص ثالث** (مثلاً برای مهاجرت از Laravel که باید هش‌های موجود `$2y$` را تأیید کند، یا برای سیاست Argon2id). فعلاً رد شد: وابستگی جدید PDR جدا می‌خواهد (OD-01/OD-02) و نیاز محدود است. در backlog ثبت می‌شود و اگر سناریوی مهاجرت تأییدشده توسط مالک لازم داشت دوباره بررسی می‌شود.
۳. **wrapper PBKDF2 خودمان روی `Rfc2898DeriveBytes`.** رد شد. قالب Identity را تکرار می‌کند و دقیقاً از همان کدهای حساس امنیتی است که پروژه نمی‌نویسد (OD-03).

## قراردادها و کیفیت

اگر مالک به‌جای آن گزینهٔ ۱ یا ۲ را انتخاب کند، مرحله این‌ها را لازم دارد: نبودن رمز ساده یا هش در لاگ، استثنا یا tagهای telemetry؛ API بدون async (هش‌کردن CPU-bound است، پس async ساختگی نه)؛ `Meter` فقط با outcomeهای محدود؛ fake درون‌حافظه‌ای که ادعای امن‌بودن نکند؛ تست‌های known-answer، verify و rehash.

## تأیید

در انتظار. از مالک خواسته می‌شود یکی را تأیید کند: **الف.** `DECLINED` با دستور‌العمل (پیشنهادی)، **ب.** `IHasher` نازک، **ج.** بستهٔ شخص ثالث bcrypt/Argon2id با PDR جدید. تا آن زمان مرحله مسدود می‌ماند.
