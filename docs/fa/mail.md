# ایمیل (Mail)

`Naravel.Mail` یک قرارداد تایپ‌شده برای ایمیل است: کلاس `Mailable` پیام را توصیف می‌کند، یک *transport* نام‌دار آن را تحویل می‌دهد و یک fake آن را قابل تست می‌کند. بستهٔ هسته **هیچ وابستگی شبکه‌ای ندارد**؛ SMTP در بستهٔ اختیاری `Naravel.Mail.Smtp` و ارسال صف‌بندی‌شده در adapter اختیاری `Naravel.Mail.Queue` است (adapter صف همین حالا موجود است؛ SMTP در گام بعدی R09.T03 می‌آید؛ این صفحه مشخص می‌کند چه چیزی همین امروز موجود است).

تصمیم‌ها: [PDR-013](../pdr/fa/PDR-013-mail.md) و [PDR-013a](../pdr/fa/PDR-013a-mail-mailable-and-transports.md). نسخهٔ انگلیسی: [docs/en/mail.md](../en/mail.md).

## شروع سریع

```csharp
builder.Services.AddNaravelMail(builder.Configuration);   // مقدار پیش‌فرض Mail:Default برابر "log" است: چیزی تحویل داده نمی‌شود

public class WelcomeMail(User user) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.Subject("Welcome").Text($"Hi {user.Name}").Html($"<p>Hi {user.Name}</p>").Build();
}

await mailer.To(user.Email).SendAsync(new WelcomeMail(user), cancellationToken);   // mailer همان IMailer تزریق‌شده است
```

```json
{ "Mail": { "Default": "log", "From": "app@example.com" } }
```

اگر `Mail:From` تنظیم نشده، داخل `Build` متد `mail.From("app@example.com")` را صدا بزنید؛ ایمیل بدون فرستنده `InvalidOperationException` می‌دهد که نام `Mail:From` را می‌آورد.

## نحوهٔ استفاده

### ارسال یک پیام آماده

`IMailer.SendAsync(MailMessage)` یک پیام صریح را بدون mailable می‌فرستد:

```csharp
await mailer.SendAsync(new MailMessage("app@example.com", "user@example.com", "Receipt", "Thanks!"), ct);
```

`MailMessage` تغییرناپذیر است. سازنده‌هایش هر نشانی را اعتبارسنجی می‌کنند (`Name <user@example.com>` پذیرفته می‌شود)، دست‌کم یک گیرنده و دست‌کم یک متن (متنی، HTML یا هر دو) می‌خواهند و موضوع `null` را رد می‌کنند (موضوع خالی مجاز است). ورودی نامعتبر هنگام ساخت و پیش از هر تماس با transport، `ArgumentException` می‌دهد.

### Mailableها

`Mailable.Build(MailBuilder)` پیام را برمی‌گرداند. builder از قبل گیرنده‌هایی که به `To(...)` داده‌اید و فرستندهٔ پیش‌فرض را دارد؛ علاوه بر آن `To`، `Cc`، `Bcc`، `From`، `Subject`، `Text`، `Html` و `Build()` را عرضه می‌کند.

- `Build` **باید خالص (pure) باشد**: بدون I/O، بدون خواندن ساعت، بدون تصادف. adapter صف آن را پیش از enqueue صدا می‌زند و فقط دادهٔ حاصل را ذخیره می‌کند.
- `mailer.To(a).To(b)` گیرنده اضافه می‌کند؛ `To(...)` یک `PendingMail` با `SendAsync(Mailable)` و `QueueAsync(Mailable)` برمی‌گرداند.
- `Build`ی که `null` برگرداند `InvalidOperationException` با نام کلاس می‌دهد.

### رندر کردن view

Naravel هیچ موتور قالبی را اجباری نمی‌کند. اگر موتوری دارید، `IMailRenderer` (اعلام‌شده) را پیاده کنید (`Task<string> RenderAsync(string template, object model, CancellationToken)`)، **پیش از** ساختن mailable رندر کنید و رشته را به آن بدهید:

```csharp
var html = await renderer.RenderAsync("welcome", model, ct);
await mailer.To(user.Email).SendAsync(new WelcomeMail(user, html), ct);
```

### Transportها

transport را با نام در پیکربندی انتخاب کنید. نام‌های زیر `Mail:Mailers` یک `Driver` را برمی‌گزینند:

```json
{
  "Mail": {
    "Default": "dev",
    "From": "app@example.com",
    "Mailers": {
      "dev":   { "Driver": "log" },
      "test":  { "Driver": "array" }
    }
  }
}
```

| Driver | کار | بسته |
|---|---|---|
| `log` | فرستنده، گیرنده‌ها و موضوع را در `ILogger` می‌نویسد و چیزی تحویل نمی‌دهد. متن نوشته نمی‌شود. گیرنده‌ها در log دیده می‌شوند؛ جایی که log به اشتراک گذاشته می‌شود استفاده نکنید. | `Naravel.Mail` |
| `array` | هر پیام ارسال‌شده را در حافظه نگه می‌دارد (`ArrayMailTransport.Sent`) برای تست و اجرای محلی. | `Naravel.Mail` |
| `smtp` | از طریق MailKit می‌فرستد. | `Naravel.Mail.Smtp` (R09.T03، هنوز موجود نیست) |

`log` و `array` بدون هیچ پیکربندی هم با همین نام‌ها وجود دارند. `IMailer` تزریق‌شده از transport پیش‌فرض استفاده می‌کند و آن را در هر ارسال حل می‌کند، پس تغییر `Mail:Default` در زمان اجرا بدون تزریق دوباره اثر می‌گذارد. برای استفادهٔ صریح از transport دیگر:

```csharp
await mailManager.Mailer("test").To(user.Email).SendAsync(new WelcomeMail(user), ct);
```

transport خودتان را در زمان اجرا با `Extend` اضافه کنید:

```csharp
mailManager.Extend("custom", sp => new MyTransport());
```

transport متد `IMailTransport.SendAsync(MailMessage, CancellationToken)` را پیاده می‌کند: فقط وقتی پیام پذیرفته شد عادی برگردد و در غیر این صورت استثنا بدهد. Naravel شکست transport را هرگز قورت نمی‌دهد و ارسال را هرگز خودکار تکرار نمی‌کند، چون تکرار پس از شکست مبهم ممکن است دو بار تحویل بدهد.

### ارسال صف‌بندی‌شده (`Naravel.Mail.Queue`)

`mailer.To(user.Email).QueueAsync(new WelcomeMail(user))` mailable را می‌سازد و **دادهٔ پیام** را به seam با نام `IMailQueue` می‌دهد که `Naravel.Mail.Queue` پیاده می‌کند. تا وقتی آن بسته ثبت نشده، `QueueAsync` یک `InvalidOperationException` می‌دهد که می‌گوید چه چیزی نصب شود. خود شیء mailable هرگز serialize نمی‌شود، پس هیچ نام نوع CLR یا delegate در دادهٔ صف ذخیره نمی‌شود.

```csharp
builder.Services.AddQueue(builder.Configuration).AddRedisDriver(builder.Configuration); // هر driver صف
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddNaravelMailQueue(options =>        // تعیین محل اختیاری است
{
    options.Connection = "redis";                       // پیش‌فرض: connection پیش‌فرض ماژول صف
    options.Queue = "emails";                           // پیش‌فرض: "default"
});
// فقط در پردازش worker:
builder.Services.AddQueueWorker(options => { options.Connection = "redis"; options.Queues = ["emails"]; });
```

نحوهٔ کار:

- `AddNaravelMailQueue()` پیاده‌سازی `IMailQueue` و `SendQueuedMailJob` را با alias پایدار `naravel.mail.send` ثبت می‌کند. صف و worker را ثبت نمی‌کند؛ آن‌ها را مثل هر job دیگر خودتان اضافه می‌کنید.
- payload ذخیره‌شده دادهٔ ساده است: `From`، `To`، `Cc`، `Bcc`، `Subject`، `TextBody`، `HtmlBody` و نام transport (`null` یعنی پیش‌فرض). worker یک `MailMessage` می‌سازد (همهٔ آدرس‌ها دوباره اعتبارسنجی می‌شوند) و با `MailManager.Mailer(transport)` می‌فرستد.
- transport در زمان enqueue با نام انتخاب می‌شود: `manager.Mailer("smtp").To(a).QueueAsync(...)` مقدار `smtp` را ذخیره می‌کند و worker همان نام را به کار می‌برد، حتی اگر `Mail:Default` در این فاصله عوض شده باشد.
- **تحویل at-least-once است.** اگر worker بعد از پذیرش پیام توسط transport و پیش از تأیید job متوقف شود، پیام دوباره تحویل می‌شود. ارسال‌های ناموفق با `MaxAttempts` و backoff استاندارد job دوباره تلاش می‌شوند. Naravel ایمیل تکراری را حذف نمی‌کند؛ اگر تکرار برایتان مضر است، در کد خودتان محافظت اضافه کنید.
- پیوست هنوز بخشی از payload نیست (PDR-013a را ببینید)؛ یک PDR بعدی نحوهٔ ارجاع به فایل را تعیین می‌کند.

### تست

```csharp
var fake = new MailFake { DefaultFrom = "app@example.com" };
var service = new SignupService(fake);          // IMailer می‌گیرد

await service.RegisterAsync("ada@example.com");

fake.AssertSent(m => m.To.Contains("ada@example.com") && m.Subject == "Welcome");
fake.AssertSentCount(1);
fake.AssertNothingQueued();
```

`MailFake` در `Naravel.Mail.Testing` است و `IMailer` را پیاده می‌کند. پیام‌های `Sent` و `Queued` را ثبت می‌کند، `AssertSent`، `AssertSentCount`، `AssertQueued`، `AssertNothingSent`، `AssertNothingQueued` و `Reset` دارد و هنگام شکست assertion `InvalidOperationException` با پیام خوانا می‌دهد. برای بررسی ارسال‌های واقعی `MailManager` از transport با نام `array` استفاده کنید.

## مرجع پیکربندی

| کلید | معنا | پیش‌فرض |
|---|---|---|
| `Mail:Default` | نام transport که `IMailer` تزریق‌شده استفاده می‌کند. | `log` |
| `Mail:From` | فرستنده‌ای که وقتی mailable متد `From(...)` را صدا نزند استفاده می‌شود. | ندارد |
| `Mail:Mailers:<name>:Driver` | `log`، `array` یا driver یک بستهٔ نصب‌شده. | - |
| `Mail:Mailers:<name>:*` | تنظیمات مخصوص driver. تغییر آن فقط همان transport را دوباره می‌سازد؛ کلیدهای نامرتبط هرگز. | - |

## مشاهده‌پذیری

`Meter` و `ActivitySource` هر دو `Naravel.Mail` نام دارند. شمارنده‌های `naravel.mail.sent` و `naravel.mail.failed` یک tag به نام `transport` (نام پیکربندی‌شده) دارند. activity با نام `mail.send` همان نام را دارد. گیرنده‌ها، موضوع و متن هرگز در telemetry نمی‌آیند.

## نحوهٔ ساخت (پیاده‌سازی)

- `MailManager` از `Manager<IMailTransport, MailOptions>` ([Foundation](architecture.md)) ارث می‌برد، پس cache کردن driver، `Extend` در زمان اجرا، dispose و بارگذاری دوبارهٔ پیکربندی با Cache و Queue و Filesystem مشترک است و چیزی دوباره‌نویسی نشده.
- `MailOptions` مقادیر `Mail:Default`، `Mail:From` و `Mail:Mailers` را bind می‌کند. `Mailers` همان دیکشنری‌ای است که manager به‌عنوان `Stores` می‌خواند.
- `MailManager.Mailer(name)` یک `IMailer` کوچک برمی‌گرداند که به *نام* transport وصل است. transport را در هر ارسال حل می‌کند، خودش چیزی اعتبارسنجی نمی‌کند (پیام در سازنده‌اش اعتبارسنجی می‌شود)، telemetry ثبت می‌کند و شکست‌ها را دوباره پرتاب می‌کند. `OperationCanceledException` شکست شمرده نمی‌شود.
- `PendingMail` mailable را با `MailBuilder` پر از گیرنده‌ها و فرستندهٔ پیش‌فرض می‌سازد و سپس mailer را صدا می‌زند.
- `AddMailTransport(...)` قلاب بسته‌های transport است: برای هر `Mail:Mailers:<name>` که `Driver` آن بخواند یک factory ثبت می‌کند، پس هر نام نمونهٔ خودش را دارد.
- `Naravel.Mail` هسته فقط به Foundation و abstractionهای `Microsoft.Extensions.*` وابسته است. به Queue ارجاع نمی‌دهد؛ `IMailQueue` همان seam است.

## شامل نیست (با تصمیم)

پیوست‌ها و تصاویر inline (payload صف را تغییر می‌دهند و PDR جدا می‌خواهند)، موتور قالب، APIهای مخصوص provider (SES، SendGrid، ...)، مدیریت bounce/webhook و امضای DKIM. «خارج از محدوده» در PDR-013a را ببینید.

## نگاشت به Laravel

| Laravel | Naravel |
|---|---|
| `Mail::to($u)->send(new Welcome($u))` | `mailer.To(u.Email).SendAsync(new WelcomeMail(u))` |
| `Mailable::build()` | `Mailable.Build(MailBuilder)` |
| `Mail::mailer('smtp')` | `mailManager.Mailer("smtp")` |
| `Mail::fake()` / `assertSent` | `MailFake` / `AssertSent` |
| `default`، `mailers`، `from` در `config/mail.php` | `Mail:Default`، `Mail:Mailers`، `Mail:From` |
| `Mail::raw`، facade، `__call` | پورت نشده |
