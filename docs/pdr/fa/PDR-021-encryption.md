# PDR-021 — facade رمزگذاری و jobهای رمزگذاری‌شده (`Naravel.Encryption`)

**داوری با اولویت .NET بومی:** ASP.NET Core Data Protection از قبل حفاظت اصالت‌سنجی‌شده، جداسازی purpose و مدیریت کلید را فراهم می‌کند. Naravel نباید primitive رمزنگاری پیاده‌سازی کند؛ ارزش افزوده به facade کوتاه و اتصال opt-in صریح به Queue محدود است.

وضعیت: **تأییدشده در ۲۰۲۶-۱۰-۰۷.** OD-03 جهت Data Protection را تأیید می‌کند؛ مالک همچنین API facade، مرز ثبت صریح job، adapter صف و رویکرد سازگاری envelope را تأیید کرده است.

## زمینه

Laravel عملیات راحت encrypt/decrypt دارد و می‌تواند jobهای صف را رمزگذاری‌شده علامت بزند. برنامه‌های .NET برای اغلب نیازها باید مستقیماً از `IDataProtectionProvider` و `IDataProtector` استفاده کنند. facade نازک می‌تواند عملیات رایج را یکسان کند؛ jobهای رمزگذاری‌شده به سازگاری، key ring و رفتار registry alias نیازمند دقت‌اند.

سطح Laravel: `Illuminate\Encryption\Encrypter`، `Crypt` و `ShouldBeEncrypted`. مبنای بومی: ASP.NET Core Data Protection. رمزنگاری سفارشی، انتخاب الگوریتم و قالب کلید مستقل پیشنهاد نمی‌شود.

## تصمیم پیشنهادی

بستهٔ نازک `Naravel.Encryption` فقط بر پایهٔ Data Protection اضافه شود. purpose مختص برنامه پیکربندی و یک سرویس کوچک typed برای حفاظت/بازیابی رشته ارائه شود. facade نباید کلیدها را آشکار کند یا ادعای سازگاری ciphertext با Laravel داشته باشد.

حالت رایج:

```csharp
services.AddNaravelEncryption();
var protectedValue = protector.Protect(value);
var value = protector.Unprotect(protectedValue);
```

facade ساده synchronous است، چون primitive مربوط به Data Protection هم synchronous است؛ caller باید persistence key و تنظیمات حفاظت-at-rest متناسب با deployment را در ASP.NET Core انجام دهد. wrapper ناهمگام ساختگی یا blocking بر عملیات async ایجاد نشود.

برای اتصال Queue، adapter opt-in در `Naravel.Queue.Encryption` و policy صریح marker/registration برای job پیشنهاد می‌شود. رمزگذاری روی payload سریال‌شده انجام شود و registry صریح alias فعلی job حفظ شود (OD-07)؛ payload فقط پس از انتخاب نوع job از alias ثبت‌شده decrypt و deserialize شود. jobهای بدون marker همان wire format فعلی را نگه دارند. rotation کلید، کلید در دسترس‌نبودنی و ناسازگاری باید آشکارا fail شوند؛ برای job علامت‌خورده هرگز به plaintext fallback نشود.

marker/API دقیق و نسخه‌بندی envelope برای تأیید مالک باز است و پیش از پیاده‌سازی باید با مسئولان Queue نهایی شود. jobها و deploymentهای فعلی به راهبرد migration مستند نیاز دارند؛ payload همهٔ صف‌ها بی‌صدا تغییر نکند.

## قراردادها و کیفیت

- فقط از `IDataProtectionProvider`/`IDataProtector` استفاده شود؛ رمزنگاری پیاده‌سازی یا کلید خام در داده‌های Naravel ذخیره نشود.
- purposeها بر اساس برنامه و feature جدا شوند؛ مسئولیت persistence و دسترسی مشترک key ring بین producer و worker بر عهدهٔ caller است.
- دادهٔ نامعتبر، دستکاری‌شده یا غیرقابل decrypt با خطای روشن گزارش شود.
- اگر adapter صف telemetry منتشر کند، `Meter`/`ActivitySource` با outcomeهای محدود به‌کار رود؛ plaintext، payload رمز‌شده، alias حاوی دادهٔ کاربر یا کلید material هرگز tag نشود.
- fake درون‌حافظه‌ای فقط وقتی اضافه شود که قرارداد encryption مفید برای برنامه وجود داشته باشد؛ تضمین رمزنگاری fake نشود.

## راستی‌آزمایی لازم پس از تأیید

رفت‌وبرگشت، تشخیص دستکاری، جداسازی purpose، persistence کلید میان providerها، cancellation در صورت کاربرد و رفتار خطا تست شوند. تست adapter صف jobهای علامت‌خورده/بدون marker، انتخاب نوع فقط با alias، سازگاری payload، کلید ناموجود و نبود fallback به plaintext را پوشش دهد. مستندات EN/FA، parity، changelog و checklist مرحله به‌روزرسانی و راستی‌آزمایی کامل solution و roadmap checker اجرا شود.

## گزینه‌های بررسی‌شده

1. **استفادهٔ مستقیم از Data Protection در همه‌جا.** برای برنامه‌هایی که API مشترک Naravel یا قرارداد job رمزگذاری‌شده نمی‌خواهند ترجیح داده می‌شود.
2. **پیاده‌سازی رمزگذاری سازگار با Laravel یا AES/مدیریت کلید سفارشی.** رد شد؛ primitive حساس امنیتی را تکرار می‌کند و صریحاً خارج از OD-03 است.
3. **رمزگذاری پیش‌فرض همهٔ payloadهای job.** رد شد؛ سازگاری wire را تغییر می‌دهد، key ring مشترک می‌خواهد و ممکن است workerهای فعلی را بشکند.
4. **حل نوع CLR از metadata payload رمز‌شده.** طبق OD-07 رد شد؛ registry alias تنها روش انتخاب نوع باقی می‌ماند.

## تأیید

مالک این پیشنهاد را در ۲۰۲۶-۱۰-۰۷ تأیید کرد. OD-03 همچنان الزام‌آور است: پیاده‌سازی باید از Data Protection استفاده کند و نباید رمزنگاری سفارشی اضافه کند.

## اصلاحیه A1 (۲۰۲۶-۱۰-۲۴، تأییدشده ۲۰۲۶-۱۰-۲۴): API دقیق، envelope و قاعده‌ی مهاجرت

تأیید بالا جهت را ثابت کرد ولی نشانگر دقیق، API و envelope را باز گذاشت («برای تأیید مالک باز است و پیش از پیاده‌سازی باید قطعی شود»). R18.T01 اینجا آن را می‌بندد. چیزی از رمزنگاری اضافه نمی‌شود: هر بایت حفاظت از `IDataProtector` می‌آید.

### API پیشنهادی (S1: یک خط ثبت، دو فراخوانی)

```csharp
services.AddNaravelEncryption();   // اگر برنامه AddDataProtection() نزده باشد آن را ثبت می‌کند، و IEncrypter را

var token = encrypter.Encrypt("secret");              // Laravel: Crypt::encryptString
var text  = encrypter.Decrypt(token);                 // Crypt::decryptString
var card  = encrypter.Decrypt<CardInfo>(encrypter.Encrypt(new CardInfo(...)));   // Crypt::encrypt / decrypt، JSON درون envelope
```

- `IEncrypter` این‌ها را دارد: `Encrypt(string)`، `Decrypt(string)`، `TryDecrypt(string, out string?)`، `Encrypt<T>(T)`، `Decrypt<T>(string)` (مقدار تایپ‌دار از `System.Text.Json` می‌گذرد؛ یک overload `JsonTypeInfo<T>` می‌گیرد تا فراداده‌ی source-generated کار کند) و `ForPurpose(string)` که یک `IEncrypter` جداشده زیر یک purpose فرعی برمی‌گرداند.
- همه‌ی اعضا **همگام** هستند، همان‌طور که تأیید شد: primitive همگام است و پوشش async ساخته نمی‌شود.
- مقداری که رمزگشایی نشود (دست‌کاری‌شده، purpose اشتباه، کلید گم یا باطل‌شده، بدشکل) `DecryptException` می‌دهد (Laravel: `DecryptException`) با پیام ثابت؛ پیام و داده‌ی استثنا هرگز ورودی یا متن ساده ندارند. `TryDecrypt` به‌جای استثنا false برمی‌گرداند.
- purpose پیش‌فرض `Naravel.Encryption.v1` است؛ `ForPurpose("billing")` همان را با `/billing` می‌دهد. purposeهای متفاوت خروجی هم را نمی‌خوانند.
- حلقه‌ی کلید: facade به کلیدها دست نمی‌زند. ماندگاری، حفاظت در حالت سکون، چرخش (Data Protection تقریباً هر ۹۰ روز کلید تازه می‌سازد و کلیدهای قدیمی را برای رمزگشایی نگه می‌دارد) و نام مشترک برنامه همه تنظیمات `AddDataProtection()` خود برنامه‌اند. تولیدکننده و کارگر صف **باید حلقه‌ی کلید و نام برنامه را مشترک داشته باشند**؛ مستندات اول همین را می‌گویند.
- تله‌متری: یک `Meter` با نام `Naravel.Encryption` و شمارنده‌ی `naravel.encryption.operations` فقط با برچسب `operation` (`encrypt`، `decrypt`) و `outcome` (`ok`، `invalid`)؛ هرگز متن ساده، متن رمزشده، purpose برآمده از ورودی کاربر یا داده‌ی کلید.
- بدون حفاظت زمان‌دار، بدون متن رمزشده‌ی سازگار با Laravel، بدون facade استاتیک `Crypt::` (از DI استفاده کنید).

### jobهای رمزگذاری‌شده (`Naravel.Queue.Encryption`)

- **نشانگر:** یک interface خالی `IShouldBeEncrypted` در `Naravel.Queue.Encryption`. کلاس job با پیاده‌سازی آن انتخاب می‌کند (Laravel: `ShouldBeEncrypted`). `Naravel.Queue` به Encryption یا نشانگر ارجاع نمی‌دهد.
- **درز:** `AddNaravelQueueEncryption()` مقدار `IJobSerializer` را با decorator روی serializer ثبت‌شده جایگزین می‌کند. `Serialize(job)` برای job بدون نشانگر همان payload درونی را بی‌تغییر می‌نویسد، و برای job نشان‌دار `naravel:enc:v1:` را به‌علاوه‌ی خروجی Data Protection از payload درونی، زیر purpose `Naravel.Queue.Encryption.v1` می‌نویسد. `Deserialize(payload, type)` فقط بعد از آنکه ثبت alias نوع را انتخاب کرد صدا زده می‌شود (OD-07)، پس رمزگشایی هرگز نوع را انتخاب نمی‌کند.
- **زنجیره‌ها، batchها و jobهای شکست‌خورده** از قبل رشته‌ی serialize‌شده را حمل می‌کنند، پس متن رمزشده را حمل می‌کنند و تغییری نمی‌خواهند.
- **قاعده‌های شکست:** کلید در دسترس نبودن، دست‌کاری payload یا purpose اشتباه باعث می‌شود job با `DecryptException` از مسیر عادی jobهای شکست‌خورده شکست بخورد؛ job نشان‌دار هرگز به‌صورت متن ساده نوشته یا فرستاده نمی‌شود، و نسخه‌ی envelope یعنی `v1` بررسی می‌شود و نسخه‌ی ناشناخته شکست می‌خورد.
- **مهاجرت:** jobی که پیش از نشان‌دار شدن کلاسش به‌صورت متن ساده در صف رفته، به‌طور پیش‌فرض با پیام روشن **رد می‌شود** (بدون متن ساده‌ی بی‌صدا). `QueueEncryptionOptions.AcceptPlaintextForMarkedJobs` (پیش‌فرض `false`) را می‌شود برای یک بازه‌ی استقرار روشن کرد تا jobهای در جریان تمام شوند، و بعد خاموش کرد. jobهای بدون نشانگر قالب فعلی را نگه می‌دارند؛ چیزی به‌طور پیش‌فرض رمزگذاری نمی‌شود.

### تصمیم‌ها برای مالک

- **E1 نام‌ها:** `IEncrypter.Encrypt`/`Decrypt` به سبک Laravel (توصیه‌شده)، یا `Protect`/`Unprotect` شروع سریع قبلی.
- **E2 جای نشانگر:** `IShouldBeEncrypted` در بسته‌ی adapter (توصیه‌شده: هسته‌ی صف از آن آزاد می‌ماند، همان‌طور که تصمیم مرحله می‌خواهد)، یا در هسته‌ی `Naravel.Queue`.
- **E3 envelope:** پیشوند متنی `naravel:enc:v1:` (توصیه‌شده: نمی‌تواند شروع یک payload از نوع JSON باشد، پس متن ساده و رمزشده بدون parse از هم تشخیص داده می‌شوند، و نسخه، قالب بعدی را ممکن می‌کند).
- **E4 گزینه‌ی مهاجرت:** شامل کردن `AcceptPlaintextForMarkedJobs` با پیش‌فرض خاموش (توصیه‌شده)، یا بدون گزینه و تخلیه‌ی صف پیش از نشان‌دار کردن job.
- **E5 purpose facade و purpose job:** purposeهای جدا (توصیه‌شده)، تا مقداری از `IEncrypter` هرگز به‌عنوان payload یک job پذیرفته نشود.
- **E6 ارجاع بسته:** facade به `Microsoft.AspNetCore.DataProtection` ارجاع می‌دهد (توصیه‌شده تا `AddNaravelEncryption()` یک خط باشد؛ abstractions را هم می‌آورد)، یا فقط به `Microsoft.AspNetCore.DataProtection.Abstractions` و برنامه خودش `AddDataProtection()` را صدا می‌زند. نسخه هنگام پیاده‌سازی از NuGet قفل می‌شود.
- **E7 Fake:** بدون `EncryptionFake` (توصیه‌شده: مرحله گفته تضمین‌های رمزنگاری را جعل نکنیم، و `EphemeralDataProtectionProvider` یک جایگزین آزمون واقعی، درون حافظه و بدون ماندگاری است که Data Protection خودش دارد؛ مستندات تنظیم دو خطی آزمون را نشان می‌دهند)، یا یک fake برچسب‌خورده و برگشت‌پذیر.

### آزمون‌های لازم (T02، T03)

رفت‌وبرگشت (رشته و تایپ‌دار)، تشخیص دست‌کاری، جداسازی purpose، `ForPurpose`، ماندگاری کلید بین دو نمونه‌ی provider، `TryDecrypt`، پیام خطایی که هرگز ورودی را ندارد. adapter: jobهای نشان‌دار و بدون نشانگر، payload ذخیره‌شده متن ساده نیست و با پیشوند envelope شروع می‌شود، job روی صف Memory همچنان اجرا می‌شود، انتخاب نوع فقط با alias، کلید گم‌شده و payload دست‌کاری‌شده بدون بازگشت به متن ساده آشکارا شکست می‌خورند، نسخه‌ی envelope ناشناخته، متنِ سادهٔ پیش از نشان‌دار شدن رد می‌شود و با گزینه پذیرفته می‌شود، زنجیره‌ها و jobهای شکست‌خورده متن رمزشده را حمل می‌کنند.

### تأیید A1

مالک در ۲۰۲۶-۱۰-۲۴، پس از نوشته شدن دلایل زیر، E1 تا E7 را مطابق توصیه تأیید کرد. برای مرور خواننده‌ی بعدی ثبت شده است.

- **E1 `Encrypt`/`Decrypt`:** اسم‌ها مثل Laravel خوانده می‌شوند که هدف بسته است؛ هزینه یک شکاف کوچک نام با `Protect`/`Unprotect` در Data Protection است که جدولی در مستندات آن را پل می‌زند.
- **E2 نشانگر در adapter:** تصمیم مرحله می‌گوید هسته‌ی صف نباید به رمزنگاری وابسته شود؛ هزینه این است که پروژه‌ی job برای یک interface خالی به بسته‌ی adapter ارجاع می‌دهد.
- **E3 پیشوند `naravel:enc:v1:`:** شروع یک payload از نوع JSON نمی‌تواند باشد، پس متن رمزشده و ساده بدون parse فرق دارند و `v1` جا برای قالب تازه می‌گذارد. نوع job همچنان فقط با alias انتخاب می‌شود (OD-07).
- **E4 گزینه‌ی مهاجرت با پیش‌فرض خاموش:** job متن ساده از کلاسی که تازه نشان‌دار شده رد می‌شود تا حفاظت بی‌صدا حذف نشود؛ `AcceptPlaintextForMarkedJobs` کلیدی موقت برای بازه‌ی استقرار است. بدون آن باید صف را پیش از نشان‌دار کردن job خالی کرد.
- **E5 دو purpose:** مقداری از `IEncrypter` هرگز به‌عنوان payload job پذیرفته نمی‌شود، با هزینه‌ای تقریباً صفر.
- **E6 ارجاع به `Microsoft.AspNetCore.DataProtection` کامل:** `AddNaravelEncryption()` را یک خط نگه می‌دارد؛ هزینه یک وابستگی بیشتر است که نسخه‌اش از NuGet قفل می‌شود و مالک پیش از مرج تأییدش می‌کند (مثل Cronos).
- **E7 بدون `EncryptionFake`:** fake برگشت‌پذیر یک تضمین رمزنگاری را جعل می‌کرد که این PDR منع کرده است؛ `EphemeralDataProtectionProvider` جایگزین واقعی درون‌حافظه است. قاعده‌ی عمومی «هر ماژول Fake دارد» اینجا با متن PDR کنار گذاشته شده و مستندات این را می‌گویند.

**الزام عملیاتی (مالک خواست برجسته مستند شود).** وقتی تولیدکننده‌ی یک job و کارگرش برنامه یا نمونه‌ی جدا هستند، **باید key ring و application name در Data Protection را مشترک داشته باشند**. وگرنه کارگر نمی‌تواند رمزگشایی کند و هر job نشان‌دار با `DecryptException` شکست می‌خورد. به‌طور مشخص:

- در همه‌ی برنامه‌هایی که داده را به اشتراک می‌گذارند `SetApplicationName(...)` را با یک مقدار صدا بزنید؛
- key ring را در جایی ذخیره کنید که همه بتوانند بخوانند (پوشه‌ی مشترک، پایگاه داده، Redis یا blob storage) و در حالت سکون محافظت کنید، چون Data Protection روی بعضی میزبان‌ها کلیدها را بدون رمز می‌نویسد؛
- Data Protection تقریباً هر ۹۰ روز کلید تازه می‌سازد و کلیدهای قدیمی را برای رمزگشایی داده‌ی قدیمی نگه می‌دارد؛ تا وقتی ممکن است job یا مقدار رمزشده وجود داشته باشد هرگز key ring را حذف نکنید؛
- key ring جدا برای هر نمونه (پیش‌فرض روی کانتینر با فایل‌سیستم موقت) رمزگشایی را بین نمونه‌ها و بعد از هر استقرار مجدد می‌شکند.

`docs/en|fa/encryption.md` با همین بخش و پیش از شروع سریع باز می‌شود، و پیام شکست یک job نشان‌دار key ring را اولین چیز برای بررسی نام می‌برد. T02 و T03 تا وجود مستندات و آزمونی برای این پیام تمام نیستند.
