# ایمیل (Mail)

`Naravel.Mail` یک قرارداد تایپ‌شده برای ایمیل است: کلاس `Mailable` پیام را توصیف می‌کند، یک *transport* نام‌دار آن را تحویل می‌دهد و یک fake آن را قابل تست می‌کند. بستهٔ هسته **هیچ وابستگی شبکه‌ای ندارد**؛ SMTP در بستهٔ اختیاری `Naravel.Mail.Smtp` و ارسال صف‌بندی‌شده در adapter اختیاری `Naravel.Mail.Queue` است (هر دو همین حالا موجودند).

تصمیم‌ها: [PDR-013](../pdr/fa/PDR-013-mail.md) و [PDR-013a](../pdr/fa/PDR-013a-mail-mailable-and-transports.md). نسخهٔ انگلیسی: [docs/en/mail.md](../en/mail.md).

## شروع سریع

```sh
dotnet add package Naravel.Mail
```

```json
{ "Mail": { "From": "app@example.com" } }
```

```csharp
using Naravel.Mail;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelMail(builder.Configuration);   // مقدار پیش‌فرض Mail:Default برابر "log" است: پیام فقط log می‌شود و چیزی تحویل داده نمی‌شود

var app = builder.Build();

app.MapPost("/welcome", async (string email, string name, IMailer mailer, CancellationToken ct) =>
{
    await mailer.To(email).SendAsync(new WelcomeMail(name), ct);
    return Results.Accepted();
});

app.Run();

public sealed class WelcomeMail(string name) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.Subject("Welcome").Text($"Hi {name}").Html($"<p>Hi {name}</p>").Build();
}
```

`POST /welcome?email=ada@example.com&name=Ada` پاسخ `202 Accepted` می‌دهد و log برنامه یک خط با فرستنده، گیرنده و موضوع نشان می‌دهد. برای تحویل واقعی، `Mail:Default` را به یک transport از نوع SMTP وصل کنید ([پایین‌تر](#smtp-naravelmailsmtp)).

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

رونوشت و فرستندهٔ اختصاصی:

```csharp
public sealed class MonthlyReport : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.From("reports@example.com")               // برای همین ایمیل جای Mail:From را می‌گیرد
            .Cc("manager@example.com")
            .Bcc("archive@example.com")                // فقط برای تحویل؛ هرگز در headerها نوشته نمی‌شود
            .Subject("Monthly report")
            .Text("Your monthly report is ready.")
            .Build();
}
```

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
| `smtp` | از طریق MailKit و روی TLS می‌فرستد. | `Naravel.Mail.Smtp` |

### SMTP (`Naravel.Mail.Smtp`)

```csharp
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddNaravelMailSmtp(builder.Configuration);   // بعد از AddNaravelMail
```

```json
{
  "Mail": {
    "Default": "relay",
    "From": "app@example.com",
    "Mailers": {
      "relay": { "Driver": "smtp", "Host": "smtp.example.com", "Port": 587, "Security": "StartTls", "Username": "app", "Timeout": 30 }
    }
  }
}
```

| کلید | معنی | پیش‌فرض |
|---|---|---|
| `Host` | سرور SMTP. **الزامی**؛ نبودنش با پیامی که نام کلید را می‌گوید شکست می‌خورد. | ندارد |
| `Port` | پورت سرور. | `587` |
| `Security` | `StartTls` (TLS اجباری، پورت 587)، `SslOnConnect` (TLS ضمنی، پورت 465) یا `None` (متن ساده، فقط سرور تست محلی). | `StartTls` |
| `Username` / `Password` | اعتبارنامه. بدون `Username` احراز هویت انجام نمی‌شود. گذرواژه را در user secrets یا `Mail__Mailers__relay__Password` بگذارید، نه در فایل commit‌شده. | ندارد |
| `Timeout` | ثانیه برای هر عملیات شبکه. | `30` |

رفتارهایی که می‌توانید به آن‌ها تکیه کنید:

- TLS **به‌طور پیش‌فرض اجباری است**. Naravel هرگز بی‌صدا به متن ساده برنمی‌گردد؛ باید صریحاً `"Security": "None"` بنویسید.
- هر ارسال اتصال خودش را باز می‌کند، احراز هویت می‌کند، می‌فرستد و قطع می‌کند (بدون pool)، پس ارسال‌های همزمان ایمن‌اند.
- رد شدن توسط سرور (مثلاً `550` برای گیرندهٔ ناشناخته) یا خطای شبکه به‌صورت exception خود MailKit به فراخواننده پرتاب می‌شود. داخل transport چیزی دوباره تلاش نمی‌شود. ایمیل صف‌بندی‌شده را job صف دوباره تلاش می‌کند (`MaxAttempts` و backoff)، یعنی ارسالی که نتیجه‌اش نامشخص بوده ممکن است دو بار تحویل شود.
- گیرندگان `Bcc` فقط برای تحویل استفاده می‌شوند و هرگز در header دیده نمی‌شوند. مقدار headerها را MimeKit encode می‌کند، پس موضوعی که شکست خط دارد نمی‌تواند header تزریق کند.
- این بسته به MailKit وابسته است (MIT، نسخهٔ 4.18.1 pin شده؛ نسخهٔ 4.16.0 مشکل CVE-2026-41319 را رفع کرد) و `System.Security.Cryptography.Pkcs` تراکنشی را به 10.0.13 ارتقا می‌دهد (رفع CVE-2026-70162 و CVE-2026-84008). هستهٔ `Naravel.Mail` وابسته نیست.

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

## خطاها

| چه وقت | exception و پیام |
|---|---|
| ایمیل فرستنده ندارد (نه `Mail:From` و نه `From(...)`) | `InvalidOperationException`: `The mail has no sender. Call From(...) in Mailable.Build or set Mail:From in configuration.` |
| `Mailable.Build` مقدار `null` برگرداند | `InvalidOperationException`: `WelcomeMail.Build returned null. Return mail.Build().` (نام کلاس، نام کلاس خودتان است) |
| یک آدرس معتبر نیست | `ArgumentException`: `'not-an-address' is not a valid e-mail address.` |
| گیرنده‌ای نیست | `ArgumentException`: `A mail message needs at least one recipient.` |
| نه بدنهٔ متنی هست و نه HTML | `ArgumentException`: `A mail message needs a text body, an HTML body, or both.` |
| `QueueAsync` بدون adapter صف | `InvalidOperationException`: `No mail queue adapter is registered. Install Naravel.Mail.Queue and call services.AddNaravelMailQueue(), or use SendAsync.` |
| transport نتواند تحویل دهد | exception خودِ transport، بدون تغییر؛ چیزی برای شما دوباره تلاش نمی‌شود. |

پیام `ArgumentException` با ` (Parameter 'to')` یا نام آرگومان خطادار تمام می‌شود. سه حالت `ArgumentException` هنگام ساخته‌شدن `MailMessage` رخ می‌دهند، پیش از آنکه به transport دست زده شود.

transport از نوع `smtp` تنظیم‌هایش را وقتی می‌خواند که transport برای نخستین بار ساخته شود، یعنی نخستین ارسال از آن mailer. `AddNaravelMailSmtp` فقط آن را ثبت می‌کند و چیزی نمی‌خواند؛ پس مقدار نادرست جلوی راه‌اندازی برنامه را نمی‌گیرد، همان ارسال اول را شکست می‌دهد و نام کلید را می‌آورد:

| تنظیم | پیام |
|---|---|
| `Host` ندارد | `The smtp mail transport needs a Host (Mail:Mailers:<name>:Host).` |
| `Port` عدد نیست | `Mail:Mailers:relay:Port 'abc' is not a number.` |
| `Port` بیرون از ۱ تا ۶۵۵۳۵ | `The smtp mail transport Port 70000 is not between 1 and 65535.` |
| `Security` ناشناخته | `Mail:Mailers:relay:Security 'x' must be StartTls, SslOnConnect or None.` |
| `Timeout` عدد مثبت نیست | `Mail:Mailers:relay:Timeout 'x' must be a positive number of seconds.` |

خطاهای خود Foundation (نام transport ناشناخته، نبودن پیش‌فرض) در [راهنمای Foundation](foundation.md#خطاها) آمده است.

assertionهای `MailFake` با `InvalidOperationException` شکست می‌خورند:

| assertion | پیام |
|---|---|
| `AssertSent()` وقتی چیزی ارسال نشده | `No mail message was sent.` |
| `AssertSent(predicate)` وقتی هیچ پیامی مطابقت ندارد | `No matching mail message was sent (1 sent in total).` |
| `AssertSentCount(2)` وقتی یکی ارسال شده | `Expected 2 sent mail message(s), but found 1.` |
| `AssertNothingSent()` وقتی یکی ارسال شده | `Expected no mail to be sent, but 1 message(s) were sent.` |
| `AssertNothingQueued()` وقتی یکی در صف رفته | `Expected no mail to be queued, but 1 message(s) were queued.` |

`AssertQueued` همین دو پیام را با `queued` به‌جای `sent` دارد.

`tests/Naravel.Mail.Tests/DocsExampleTests.cs` مثال quickstart، مثال رونوشت و همهٔ پیام‌های بالا را، جز جدول `smtp`، اجرا می‌کند؛ تنظیمات SMTP را `tests/Naravel.Mail.Smtp.Tests` می‌پوشاند، از جمله اینکه مقدار نادرست هنگام نخستین ساخته‌شدن transport شکست می‌خورد و نه هنگام ثبت. یکی را عوض کردید، دیگری را هم عوض کنید.

## مثال کامل

یک mailable، سرویسی که آن را می‌فرستد، ثبت در DI و یک تست با `MailFake`. `SignupRequest` نوع درخواست خودتان است.

```csharp
public sealed class WelcomeMail(string name) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.Subject("Welcome").Text($"Hi {name}").Html($"<p>Hi {name}</p>").Build();   // pure: no I/O, no clock
}

public sealed class SignupService(IMailer mailer)
{
    public Task RegisterAsync(string email, string name, CancellationToken ct = default) =>
        mailer.To(email).SendAsync(new WelcomeMail(name), ct);
}

// Program.cs: Mail:Default = "log" in development, so nothing is delivered; switch the transport in configuration for production.
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddScoped<SignupService>();

app.MapPost("/signup", async (SignupRequest body, SignupService signup, CancellationToken ct) =>
{
    await signup.RegisterAsync(body.Email, body.Name, ct);
    return Results.Accepted();
});

// Test
[Fact]
public async Task Register_sends_a_welcome_mail()
{
    var fake = new MailFake { DefaultFrom = "app@example.com" };
    await new SignupService(fake).RegisterAsync("ada@example.com", "Ada");

    fake.AssertSent(m => m.To.Contains("ada@example.com") && m.Subject == "Welcome");
    fake.AssertSentCount(1);
    fake.AssertNothingQueued();
}
```

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
