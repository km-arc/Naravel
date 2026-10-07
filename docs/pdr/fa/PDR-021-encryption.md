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
