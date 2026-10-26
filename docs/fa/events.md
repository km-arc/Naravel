# رویدادها

`Naravel.Events` dispatch ناهمگام و تایپ‌شدهٔ رویداد را با ثبت صریح listener، ترتیب قطعی و توقف انتشار فراهم می‌کند.

## شروع سریع

```csharp
services.AddNaravelEvents();
events.Listen<OrderPaid>((evt, context, ct) => listener.HandleAsync(evt, context, ct));
await events.DispatchAsync(new OrderPaid(orderId));
```

`Listen<TEvent>` یک callback را در scope جاری DI ثبت و `IDisposable` برای لغو subscription برمی‌گرداند. سرویس‌های
DI با قرارداد `IEventListener<TEvent>` ابتدا و به ترتیب ثبت اجرا می‌شوند؛ callbackهای `Listen` پس از آن و به ترتیب ثبت
فراخوانی می‌شوند. listener می‌تواند با `context.Stop()` ادامهٔ انتشار را متوقف کند. نخستین exception به caller منتقل
می‌شود و dispatch متوقف می‌گردد.

## Listenerهای DI

```csharp
services.AddScoped<IEventListener<OrderPaid>, UpdateOrderReadModel>();
```

dispatcher scoped است و listener plan هر نوع رویداد را در همان scope cache می‌کند تا lifetime سرویس‌های scoped حفظ شود.
dispatch یک `CancellationToken` می‌گیرد و cancellation را به caller منتقل می‌کند.

listenerها و callbackهای `Listen` بر اساس همان `TEvent` دقیقِ محل فراخوانی انتخاب می‌شوند. `DispatchAsync<OrderPaid>(...)` به listenerهایی که برای کلاس پایه یا interface ثبت شده‌اند خبر نمی‌دهد و متغیری که از نوع `object` است به‌عنوان `object` dispatch می‌شود. برای هر نوع رویداد مشخص یک listener ثبت کنید؛ یک کلاس می‌تواند چند interface از نوع `IEventListener<T>` را پیاده‌سازی کند.

## Listenerهای صف

adapter اختیاری `Naravel.Events.Queue` را کنار `Naravel.Queue` نصب و ثبت کنید:

```csharp
services.AddNaravelEventsQueue();
services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");
```

listenerهای صف با `IJobDispatcher` منتشر می‌شوند و worker آن‌ها را با alias پایدار و صریح resolve می‌کند. بستهٔ اصلی
Events وابستگی به Queue ندارد و نه assembly scan انجام می‌دهد و نه نوع CLR را از دادهٔ صف resolve می‌کند.

محدودیت‌های adapter که باید بدانید:

- به‌طور پیش‌فرض dispatch روی connection پیش‌فرض Queue و queue با نام `default` انجام می‌شود؛ پس workerی اجرا کنید که همان‌ها را مصرف می‌کند. برای مسیر دیگر، گزینه‌های dispatch بدهید: `AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt", o => o.OnConnection("redis").OnQueue("mail").WithMaxAttempts(5))`. این action برای هر dispatch روی یک `DispatchOptions` تازه اجرا می‌شود و چیزی بین dispatchها مشترک نیست.
- به‌طور پیش‌فرض رویداد با `System.Text.Json` مبتنی بر reflection serialize می‌شود. برای trimming و Native AOT به‌جای آن metadata تولیدشده با source generator بدهید: `AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt", MyJsonContext.Default.OrderPaid)` (در صورت نیاز با action گزینه‌های dispatch). در هر دو حالت رویدادها را کوچک و قابل serialize نگه دارید.
- listener صف‌شده بعداً و با یک `EventDispatchContext` تازه اجرا می‌شود: نمی‌تواند انتشار را متوقف کند، کدِ dispatch‌کننده منتظر آن نمی‌ماند و exceptionهای آن از قواعد retry و failed-job در Queue پیروی می‌کنند، نه سیاست نخستین exception. اجرا at-least-once است؛ listenerها را idempotent بنویسید.
- listenerی که `IQueuedEventListener<TEvent>` را پیاده‌سازی کرده اما با `AddQueuedEventListener` ثبت نشده، چون alias ندارد در زمان dispatch با `InvalidOperationException` شکست می‌خورد.

## مشاهده‌پذیری و تست

نام `Meter` و `ActivitySource` هر دو `Naravel.Events` است. `Naravel.Events.Testing.EventFake` رویدادها را ثبت می‌کند
و assertion نوع/تعداد و reset دارد؛ listener اجرا نمی‌کند. `AddNaravelEvents()` از `TryAdd` استفاده می‌کند، پس برنامه
می‌تواند `IEventDispatcher` را صریح جایگزین کند.
