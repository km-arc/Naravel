# اعلان‌ها (Notifications)

`Naravel.Notifications` **یک اعلان تایپ‌شده را از راه channelهایی که شما نام می‌برید** و به همان ترتیبی که نام می‌برید برای یک گیرنده می‌فرستد، و یک fake برای تست‌پذیری دارد. بستهٔ هسته هیچ وابستگی به mail، queue یا شبکه ندارد: Mail و Queue بسته‌های اختیاری جدا هستند و هر channel دیگری یک کلاس یا delegate معمولی است که خودتان ثبت می‌کنید.

تصمیم‌ها: [PDR-014](../pdr/fa/PDR-014-notifications.md). نسخهٔ انگلیسی: [docs/en/notifications.md](../en/notifications.md).

## شروع سریع

```csharp
builder.Services.AddNaravelNotifications();
builder.Services.AddLogNotificationChannel<InvoiceReady>();          // یک channel با نام "log"

public sealed record InvoiceReady(int InvoiceId);

await notifications.SendAsync(user, new InvoiceReady(7), ["log"], cancellationToken);   // notifications یک INotificationSender تزریق‌شده است
```

هر channel **برای یک نوع اعلان** ثبت می‌شود و در هر فراخوانی **با نام** انتخاب می‌شود. هیچ چیز از روی گیرنده یا ترتیب ثبت حدس زده نمی‌شود.

## نحوهٔ استفاده

### Channelها

یک channel، `INotificationChannel<TNotification>` را پیاده می‌کند: یک `Name` و یک `SendAsync(NotificationContext, TNotification, CancellationToken)`. آن را به‌صورت کلاس (از DI و برای هر scope ساخته می‌شود، پس می‌تواند وابستگی بگیرد)، به‌صورت delegate ثبت کنید، یا از channel لاگ آماده استفاده کنید:

```csharp
services.AddNotificationChannel<InvoiceReady, SmsChannel>();                       // کلاس
services.AddNotificationChannel<InvoiceReady>("audit", (context, invoice, ct) =>   // delegate
    audit.WriteAsync(context.Recipient, invoice, ct));
services.AddLogNotificationChannel<InvoiceReady>("log");                           // فقط نام نوع‌ها را لاگ می‌کند
```

`NotificationContext` شامل `Recipient` صریح (یک `object`) و نام `Channel` است. `context.GetRecipient<User>()` آن را cast می‌کند و اگر گیرنده نوع دیگری باشد، `InvalidOperationException` می‌دهد که نام هر دو نوع را دارد. نام channel برای هر نوع اعلان یکتاست و بدون توجه به بزرگی و کوچکی حروف مقایسه می‌شود؛ ثبت دوبارهٔ یک نام هنگام ارسال اعلان گزارش می‌شود. ثبت دوبارهٔ همان کلاس channel اثری ندارد.

channel لاگ برای هر تحویل یک خط می‌نویسد که فقط **نام نوع‌های** اعلان و گیرنده را دارد و هرگز محتوایشان را، و بدون هیچ ثبت logging هم کار می‌کند.

### ارسال

```csharp
await notifications.SendAsync(user, new InvoiceReady(7), ["mail", "sms", "log"], ct);
```

- channelها **به ترتیبی که داده‌اید** و یکی‌پس‌ازدیگری اجرا می‌شوند، نه به ترتیب ثبت.
- همهٔ نام‌ها **پیش از اولین تحویل** resolve می‌شوند: غلط املایی یا channel ثبت‌نشده `InvalidOperationException` می‌دهد (نام‌های ثبت‌شده را فهرست می‌کند) و هیچ چیزی فرستاده نمی‌شود.
- `channels` باید دست‌کم یک نام داشته باشد، نام خالی و تکراری (بدون توجه به حروف) نداشته باشد؛ وگرنه `ArgumentException`. گیرنده یا اعلان `null` یعنی `ArgumentNullException`.
- channelی که استثنا بدهد، **ارسال را متوقف می‌کند**: channelهای بعدی امتحان نمی‌شوند و فراخوانی `NotificationDeliveryException` می‌دهد با `Channel` (آنکه شکست خورد)، `DeliveredChannels` (آن‌هایی که پیش‌تر تحویل داده بودند، به ترتیب) و استثنای اصلی در `InnerException`. تحویل ناقص هرگز موفقیت گزارش نمی‌شود و فراخواننده‌ای که دوباره تلاش می‌کند می‌داند چه چیزی قبلاً رفته است.
- لغو شکست نیست: `OperationCanceledException` همان‌طور که هست پرتاب می‌شود، هرگز پوشانده نمی‌شود، و توکن لغوشده هیچ channel ای را اجرا نمی‌کند.

`INotificationSender` به‌صورت **scoped** ثبت می‌شود؛ آن را در handlerهای درخواست، jobها و کدهای scoped دیگر تزریق کنید.

### channel ایمیل (`Naravel.Notifications.Mail`)

```csharp
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddMailNotificationChannel<InvoiceReady>(
    address: c => c.GetRecipient<User>().Email,
    mailable: n => new InvoiceMail(n));

await notifications.SendAsync(user, new InvoiceReady(7), ["mail", "log"], ct);
```

دو delegate ساده جای `routeNotificationForMail()` و `toMail()` لاراول را می‌گیرند: یکی آدرس را از گیرندهٔ صریح می‌خواند، دیگری `Mailable` را می‌سازد. پیام با `PendingMail` از mailer نام‌دار (آرگومان `mailer:`؛ اگر ندهید mailer پیش‌فرض) می‌رود، پس `From` پیش‌فرض mailer و اعتبارسنجی آدرس در Naravel.Mail اعمال می‌شود؛ آدرس نامعتبر channel را شکست می‌دهد. نام channel همان `mail` است مگر `name:` بدهید. برای ایمیل صف‌بندی‌شده از `QueueAsync` خود Naravel.Mail یا آداپتور صف پایین استفاده کنید.

### اعلان‌های صف‌بندی‌شده (`Naravel.Notifications.Queue`)

```csharp
builder.Services.AddNaravelNotificationQueue();      // به‌علاوهٔ AddQueue، یک درایور صف و (در پردازه‌های worker) AddQueueWorker
builder.Services.AddQueuedNotification<InvoicePayload>("invoice.ready", async (payload, scope, ct) =>
{
    var user = await scope.GetRequiredService<IUserRepository>().FindAsync(payload.UserId, ct);   // داده‌ٔ تازه
    await scope.GetRequiredService<INotificationSender>().SendAsync(user, new InvoiceReady(payload.InvoiceId), ["mail"], ct);
});

await notificationQueue.QueueAsync("invoice.ready", new InvoicePayload(user.Id, 7), ct);   // INotificationQueue
```

صف **فقط یک alias و payload را به‌صورت JSON** ذخیره می‌کند، نه چیز دیگری: نه نام نوع CLR، نه delegate، نه اعلان و نه شیء گیرنده (PDR-014، تصمیم مالک OD-07). handler ثبت‌شده زیر alias در worker با payload بازسازی‌شده و scope سرویس همان job اجرا می‌شود، پس گیرنده را از سرویس‌های خودتان می‌خواند و اعلان را خودش می‌سازد.

- aliasی که ثبت نشده باشد job را شکست می‌دهد، به‌جای اینکه نوعی حدس زده شود؛ صف‌بندی alias ثبت‌نشده، یا payload از نوعی غیر از نوع ثبت‌شده، بلافاصله استثنا می‌دهد.
- ثبت دوبارهٔ یک alias رد می‌شود. تا وقتی jobهایی با آن alias در صف هستند، **alias را هرگز عوض نکنید**.
- `AddNaravelNotificationQueue(options => { options.Connection = ...; options.Queue = ...; })` محل jobها را تعیین می‌کند؛ هر دو اختیاری‌اند و پیش‌فرض ماژول صف را می‌گیرند.
- تحویل **at-least-once** است، مثل هر job در Naravel: اگر worker بعد از تحویل channelها و پیش از acknowledge شدن job متوقف شود، اعلان دوباره فرستاده می‌شود و تلاش‌های مجدد از backoff استاندارد job استفاده می‌کنند. جایی که تکرار مهم است handler را idempotent بنویسید. payload با System.Text.Json سریال می‌شود: آن را کوچک و ساده نگه دارید.

### تست

```csharp
var fake = new NotificationFake();                      // یک INotificationSender که چیزی تحویل نمی‌دهد
await fake.SendAsync(user, new InvoiceReady(7), ["mail", "log"]);

var sent = fake.AssertSent<InvoiceReady>();             // اولین مورد؛ اگر هیچ‌کدام نرفته باشد با پیام روشن استثنا می‌دهد
sent.Recipient.Should().BeSameAs(user);
sent.Notification.InvoiceId.Should().Be(7);
sent.Channels.Should().Equal("mail", "log");
fake.AssertSent<InvoiceReady>(1);                       // تعداد دقیق
fake.AssertNotSent<InvoiceCancelled>();
fake.AssertNothingSent();                               // بعد از Reset()، یا وقتی چیزی فرستاده نشده
```

آن را با `services.AddSingleton<INotificationSender>(fake)` ثبت کنید. همان بررسی آرگومان‌های فرستندهٔ واقعی را انجام می‌دهد (نام خالی یا تکراری استثنا می‌دهد)، پس فراخوانی غلط در تست هم شکست می‌خورد؛ نام channelهای **درخواست‌شده** را ثبت می‌کند و هیچ channel ای را اجرا نمی‌کند. برای تست خود channel، مستقیم روی آن `SendAsync` را صدا بزنید، یا آن را با فرستندهٔ واقعی و transport ایمیل `array` ثبت کنید.

## مشاهده‌پذیری

`Meter` و `ActivitySource` هر دو `Naravel.Notifications` نام دارند.

| ابزار | معنی | tagها |
|---|---|---|
| `naravel.notifications.dispatched` | ارسال‌هایی که همهٔ channelها تحویل دادند. | ندارد |
| `naravel.notifications.channel.deliveries` | یکی برای هر تلاش یک channel. | `channel` (نامی که ثبت کردید)، `outcome` (`sent` یا `failed`) |
| `naravel.notifications.dispatch.duration` | میلی‌ثانیه برای هر ارسال، موفق یا ناموفق. | ندارد |

activity با نام `notifications.send` هنگام شکست یک channel به‌صورت error علامت می‌خورد. گیرنده و محتوای اعلان **هرگز** tag یا لاگ نمی‌شوند.

## نحوهٔ ساخت (پیاده‌سازی)

- `NotificationSender` مقدار `INotificationChannel<TNotification>` را از DI می‌گیرد، نام‌های درخواستی را (بدون توجه به حروف) تطبیق می‌دهد و channelها را یکی‌یکی اجرا می‌کند. manager و بخش پیکربندی وجود ندارد، چون channel تایپ‌شده است و فراخواننده انتخابش می‌کند، نه یک پیش‌فرض پیکربندی‌شده (PDR-014). پس «توسعه در زمان اجرا» یعنی ثبت یک channel پیش از ساخته‌شدن provider.
- هسته فقط به `Microsoft.Extensions.DependencyInjection.Abstractions` و `Microsoft.Extensions.Logging.Abstractions` وابسته است. `Naravel.Notifications.Mail` به Naravel.Mail و `Naravel.Notifications.Queue` به Naravel.Queue ارجاع دارد؛ هسته به هیچ‌کدام ارجاع نمی‌دهد.
- آداپتور صف یک ثبت‌نام alias صریح (`alias ← handler`) نگه می‌دارد، همان قاعدهٔ aliasهای job: رشتهٔ ذخیره‌شده هرگز به نوع CLR تبدیل نمی‌شود.
- ارسال I/O-bound است (کار را channelها می‌کنند)، پس benchmark ندارد؛ فرستنده در هر فراخوانی فقط چند آرایهٔ کوچک و یک لیست می‌سازد.

## شامل نیست (با تصمیم)

trait مدلی `Notifiable`، استنتاج channel با `via()`، قراردادهای `routeNotificationFor*` که با reflection پیدا شوند، کشف خودکار اعلان‌ها، channel دیتابیس، channel broadcast (Broadcasting مرحلهٔ جداست)، providerهای پیامک یا چت، ترجیح channel برای هر کاربر، محلی‌سازی اعلان و صف‌بندی با interface. برنامه‌ها آنچه لازم دارند را به‌صورت channel اضافه می‌کنند.

## نگاشت به Laravel

| Laravel | Naravel |
|---|---|
| `Notification::send($user, new InvoicePaid($inv))` | `notifications.SendAsync(user, new InvoicePaid(inv), ["mail"], ct)` |
| `via()` channelها را برمی‌گرداند | فراخواننده نام channelها را می‌دهد |
| `toMail()` + `routeNotificationForMail()` | `AddMailNotificationChannel(address, mailable)` |
| اعلان `ShouldQueue` | `AddQueuedNotification(alias, handler)` + `INotificationQueue.QueueAsync` |
| کلاس channel سفارشی با `send()` | `INotificationChannel<TNotification>` |
| `Notification::fake()` + `assertSentTo` | `NotificationFake` + `AssertSent<T>()` |
