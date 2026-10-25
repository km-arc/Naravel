# Broadcasting

`Naravel.Broadcasting` لایه‌ای نازک روی SignalR است. SignalR خودش پیام می‌رساند (`IHubContext<T>`، groupها، backplaneها)؛ این بسته آنچه را ندارد اضافه می‌کند: کلاس رویدادی که بگوید به کدام کانال‌ها می‌رود، قاعده‌ای ثبت‌شده برای اینکه چه کسی می‌تواند به `private-orders.{id}` بپیوندد، یک نقطه برای عوض کردن transport، و fake برای آزمون. جای SignalR را نمی‌گیرد.

تصمیم و دلایل: [PDR-018](../pdr/fa/PDR-018-broadcasting.md). نسخهٔ انگلیسی: [docs/en/broadcasting.md](../en/broadcasting.md).

## شروع سریع

```csharp
builder.Services.AddNaravelBroadcasting(builder.Configuration,
    o => o.Channels.Authorize("private-orders.{id}", (user, id) => user.FindFirst("order")?.Value == id));
builder.Services.AddBroadcastOn<OrderShipped>();                  // اختیاری: وقتی Naravel.Events این رویداد را dispatch کرد broadcast شود
app.UseAuthentication(); app.UseAuthorization();
app.MapNaravelBroadcasting("/hubs/broadcast");                    // تنها hubی که clientها به آن وصل می‌شوند

public sealed record OrderShipped(int OrderId) : IBroadcastable
{
    public IEnumerable<string> Channels() => [$"private-orders.{OrderId}"];
}

await broadcaster.BroadcastAsync(new OrderShipped(7), cancellationToken);   // broadcaster یک IBroadcaster تزریق‌شده است
```

client مرورگر (`@microsoft/signalr`) به `/hubs/broadcast` وصل می‌شود، `connection.invoke("Subscribe", "private-orders.7")` را صدا می‌زند و با `connection.on("OrderShipped", payload => ...)` گوش می‌دهد. نام متد همان `BroadcastAs()` است (به‌طور پیش‌فرض نام نوع) و payload همان `BroadcastWith()` است (به‌طور پیش‌فرض خود رویداد) که به‌صورت JSON با camelCase سریالایز می‌شود.

## کانال‌ها و مجوزدهی

| نام | چه کسی می‌تواند مشترک شود |
|---|---|
| `news`، `orders.7` (عمومی) | هر اتصال |
| `private-...` | کاربر احراز‌شده، و قاعده‌ای از `Channels.Authorize` که تطبیق کند و `true` بدهد |
| `presence-...` | کاربر احراز‌شده، و قاعده‌ای از `Channels.AuthorizePresence` که عضو این کاربر را برگرداند (`null` رد می‌کند). کانال فهرست اعضا دارد، «Presence» پایین را ببینید. |

- کانال محافظت‌شده‌ای که **هیچ قاعده‌ای با آن تطبیق نکند رد می‌شود**، و کانالی که قاعده‌اش استثنا بدهد هم. این پیش‌فرض امن است: در SignalR ساده متد hubی که client را به هر group اضافه کند برای همهٔ clientها باز است.
- الگو با کل نام کانال تطبیق می‌خورد. placeholder مثل `{id}` یک یا چند نویسه جز `.` را می‌پذیرد (مثل Laravel). اولین قاعدهٔ ثبت‌شده‌ای که تطبیق کند تصمیم می‌گیرد.
- شکل تک‌placeholder: `Authorize("private-orders.{id}", (user, id) => ...)`. `Authorize` برای الگوهای `private-` و `AuthorizePresence` برای الگوهای `presence-` است؛ پیشوند اشتباه هنگام ثبت قاعده رد می‌شود. برای چند placeholder یا قاعده‌ای که چیزی را await می‌کند (فراخوانی پایگاه داده): `Authorize("private-teams.{team}.users.{user}", async (user, values, ct) => ...)`؛ `values` placeholderها را با نام دارد و `ct` وقتی اتصال بسته شود لغو می‌شود. قاعده یک delegate است: چیزهایی را که لازم دارد هنگام ثبت capture کنید.
- نام‌ها حرف، رقم و `_ . : -` دارند، حداکثر ۱۲۸ نویسه. هر چیز دیگر را hub و `BroadcastAsync` رد می‌کنند.
- رد شدن برای هر علتی همان `HubException("Subscription denied.")` ثابت است، پس client نمی‌تواند «قاعده‌ای نیست» را از «مجاز نیست» تشخیص دهد.

## Presence

کانال `presence-` به اعضایش می‌گوید چه کسان دیگری آنجا هستند، مثل `Echo.join()` در Laravel.

```csharp
builder.Services.AddNaravelBroadcasting(builder.Configuration, o =>
    o.Channels.AuthorizePresence("presence-room.{id}", (user, id) =>
        new PresenceMember(user.FindFirst("sub")!.Value, new { name = user.Identity!.Name })));
```

```js
const members = await connection.invoke("Subscribe", "presence-room.1");   // همه‌ی کسانی که الان هستند، با خودتان: [{ userId, info }]
connection.on("MemberJoined", e => ...);   // { channel, member } وقتی کاربری برای اولین بار پیدا می‌شود
connection.on("MemberLeft",   e => ...);   // { channel, member } وقتی آخرین اتصال یک کاربر می‌رود
```

- **عضو یعنی کاربر، نه اتصال.** دو تب یک کاربر یک عضو است؛ `MemberJoined` برای اولین اتصال و `MemberLeft` برای آخرین اتصال می‌آید. شناسه‌ی کاربر همان است که قاعده در `PresenceMember` می‌دهد. `info` هرچه قاعده اضافه کند (نام، آواتار) و حداکثر ۲۰۴۸ نویسه‌ی JSON است؛ برای همه‌ی اعضا فرستاده می‌شود، پس چیز خصوصی در آن نگذارید.
- **فهرست در cache مشترک می‌ماند** (`ICacheStore` و `ICacheLock` از `Naravel.Cache`)، پس هر نمونه همان اعضا را می‌بیند. اگر چند نمونه دارید cache از نوع Redis یا Memcached ثبت کنید. بدون cache یا با cache حافظه‌ای هر پردازه فهرست خودش را دارد و ماژول یک هشدار لاگ می‌کند.
- **درستی بین نمونه‌ها:** هر تغییر یک خواندن-و-نوشتن روی یک ورودی cache برای هر کانال است، زیر قفل per-channel، پس دو نمونه نمی‌توانند روی هم بنویسند. تغییر تا حدود دو ثانیه منتظر قفل می‌ماند و بعد پیوستن رد می‌شود (`Presence is unavailable.`).
- **نمونه‌ای که مرد:** هر نمونه ورودی‌های اتصال‌های خودش را هر یک‌سوم `PresenceEntryTtl` (به‌طور پیش‌فرض ۹۰ ثانیه، حداقل ۳) تازه می‌کند. ورودی اتصالی که نمونه‌اش مرده بعد از حداکثر همین مدت از فهرست می‌رود. **برای چنین انقضایی `MemberLeft` فرستاده نمی‌شود،** چون کسی نمانده که بفرستد: فهرست درست است، و client که اطمینان می‌خواهد می‌تواند دوباره `Subscribe` کند.
- کد سرور فهرست را با `IPresence.MembersAsync(channel)` می‌خواند.

## ارسال

`IBroadcaster.BroadcastAsync(event, ct)` رویداد را به هر کانالی که فهرست کرده می‌فرستد (تکراری‌ها حذف می‌شوند؛ رویداد بدون کانال چیزی نمی‌فرستد).

- **تحویل درون‌خطی و حداکثر یک‌بار است.** پیش از برگشتن `BroadcastAsync` انجام می‌شود. replay و صف پایدار ندارد: clientی که آفلاین باشد پیام را از دست می‌دهد. broadcasting از طریق `Naravel.Queue` بخشی از این بسته نیست.
- شکست transport به **فراخواننده پرتاب می‌شود**؛ کانالی که کسی گوش نمی‌دهد خطا نیست.
- چند نمونه به backplane‌ی SignalR نیاز دارند (Redis یا Azure SignalR Service). این گزینهٔ بومی SignalR است که خودتان پیکربندی می‌کنید.

### از `Naravel.Events`

`Naravel.Events` با **نوع دقیق رویداد** dispatch می‌کند و dispatch بر اساس نوع پایه یا interface ندارد، پس «هرچه `IBroadcastable` است broadcast شود» یک listener نمی‌شود. هر نوع را جدا اضافه کنید: `services.AddBroadcastOn<OrderShipped>()`. ثبت دوباره‌ی همان نوع بی‌ضرر است.

## driverها

`Broadcasting:Default` (پیش‌فرض `signalr`) و driverهای نام‌دار زیر `Broadcasting:Stores:<name>:Driver`، مثل Mail و Cache. نام‌ها را می‌توان در زمان اجرا با `BroadcastManager.Extend(name, factory)` اضافه کرد؛ `manager.Broadcaster("log")` broadcaster‌ای مقید به یک driver می‌دهد.

| driver | چه می‌کند |
|---|---|
| `signalr` | به group‌ای که نامش مثل کانال است می‌فرستد |
| `log` | نام رویداد و تعداد کانال‌ها را لاگ می‌کند. نام کانال و payload لاگ نمی‌شوند: اغلب شناسه دارند. |
| `null` | دور می‌ریزد |

## آزمون

`BroadcastFake` یک `IBroadcaster` است که آنچه فرستاده شد را ضبط می‌کند: به‌جای نمونهٔ واقعی ثبتش کنید و `AssertBroadcast(predicate)`، `AssertBroadcastCount(n)` یا `AssertNothingBroadcast()` را صدا بزنید. برای آزمون مجوزدهی، مثل آزمون‌های خود این بسته، `Microsoft.AspNetCore.TestHost` و `Microsoft.AspNetCore.SignalR.Client` را روی `MapNaravelBroadcasting` به کار ببرید. فهرست presence روی Redis واقعی هم، با دو اتصال که جای دو سرور هستند، آزمون می‌شود (`PresenceRedisTests`)؛ این آزمون‌ها فقط وقتی `NARAVEL_TEST_REDIS` تنظیم باشد اجرا می‌شوند، که job ‏`services` در CI تنظیم می‌کند.

## تله‌متری

Meter و activity source به نام `Naravel.Broadcasting`؛ شمارندهٔ `naravel.broadcasting.broadcasts` که فقط با `driver` و `outcome` (`succeeded`، `failed`، `cancelled`) برچسب می‌خورد. نام کانال، payload و توکن هرگز برچسب یا مقدار لاگ نمی‌شوند.

## Laravel به .NET

| Laravel | Naravel |
|---|---|
| `ShouldBroadcast`، `broadcastOn()`، `broadcastAs()`، `broadcastWith()` | `IBroadcastable`، `Channels()`، `BroadcastAs()`، `BroadcastWith()` |
| `broadcast($event)` | `IBroadcaster.BroadcastAsync(event)` |
| `Broadcast::channel('orders.{id}', fn ($user, $id) => ...)` | `Channels.Authorize("private-orders.{id}", (user, id) => ...)` |
| `/broadcasting/auth` | hub هنگام صدا زدن `Subscribe` توسط client مجوز می‌دهد |
| driverهای `pusher`، `ably`، `reverb`، `redis`، `log`، `null` | `signalr`، `log`، `null` (backplane جای `redis` را می‌گیرد) |
| به‌طور پیش‌فرض صف‌بندی‌شده | درون‌خطی، حداکثر یک‌بار |
| `Broadcast::channel('presence-room.{id}', fn ... => ['id' => ..., 'name' => ...])`، `Echo.join()` | `Channels.AuthorizePresence(..., (user, id) => new PresenceMember(userId, info))`، `Subscribe`، `MemberJoined`، `MemberLeft`، `IPresence` |

## آنچه پوشش نمی‌دهد

جایگزینی SignalR، driverهای Pusher/Ably/Reverb، broadcasting صف‌بندی‌شده، کتابخانه‌های client، پیام‌های «در حال تایپ» (client events)، و `MemberLeft` برای ورودی‌هایی که بعد از crash منقضی می‌شوند.
