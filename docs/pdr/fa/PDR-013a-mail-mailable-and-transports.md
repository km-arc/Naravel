# PDR-013a — شکل Mailable، transportهای نام‌دار و adapter صف (R09.T01)

**داوری با اولویت .NET بومی:** .NET هنوز فرستندهٔ مدرن یا مدل پیام ایمیل ندارد و هیچ چیز بومی «پیام به‌صورت کلاس» با نوع‌های مشخص و قابل تست، یا انتخاب transport برای هر محیط را نمی‌دهد. Naravel فقط قرارداد نازک را روی [PDR-013](PDR-013-mail.md) می‌گذارد؛ کار واقعی SMTP همان‌طور که پیش‌تر تأیید شد در یک بستهٔ جدای MailKit می‌ماند.

وضعیت: **پیشنهادی ۲۰۲۶-۱۰-۱۴، منتظر تأیید مالک.** پیش از تأیید هیچ کدی نوشته نمی‌شود.

## چرا ضمیمه

PDR-013 (تأییدشده ۲۰۲۶-۱۰-۰۷) مرز را تعیین کرد: `IMailer`، `IMailTransport`، `MailMessage`، `MailFake`، adapter اختیاری MailKit، بدون قالب‌سازی، و **بدون driver manager مگر آنکه چند transport آن را توجیه کنند**. برنامهٔ مرحلهٔ R09 (`roadmap/stages/R09-mail.md`) جلوتر می‌رود: کلاس `Mailable`، `IMailer` روی `Manager<,>`، transportهای داخلی `log` و `array` و adapter با `QueueAsync`. این‌ها تصمیم‌هایی‌اند که PDR-013 باز گذاشته و پیش از R09.T02 باید تأیید شوند.

## تصمیم

### ۱. شکل Mailable

```csharp
public class WelcomeMail(User user) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.From("app@example.com").Subject("Welcome").Text($"Hi {user.Name}").Html($"<p>Hi {user.Name}</p>").Build();
}

await mailer.To(user.Email).SendAsync(new WelcomeMail(user), cancellationToken);
```

- `Mailable.Build` همان `MailMessage` صریح PDR-013 را برمی‌گرداند. mailable فقط یک کارخانهٔ نوع‌دار برای آن پیام است؛ بدون کشف خودکار، بدون جست‌وجوی قراردادی و بدون جادوی مدل.
- `IMailer` همان `SendAsync(MailMessage)` را نگه می‌دارد (PDR-013) و `To(address)` را اضافه می‌کند که `PendingMail` کوچکی با `SendAsync(Mailable)` برمی‌گرداند.
- **دلیل:** با ذهنیت Laravel (`Mail::to($user)->send(new Welcome($user))`) می‌خواند ولی C# ساده می‌ماند. `Build` باید خالص باشد (بدون I/O) چون adapter صف پیش از enqueue آن را صدا می‌زند.
- رندر view **شامل نیست.** `Mailable` می‌تواند `IMailRenderer` ‏(`Task<string> RenderAsync(string template, object model, CancellationToken)`) را که برنامه فراهم می‌کند صدا بزند؛ Naravel فقط آن را تعریف می‌کند و پیاده‌سازی نمی‌دهد، پس موتوری تحمیل نمی‌شود.

### ۲. transportهای نام‌دار روی `Manager<IMailTransport, MailOptions>`

transportهای `smtp`، `log` و `array` با نام از پیکربندی انتخاب می‌شوند (`Mail:Default`، `Mail:Mailers:<name>`)، با `Extend` و hot reload مثل Cache و Queue.

- **چرا احتیاط PDR-013 دربارهٔ «بدون manager» برعکس می‌شود:** با سه transport پشتیبانی‌شده و الگوی رایج «در توسعه log، در تولید SMTP، در تست array» شرطی که PDR-013 گذاشت («مگر چند transport توجیه کنند») برقرار است. برنامه‌ای با یک transport فقط `Mail:Default` را تنظیم می‌کند.
- `log`: پیام را برای توسعه با `ILogger` می‌نویسد. گیرنده‌ها و موضوع را ثبت می‌کند؛ مستندات می‌گویند در جایی که لاگ‌ها به اشتراک گذاشته می‌شوند نباید استفاده شود، چون PDR-013 گیرنده/محتوا را فقط در **telemetry** ممنوع کرده، نه در لاگر صریح توسعه.
- `array`: پیام‌های ارسال‌شده را برای تست و اجرای محلی در حافظه نگه می‌دارد. `MailFake` (در `Naravel.Mail.Testing`) `IMailer` را برای assertها پیاده می‌کند (`AssertSent`، `AssertNothingSent`، `Reset`)، همان‌طور که `QueueFake` می‌کند.

### ۳. adapter صف `Naravel.Mail.Queue`

```csharp
await mailer.To(user.Email).QueueAsync(new WelcomeMail(user), cancellationToken);
```

- `QueueAsync` ابتدا `Build` را صدا می‌زند و بعد `SendQueuedMailJob` را enqueue می‌کند که payload آن **فقط دادهٔ `MailMessage` ساخته‌شده** است (آدرس‌ها، موضوع، متن/HTML). خودِ شیء mailable هرگز serialize نمی‌شود، پس نام نوع CLR یا delegate ذخیره نمی‌شود (OD-07، PDR-013).
- job از retry/backoff معمول `Job` استفاده می‌کند. سهمیهٔ provider با middleware ‏`RateLimited` از R08 مهار می‌شود که در مستندات به‌صورت دستورالعمل اختیاری می‌آید؛ `Naravel.Mail.Queue` به `Naravel.Queue.Coordination` **وابسته نیست**.
- تحویل at-least-once است، پس تلاش مجدد پس از شکست مبهم ممکن است پیام را دوبار بفرستد. مستندات این را می‌گویند و وقتی provider پشتیبانی کند کلید idempotency در transport را توصیه می‌کنند.
- adapter به Queue ارجاع می‌دهد؛ هستهٔ `Naravel.Mail` نه (وابستگی‌ها یک‌طرفه‌اند).

### ۴. وابستگی‌ها

`Naravel.Mail.Smtp` روی MailKit سوار است (تأییدشده ۲۰۲۶-۱۰-۰۷). در R09.T03 نسخهٔ دقیق، مجوز (MIT) و ورودی `Directory.Packages.props` را پیش از merge برای تأیید پیشنهاد می‌کنم. هیچ وابستگی‌ای وارد هستهٔ `Naravel.Mail` نمی‌شود.

## خارج از محدوده

پیوست‌ها، تصویر inline، موتور قالب داخلی، APIهای مخصوص provider (SES، SendGrid و ...)، مدیریت bounce/webhook و امضای DKIM. پیوست‌ها می‌توانند بعداً با PDR خودشان بیایند چون payload سریال‌شدهٔ صف را تغییر می‌دهند.

## گزینه‌های بررسی‌شده

۱. **فقط `IMailer` + `MailMessage` (بدون Mailable).** کوچک‌ترین گزینه است ولی کلاس پیام نوع‌دار آشنای Laravel را که هدف مرحله نام می‌برد از دست می‌دهد و هر برنامه کارخانهٔ خودش را می‌نویسد.
۲. **serialize کردن کل `Mailable` برای صف.** رد شد: نوع‌های CLR دلخواه را در دادهٔ صف ذخیره می‌کرد و با تغییر نام کلاس می‌شکست.
۳. **یک transport ثبت‌شده در DI و بدون manager.** برای یک transport کار می‌کند؛ ولی جابه‌جایی برای هر محیط و `Extend` در زمان اجرا را موردی می‌کند، در حالی که Cache و Queue و Foundation همین را یکسان حل کرده‌اند.

## راستی‌آزمایی لازم پس از تأیید

تست ساخت `Mailable`، اعتبارسنجی `To(...)`، انتخاب transport و `Extend` و reload، transportهای `log` و `array`، assertهای `MailFake`، لغو و شکست transport، adapter صف با صف Memory (payload بدون نام نوع) و تست SMTP با شرط محیطی (`NARAVEL_TEST_SMTP`). مستندات EN/FA، جدول parity، changelog، چک‌لیست مرحله، راستی‌آزمایی کل solution و `roadmap/check.py`. هر چه اجرا نشود NOT RUN گزارش می‌شود.

## تأیید

هنوز تأیید نشده. پرسش‌های باز برای مالک:

- (الف) آیا شکل `Mailable` + `To(...).SendAsync(...)` پذیرفته است؟
- (ب) آیا برعکس‌کردن احتیاط «بدون manager» در PDR-013 به نفع transportهای نام‌دار (`smtp`/`log`/`array`) را می‌پذیرید؟
- (ج) آیا `QueueAsync` فقط پیام ساخته‌شده را serialize کند (پیشنهادی)، آن‌طور که شرح داده شد؟
- (د) آیا پیوست‌ها به PDR بعدی موکول شوند؟
