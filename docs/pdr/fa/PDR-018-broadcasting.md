# PDR-018 — Broadcasting (`Naravel.Broadcasting`)

**داوری با اولویت .NET بومی:** SignalR از قبل پیام‌های بلادرنگ را می‌رساند: `IHubContext<TaskHub>` به‌علاوه‌ی `Clients.Group("orders.7").SendAsync("OrderShipped", payload)` یک خط است، `[Authorize]` روی hub هست، و backplaneهای Redis و Azure SignalR داخلی‌اند. آنچه SignalR نمی‌دهد **قرارداد رویداد تایپ‌دار و مجوزدهی کانال** در Laravel است: کلاس رویدادی که بگوید به کدام کانال‌ها می‌رود، قاعده‌ای ثبت‌شده که تعیین کند چه کسی می‌تواند به `private-orders.{id}` بپیوندد، یک نقطه برای عوض کردن transport، و fake برای آزمون. **نتیجه‌ی پیشنهادی: پذیرفته، به‌صورت لایه‌ای نازک روی SignalR و هیچ چیزی که جای SignalR را بگیرد.**

وضعیت: **پیشنهادی، منتظر تأیید مالک.**

## زمینه

Laravel: رویداد `ShouldBroadcast` را با `broadcastOn()` (کانال‌ها)، `broadcastAs()` و `broadcastWith()` پیاده می‌کند؛ `Broadcast::channel('orders.{id}', fn ($user, $id) => ...)` کانال‌های private و presence را مجاز می‌کند (نام‌هایشان با `private-` یا `presence-` شروع می‌شود)؛ driverها: `pusher`، `ably`، `reverb`، `redis`، `log`، `null`؛ و `Broadcast::routes()` مسیر `/broadcasting/auth` را باز می‌کند. broadcasting به‌طور پیش‌فرض صف‌بندی می‌شود.

آنچه SignalR در ASP.NET Core می‌دهد (بخشی از shared framework به نام `Microsoft.AspNetCore.App` که `Naravel.Routing` از قبل به آن ارجاع می‌دهد): hubها، groupها، userها، `IHubContext<T>` برای ارسال از بیرون hub، hub filterها، `[Authorize]`، backplaneها (Redis، Azure SignalR Service) و کتابخانه‌های client. **تحویل SignalR حداکثر یک‌بار است:** clientی که آفلاین باشد پیام را از دست می‌دهد.

شکاف‌هایی که این PDR هدف می‌گیرد:

- نام group یک رشته‌ی آزاد است و **پیوستن به group به‌طور پیش‌فرض مجاز نمی‌شود:** متدی مثل `JoinGroup(name)` در hub کد برنامه است و فراموش کردن بررسی باعث می‌شود هر client متصل به هر group گوش دهد.
- رویداد تایپ‌دار نیست: شکل payload و نام رویداد بین نقطه‌های فراخوانی پخش است.
- test double نیست: آزمون‌ها به client و سرور SignalR نیاز دارند.

قید از Naravel.Events (در مستنداتش بررسی شده): listenerها **دقیقاً با نوع رویداد** تطبیق می‌خورند، بدون dispatch بر اساس نوع پایه یا interface. یک listener کلی «هر چه `IBroadcastable` را پیاده کند broadcast کن» صدا زده نمی‌شد، پس یکپارچگی با رویدادها باید برای هر نوع رویداد صریح باشد.

## شروع سریع پیشنهادی (S1)

```csharp
builder.Services.AddNaravelBroadcasting(o => o.Channels.Authorize("private-orders.{id}", (user, id) => user.FindFirst("order")?.Value == id));
app.MapNaravelBroadcasting("/hubs/broadcast");                      // تنها hubی که clientها به آن وصل می‌شوند

public sealed record OrderShipped(int OrderId) : IBroadcastable
{
    public IEnumerable<string> Channels() => [$"private-orders.{OrderId}"];
}

await broadcaster.BroadcastAsync(new OrderShipped(7), cancellationToken);   // broadcaster یک IBroadcaster تزریق‌شده است
```

## تصمیم پیشنهادی

1. بسته‌ی جدید `Naravel.Broadcasting` روی `Manager<,>` (driverها: `signalr`، `log`، `null`؛ شکل پیکربندی طبق PDR-004 و PDR-005) که مثل `Naravel.Routing` به shared framework ASP.NET Core ارجاع می‌دهد. backplane‌ی Redis گزینه‌ی بومی SignalR می‌ماند که برنامه پیکربندی می‌کند (خارج از دامنه).
2. **`IBroadcastable`:** `Channels()` الزامی است؛ `BroadcastAs()` (پیش‌فرض: نام نوع) و `BroadcastWith()` (پیش‌فرض: رویداد سریالایزشده با `System.Text.Json`) اختیاری‌اند. **`IBroadcaster.BroadcastAsync(event, ct)`** به همه‌ی کانال‌های برگردانده‌شده می‌فرستد. یکپارچگی با Events صریح است و با dispatch دقیق‌نوع هم‌خوان: `AddBroadcastOn<TEvent>()` یک `IEventListener<TEvent>` ثبت می‌کند که broadcast می‌کند، پس کاربر می‌گوید کدام رویدادها بیرون می‌روند.
3. **یک hub به نام `BroadcastHub`** که با `MapNaravelBroadcasting(path)` map می‌شود. clientها `Subscribe(channel)` را صدا می‌زنند. کانالی که با `private-` یا `presence-` شروع شود callbackهای مجوزدهی ثبت‌شده را اجرا می‌کند (`Channels.Authorize("private-orders.{id}", (user, id) => ...)` با placeholderهای سبک route) و **اگر هیچ callbackی تطبیق نکند رد می‌شود:** کانال private ثبت‌نشده رد می‌شود، نه مجاز. کانال‌های عمومی قاعده نمی‌خواهند. callback `ClaimsPrincipal` احراز‌شده را می‌گیرد و hub برای کانال‌های private و presence احراز هویت را الزامی می‌کند.
4. **Presence** (فهرست اعضا) در نسخه‌ی اول **نیست**؛ ردیابی اتصال می‌خواهد که به backplane بستگی دارد. در backlog ثبت می‌شود.
5. **قرارداد تحویل، در مستندات اعلام می‌شود:** حداکثر یک‌بار، بدون replay، بدون صف پایدار. نسخه‌ی اول درون‌خطی (inline) می‌فرستد؛ broadcasting از طریق `Naravel.Queue` (پیش‌فرض Laravel) یک adapter‌ی اختیاری برای بعد است و بخشی از این PDR نیست.
6. **رفتار در شکست:** استثنای transport به فراخواننده‌ی `BroadcastAsync` می‌رسد (بلعیده نمی‌شود)؛ فرستادن به کانالی که شنونده ندارد خطا نیست.
7. **S3:** `BroadcastFake` پخش‌ها را ضبط می‌کند (رویداد، کانال‌ها، نام، payload) با assertion مثل fakeهای دیگر؛ یک `Meter` به نام `Naravel.Broadcasting` با شمارنده‌ی `naravel.broadcasting.broadcasts` که فقط با `driver` و `outcome` برچسب می‌خورد (هرگز نام کانالی که شناسه‌ی کاربر دارد، payload یا توکن) و یک `ActivitySource`.
8. **S2:** broadcast وابسته به I/O است؛ benchmark اضافه نمی‌شود. تطبیق قاعده‌ی کانال یک بار به الگوهای کامپایل‌شده resolve می‌شود، نه با reflection در هر فراخوان.
9. **خارج از دامنه:** جایگزینی SignalR، driverهای Pusher/Ably/Reverb، endpoint‌ی HTTP برای `/broadcasting/auth` (SignalR هنگام پیوستن مجوز می‌دهد)، backplane‌ی Redis، کتابخانه‌های client.

## گزینه‌های بررسی‌شده

1. **رد کردن: مستقیم از `IHubContext` و متدهای hub استفاده کنید.** پاسخ بومی صادقانه و انتخابی معتبر. تله‌ی group بدون مجوز، قرارداد پراکنده‌ی رویداد و نبود fake را به عهده‌ی هر برنامه می‌گذارد.
2. **یک driver برای هر vendor (Pusher، Ably، Reverb).** فعلاً رد شد: هرکدام پروتکل و وابستگی جداست؛ SignalR همان transport بومی است.
3. **broadcast خودکار هر رویدادی که interface را پیاده کند.** رد شد: Events بر اساس نوع دقیق dispatch می‌کند و اسکن با reflection هنگام startup یا dispatch دقیقاً همان رفتار پنهانی است که پروژه از آن پرهیز می‌کند.
4. **صف‌بندی پیش‌فرض، مثل Laravel.** به تعویق افتاد: adapter‌ی Queue و قرارداد payload می‌خواهد؛ تحویل درون‌خطی حداکثر یک‌بار صریح و ساده است.

## تصمیم‌ها برای مالک

- **D1:** پذیرش لایه‌ی نازک (توصیه‌شده)، یا `DECLINED` با دستورالعمل (گزینه‌ی ۱).
- **D2:** کانال‌های ثبت‌نشده‌ی `private-`/`presence-` **رد شوند** (توصیه‌شده: امن به‌طور پیش‌فرض)، یا مجاز باشند.
- **D3:** یکپارچگی صریح رویداد با `AddBroadcastOn<TEvent>()` (توصیه‌شده)، یا فقط `BroadcastAsync` مستقیم.
- **D4:** نسخه‌ی اول بدون presence (توصیه‌شده)، یا با فهرست اعضای درون‌پردازه‌ای.
- **D5:** فعلاً تحویل درون‌خطی و حداکثر یک‌بار؛ adapter‌ی صف بعداً (توصیه‌شده).

## قراردادها و کیفیت (در صورت پذیرش)

callbackهای مجوزدهی پیش از هر پیوستن به group اجرا می‌شوند؛ callbackی که استثنا بدهد پیوستن را رد می‌کند و بدون claimهای کاربر لاگ می‌شود؛ نام کانال و payload هرگز در برچسب متریک یا قالب لاگ نمی‌آیند، جز الگوی کانال؛ ناهمگام با `CancellationToken`. آزمون‌ها: TestHost با client واقعی SignalR رویداد را روی کانال عمومی می‌گیرد؛ پیوستن مجاز به کانال private آن را می‌گیرد؛ پیوستن غیرمجاز و پیوستن به کانال private ثبت‌نشده رد می‌شود و چیزی نمی‌گیرد؛ assertionهای `BroadcastFake`؛ آزمون‌های `Extend` و تعویض driver؛ یکپارچگی رویداد با نوع دقیق.

## تأیید

هنوز تأیید نشده. پس از تأیید، مرحله‌ی پیاده‌سازی و مرحله‌ی اعتبارسنجی اجرا می‌شوند.
