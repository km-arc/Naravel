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

## Listenerهای صف

adapter اختیاری `Naravel.Events.Queue` را کنار `Naravel.Queue` نصب و ثبت کنید:

```csharp
services.AddNaravelEventsQueue();
services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");
```

listenerهای صف با `IJobDispatcher` منتشر می‌شوند و worker آن‌ها را با alias پایدار و صریح resolve می‌کند. بستهٔ اصلی
Events وابستگی به Queue ندارد و نه assembly scan انجام می‌دهد و نه نوع CLR را از دادهٔ صف resolve می‌کند.

## مشاهده‌پذیری و تست

نام `Meter` و `ActivitySource` هر دو `Naravel.Events` است. `Naravel.Events.Testing.EventFake` رویدادها را ثبت می‌کند
و assertion نوع/تعداد و reset دارد؛ listener اجرا نمی‌کند. `AddNaravelEvents()` از `TryAdd` استفاده می‌کند، پس برنامه
می‌تواند `IEventDispatcher` را صریح جایگزین کند.
