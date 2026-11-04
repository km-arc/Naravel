# رویدادها

`Naravel.Events` dispatch ناهمگام و تایپ‌شدهٔ رویداد را با ثبت صریح listener، ترتیب قطعی و توقف انتشار فراهم می‌کند.

## چیست و کی استفاده شود

از `Naravel.Events` وقتی استفاده کنید که می‌خواهید «اتفاقی افتاد» را از «بعدش چه شود» جدا کنید: کدی که سفارش را پرداخت‌شده علامت می‌زند رویداد `OrderPaid` را dispatch می‌کند و listenerهای مستقل، read model را به‌روز می‌کنند، رسید می‌فرستند یا رکورد audit می‌نویسند. این معادل event dispatcher لاراول است، بدون بخش‌هایی که .NET خودش بهتر انجام می‌دهد: listenerها سرویس‌های معمولی DI هستند (بدون کشف به‌کمک قرارداد نام‌گذاری)، نوع رویداد در زمان کامپایل تعیین می‌شود (بدون نام رشته‌ای) و exceptionها همان exceptionهای معمولی‌اند. اگر یک کلاس فقط یک همکار را صدا می‌زند، مستقیم صدایش بزنید؛ رویداد وقتی می‌ارزد که چند واکنش مستقل یک محرک مشترک داشته باشند.

## شروع سریع

```sh
dotnet add package Naravel.Events
```

```csharp
using Naravel.Events;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelEvents();
builder.Services.AddScoped<IEventListener<OrderPaid>, WriteAuditLine>();   // listener یک سرویس معمولی DI است

var app = builder.Build();

app.MapPost("/orders/{id}/pay", async (string id, IEventDispatcher events, CancellationToken ct) =>
{
    await events.DispatchAsync(new OrderPaid(id, 42m), ct);   // وقتی برمی‌گردد، همهٔ listenerهای OrderPaid اجرا شده‌اند
    return Results.Ok($"Order {id} paid");
});

app.Run();

public sealed record OrderPaid(string OrderId, decimal Total);

public sealed class WriteAuditLine : IEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"audit: order {evt.OrderId} paid, total {evt.Total}");
        return Task.CompletedTask;
    }
}
```

`POST /orders/7/pay` خط audit را چاپ می‌کند و بعد پاسخ می‌دهد. کل سازوکار همین است: رویداد یک record است، listener کلاسی با `HandleAsync` است و `DispatchAsync` همهٔ listenerهای ثبت‌شده برای همان نوع دقیق رویداد را صدا می‌زند.

| عضو | کاری که می‌کند |
|---|---|
| `AddNaravelEvents()` | `IEventDispatcher` با lifetime از نوع scoped را ثبت می‌کند. از `TryAdd` استفاده می‌کند، پس برنامه می‌تواند dispatcher را جایگزین کند. |
| `IEventDispatcher.DispatchAsync<TEvent>(evt, ct)` | همهٔ listenerهای دقیقاً `TEvent` را به ترتیب اجرا می‌کند. |
| `IEventDispatcher.Listen<TEvent>(callback)` | یک callback را در scope جاری DI اضافه می‌کند و `IDisposable` برمی‌گرداند؛ با dispose آن callback حذف می‌شود. |
| `IEventListener<TEvent>.HandleAsync(evt, context, ct)` | قرارداد listener. |
| `IQueuedEventListener<TEvent>` | listenerی که بعداً روی worker صف اجرا می‌شود (به `Naravel.Events.Queue` نیاز دارد). |
| `EventDispatchContext.Stop()` و `.StopPropagation` | listenerهای باقی‌مانده را رد می‌کند، و نشان می‌دهد کسی این کار را کرده یا نه. |
| `Naravel.Events.Testing.EventFake` | یک test double که رویدادهای dispatch‌شده را ثبت می‌کند و هیچ listenerی اجرا نمی‌کند. |

dispatch چطور اجرا می‌شود: سرویس‌های DI با قرارداد `IEventListener<TEvent>` ابتدا و به ترتیب ثبت اجرا می‌شوند؛ callbackهای `Listen` پس از آن و به ترتیب ثبت فراخوانی می‌شوند. listener می‌تواند با `context.Stop()` ادامهٔ انتشار را متوقف کند. نخستین exception listener به caller منتقل می‌شود و dispatch متوقف می‌گردد.

## Listenerهای DI

```csharp
services.AddScoped<IEventListener<OrderPaid>, UpdateOrderReadModel>();
```

dispatcher scoped است و listener plan هر نوع رویداد را در همان scope cache می‌کند تا lifetime سرویس‌های scoped حفظ شود.
dispatch یک `CancellationToken` می‌گیرد و cancellation را به caller منتقل می‌کند.

listenerها و callbackهای `Listen` بر اساس همان `TEvent` دقیقِ محل فراخوانی انتخاب می‌شوند. `DispatchAsync<OrderPaid>(...)` به listenerهایی که برای کلاس پایه یا interface ثبت شده‌اند خبر نمی‌دهد و متغیری که از نوع `object` است به‌عنوان `object` dispatch می‌شود. برای هر نوع رویداد مشخص یک listener ثبت کنید؛ یک کلاس می‌تواند چند interface از نوع `IEventListener<T>` را پیاده‌سازی کند.

## یک مثال کامل

```csharp
// Events/OrderPaid.cs
public sealed record OrderPaid(string OrderId, decimal Total);

// Listeners/UpdateOrderReadModel.cs - runs inline, in the dispatching request
public sealed class UpdateOrderReadModel(IOrderReadModel readModel) : IEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default) =>
        readModel.MarkPaidAsync(evt.OrderId, evt.Total, cancellationToken);
}

// Listeners/SendReceipt.cs - queued: runs later on a worker
public sealed class SendReceipt(IReceiptMailer mailer) : IQueuedEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default) =>
        mailer.SendAsync(evt.OrderId, cancellationToken);
}
```

```csharp
// Program.cs
builder.Services.AddNaravelEvents();
builder.Services.AddScoped<IEventListener<OrderPaid>, UpdateOrderReadModel>();

// Only needed for queued listeners (Naravel.Events.Queue + Naravel.Queue + a driver):
builder.Services.AddQueue(builder.Configuration).AddMemoryDriver(builder.Configuration);
// usings: Naravel.Queue.Extensions (AddQueue, AddQueueWorker) and Naravel.Queue.Memory (AddMemoryDriver)
builder.Services.AddNaravelEventsQueue();
builder.Services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");
builder.Services.AddQueueWorker();
```

مثال کامل بالا به صورت تست اجرا می‌شود: `tests/Naravel.Events.Tests/DocsExampleTests.cs` (listener درون‌خطی، listener صف‌شده روی worker، `context.Stop()` و `EventFake`). هر تغییری در یکی را در دیگری هم اعمال کنید.

```json
{
  "NaravelQueue": {
    "Default": "events",
    "Stores": { "events": { "Driver": "memory" } }
  }
}
```

```csharp
// Somewhere that takes an IEventDispatcher (a controller, a service, a minimal API handler)
app.MapPost("/orders/{id}/pay", async (string id, decimal total, IEventDispatcher events, CancellationToken ct) =>
{
    await events.DispatchAsync(new OrderPaid(id, total), ct);
    return Results.Accepted();
});
```

`UpdateOrderReadModel` داخل همان درخواست و پیش از برگشتن `DispatchAsync` اجرا می‌شود؛ اگر exception بدهد به فراخواننده می‌رسد و listenerهای بعدی اجرا نمی‌شوند. `SendReceipt` در صف منتشر می‌شود و فوراً برمی‌گردد؛ worker بعداً و دست‌کم یک بار آن را اجرا می‌کند.

برای متوقف کردن listenerهای بعدی از داخل یک listener، `context.Stop()` را صدا بزنید:

```csharp
public sealed class RejectFraudulentOrders : IEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        if (evt.Total > 100_000m) context.Stop(); // listeners registered after this one are skipped
        return Task.CompletedTask;
    }
}
```

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

## سه نوع listener کنار هم

یک رویداد، یک listener در DI که می‌تواند انتشار را متوقف کند، یک listener صف‌شده و یک callback از نوع `Listen` در محدودهٔ درخواست. `IOrderReadStore` و `IMailer` نمایندهٔ سرویس‌های خودتان هستند. نسخهٔ قابل اجرا در `samples/Naravel.Sample` (صفحهٔ `/events`) است.

```csharp
// 1. The event: a small serializable record (ids and scalars only).
public sealed record OrderPaid(string OrderId, string Email, decimal Total);

// 2. A DI listener. It runs inside DispatchAsync, in registration order.
public sealed class UpdateOrderReadModel(IOrderReadStore store) : IEventListener<OrderPaid>
{
    public async Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        await store.MarkPaidAsync(evt.OrderId, evt.Total, cancellationToken);
        if (evt.Total > 10_000m)
            context.Stop();                       // later listeners and Listen callbacks do not run
    }
}

// 3. A queued listener: dispatch only enqueues a job, the worker calls HandleAsync later (at-least-once).
public sealed class SendReceipt(IMailer mailer) : IQueuedEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
        => mailer.SendReceiptAsync(evt.Email, evt.OrderId, cancellationToken);   // make it idempotent
}

// 4. Program.cs
builder.Services.AddNaravelEventsQueue();                                          // also registers the dispatcher
builder.Services.AddScoped<IEventListener<OrderPaid>, UpdateOrderReadModel>();
builder.Services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");

// 5. Dispatch from an endpoint, and add a request-scoped callback.
app.MapPost("/orders/{id}/pay", async (string id, string email, decimal total, IEventDispatcher events, CancellationToken ct) =>
{
    using var audit = events.Listen<OrderPaid>((evt, _, _) =>
    {
        Console.WriteLine($"audit: {evt.OrderId}");      // runs after the DI listeners, then is unsubscribed on Dispose
        return Task.CompletedTask;
    });

    await events.DispatchAsync(new OrderPaid(id, email, total), ct);
    return Results.Accepted();
});
```

ترتیب اجرا برای مبلغ 120: ابتدا `UpdateOrderReadModel`، بعد callback مربوط به `Listen`؛ `SendReceipt` بعداً در worker صف اجرا می‌شود (workerی روی connection پیش‌فرض و queue با نام `default` اجرا کنید). برای مبلغ بالاتر از 10,000 listener اول انتشار را متوقف می‌کند و چیز دیگری اجرا نمی‌شود.

## خطاها

| چه وقت | چه می‌شود |
|---|---|
| `DispatchAsync(null)` | `ArgumentNullException`. |
| token پیش از listenerها یا میان آن‌ها لغو شود | `OperationCanceledException`؛ listener دیگری اجرا نمی‌شود و شکست حساب نمی‌شود. |
| یک listener exception پرتاب کند | همان exception به caller متد `DispatchAsync` می‌رسد؛ listenerهای بعدی اجرا نمی‌شوند و dispatch شکست‌خورده شمرده می‌شود. |
| listener صف با `AddQueuedEventListener` ثبت نشده باشد | `InvalidOperationException` هنگام dispatch: `Queued listener 'MyApp.SendReceipt' for event 'MyApp.OrderPaid' has no explicit queue alias registration.` |
| alias خالی به `AddQueuedEventListener` داده شود | `ArgumentException`. |
| یک alias دو بار ثبت شود | `InvalidOperationException`: `Queued event listener alias 'order-paid.receipt' is registered more than once.` |
| یک listener دو بار برای یک رویداد ثبت شود | `InvalidOperationException`: `Queued event listener 'MyApp.SendReceipt' for 'MyApp.OrderPaid' is registered more than once.` |
| worker jobی با aliasی بگیرد که نمی‌شناسد (مثلاً از استقرار قدیمی‌تر یا جدیدتر) | `InvalidOperationException`: `Queued event listener alias 'order-paid.receipt' is not registered.` سپس job از قواعد retry و failed-job در Queue پیروی می‌کند. |
| payload صف به `null` deserialize شود | `InvalidDataException`: `Queued event payload for alias 'order-paid.receipt' was null.` |

دو خطای ثبت تکراری وقتی پرتاب می‌شوند که registry مربوط به listenerهای صف برای نخستین بار از روی ثبت‌ها ساخته شود، نه هنگام فراخوانی `AddQueuedEventListener`.

assertionهای `EventFake` با `InvalidOperationException` شکست می‌خورند:

| assertion | پیام |
|---|---|
| `AssertDispatched<OrderPaid>()` وقتی هیچ رویدادی dispatch نشده | `No OrderPaid event was dispatched.` |
| `AssertDispatched<OrderPaid>(1)` وقتی هیچ رویدادی نبوده | `Expected 1 OrderPaid events to be dispatched, but found 0.` |

## مشاهده‌پذیری و تست

نام `Meter` و `ActivitySource` هر دو `Naravel.Events` است. متریک‌ها `naravel.events.dispatched` و `naravel.events.failed` (شمارنده) و `naravel.events.dispatch.duration` (histogram به میلی‌ثانیه) هستند؛ نام span برابر `events.dispatch` است. `Naravel.Events.Testing.EventFake` رویدادها را ثبت می‌کند
و assertion نوع/تعداد و reset دارد؛ listener اجرا نمی‌کند. `AddNaravelEvents()` از `TryAdd` استفاده می‌کند، پس برنامه
می‌تواند `IEventDispatcher` را صریح جایگزین کند.

### تست کدی که رویداد dispatch می‌کند

`EventFake` را جایی تزریق کنید که `IEventDispatcher` انتظار می‌رود، کد مورد تست را اجرا کنید و روی رویدادهای dispatch‌شده assert بزنید. fake، listener اجرا نمی‌کند؛ پس تست کد dispatch‌کننده نیازی به read model، mailer یا صف ندارد:

```csharp
[Fact]
public async Task Paying_an_order_dispatches_OrderPaid()
{
    var events = new EventFake();
    var service = new CheckoutService(events);

    await service.PayAsync("order-7", 42m, CancellationToken.None);

    var evt = events.AssertDispatched<OrderPaid>();   // throws when none was dispatched
    Assert.Equal("order-7", evt.OrderId);
    events.AssertDispatched<OrderPaid>(1);            // exactly one
    events.Reset();                                   // forget everything recorded so far
}
```

یک listener را جداگانه با ساختن آن و صدا زدن `HandleAsync` همراه با یک `EventDispatchContext` تازه تست کنید، و اگر ممکن است انتشار را متوقف کند `context.StopPropagation` را بررسی کنید.
