# PDR-013 — ایمیل (`Naravel.Mail`)

**داوری با اولویت .NET بومی:** .NET فرستندهٔ مدرن و عمومی برای ایمیل ندارد؛ `System.Net.Mail.SmtpClient` محدود است و نباید انتخاب پیش‌فرض کد جدید باشد. Naravel می‌تواند قرارداد typed ایمیل اضافه کند، اما وابستگی transport مدرن باید اختیاری و نیازمند تأیید صریح باشد.

وضعیت: **تأییدشده در ۲۰۲۶-۱۰-۰۷.**

## زمینه

Laravel یک abstraction برای mailer و mailable ارائه می‌کند، در حالی که برنامه‌های .NET معمولاً مستقیماً provider را انتخاب می‌کنند. facade typed کوچک می‌تواند پیام‌ها را قابل تست کند و کد برنامه را از جزئیات transport جدا نگه دارد. این ماژول نباید به موتور template یا تکرار قابلیت‌های اختصاصی provider تبدیل شود.

سطح Laravel: `Illuminate\Contracts\Mail\Mailer` و mailableها. مبنای بومی: `System.Net.Mail`، `HttpClientFactory` و SDKهای provider. ارزش افزوده، قراردادی پایدار در Naravel با مرز transport صریح و امکان اتصال اختیاری به Queue است.

## تصمیم پیشنهادی

`Naravel.Mail` با قراردادهای typed برای پیام و ارسال اضافه شود. API نخست فرستنده، گیرنده‌ها، subject، متن و body اختیاری HTML را پشتیبانی می‌کند؛ attachment، template، کشف provider و پیکربندی global سازگار با Laravel خارج از محدوده‌اند.

حالت رایج:

```csharp
services.AddNaravelMail();
await mailer.SendAsync(new MailMessage(from, to, "Receipt", text), cancellationToken);
```

قراردادهای `IMailer` و `IMailTransport` تعریف شوند. تا وقتی چند transport پشتیبانی‌شده آن را توجیه نکرده‌اند، manager درایوری اضافه نشود؛ برای انتخاب transport از DI معمول استفاده شود. برای assertionها `MailFake` ارائه شود و از state سراسری پنهان پرهیز شود.

بستهٔ اصلی transport شبکه‌ای پیش‌فرض انتخاب نمی‌کند. adapter جداگانه و قابل نصب برای SMTP/provider می‌تواند از MailKit استفاده کند، اما این وابستگی و جداسازی بسته پیش از پیاده‌سازی به تأیید مالک نیاز دارد. به‌عنوان fallback راحت، transport تولیدی بر پایهٔ `SmtpClient` منسوخ ساخته نشود. ارسال async و cancellable باشد؛ خطای transport از پذیرش موفق جدا گزارش شود و ارسال غیر idempotent بی‌صدا retry نشود.

ارسال اختیاری از طریق Queue می‌تواند پس از R07 در adapter جدا ارائه شود؛ dispatch مستقیم در caller رفتار اصلی هسته باقی می‌ماند.

## قراردادها و کیفیت

- دادهٔ پیام صریح باشد و در صورت افزودن adapter صف قابل serialization بماند؛ delegate یا نام نوع CLR ذخیره نشود.
- قالب آدرس گیرنده و فرستنده در مرز ورودی اعتبارسنجی و خطای provider صریح گزارش شود.
- `MailFake` پیام‌ها را ثبت کند و assertion/reset فراهم آورد.
- اگر transport شبکه‌ای پیاده‌سازی شد، `Meter` و `ActivitySource` با tagهای کم‌کاردینالیتی و بدون محتوای پیام/گیرنده ارائه شود.
- موتور HTML rendering، جست‌وجوی ضمنی template و magic مدل در محدوده نیست.

## راستی‌آزمایی لازم پس از تأیید

ساخت پیام، اعتبارسنجی گیرنده، cancellation، خطاهای transport، assertionهای fake و رفتار adapterها تست شوند. transport adapter تست‌های متمرکز خود را دارد و نباید وابستگی هسته باشد. مستندات EN/FA، سوابق parity، changelog و checklist مرحله به‌روزرسانی شوند؛ سپس راستی‌آزمایی solution و roadmap checker اجرا شود.

## گزینه‌های بررسی‌شده

1. **استفاده از `System.Net.Mail.SmtpClient` به‌عنوان transport داخلی.** به‌عنوان پیش‌فرض کد جدید رد شد؛ محدودیت‌ها و نبود قرارداد مناسب میان providerها.
2. **وابستگی MailKit در بستهٔ اصلی.** رد شد؛ کاربر فقط در صورت انتخاب transport باید وابستگی شبکه‌ای بگیرد.
3. **استفادهٔ مستقیم از SDK provider در برنامه.** برای نیازهای اختصاصی معتبر می‌ماند، اما seam مشترک تست یا ارسال queued اختیاری نمی‌دهد.
4. **افزودن موتور template و کشف mailable مشابه Laravel.** به‌دلیل scope غیرضروری و magic زمان اجرا رد شد.

## تأیید

مالک این پیشنهاد را در ۲۰۲۶-۱۰-۰۷، شامل قرارداد `Naravel.Mail` و transport جداگانهٔ مبتنی بر MailKit تأیید کرد. این تأیید مربوط به R09 است و افزودن MailKit به بستهٔ core را مجاز نمی‌کند.
