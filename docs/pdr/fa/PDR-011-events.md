# PDR-011 — Events (`Naravel.Events`)

وضعیت: **تأییدشده در ۲۰۲۶-۱۰-۰۷.**

## زمینه

Laravel یک dispatcher رویداد سبک ارائه می‌دهد: listenerها بر اساس نوع ثبت می‌شوند، با نمونهٔ رویداد فراخوانی می‌شوند، به ترتیب ثبت اجرا می‌شوند و می‌توانند انتشار را متوقف کنند. بسیاری از برنامه‌ها از این الگو برای جداسازی عملیات domain از اثرات جانبی و کاهش پیچیدگی تست استفاده می‌کنند.

مبنای بومی .NET هم قوی است: delegateها، DI، `IHostedService` و راه‌حل‌های کاملاً تایپ‌شده برای ترکیب handlerها موجود است. شکاف مفید نه یک سیستم کامل و PHP-like، بلکه یک dispatcher کوچک و async-friendly با ترتیب صریح و adapter صف برای listenerهای طولانی‌مدت است.

سطح Laravel: `Illuminate\Contracts\Events\Dispatcher` و ثبت listener/subscriber. مبنای بومی: DI، delegateها، `CancellationToken` و سرویس‌های hosted provider-native. قاعده صفر توصیه می‌کند فقط بخش‌هایی اضافه شود که ارزش واقعی نسبت به الگوی بومی .NET داشته باشند.

## داوری با اولویت .NET بومی

یک dispatcher کوچک مبتنی بر listenerهای تایپ‌شده مناسب‌ترین انتخاب است. این راه‌حل از تلاش برای بازتولید کشف دینامیک و reflection در زمان اجرا ساده‌تر و امن‌تر است و از بارگذاری یک abstraction سنگین و MediatR-like به هستهٔ بسته جلوگیری می‌کند. سیستم رویداد باید ثبت صریح، dispatch تایپ‌شده، ترتیب اجرا، و یک adapter صف برای listenerهای زمان‌بر را پشتیبانی کند، در حالی که الگوهای بومی .NET برای برنامه در دسترس باقی بماند.

## تصمیم پیشنهادی

یک بستهٔ جدید `Naravel.Events` با قراردادهای اصلی زیر اضافه شود:

- `IEventDispatcher`: یک نمونهٔ رویداد را dispatch می‌کند و listenerها را از طریق DI حل می‌کند.
- `IEventListener<TEvent>`: قرارداد handler برای یک رویداد تایپ‌شده.
- `EventDispatcher`: اجرای پیش‌فرض؛ listener plans را یک‌بار محاسبه و برای هر نوع رویداد cache می‌کند و به ترتیب ثبت اجرا می‌کند.
- `EventFake`: ساختگی در-memory برای تست و assertionها.

گروه‌های subscriber با registration معمول DI یا فراخوانی صریح `Listen<TEvent>` ساخته می‌شوند؛ هسته متدهای subscriber
را استنباط نمی‌کند و assemblyها را scan نمی‌کند.

Events ماژول مبتنی بر driver نیست، پس `Manager.Extend` در اینجا کاربرد ندارد. برنامه‌ها می‌توانند dispatcher یا
listener invoker را با registration معمول DI جایگزین کنند.

ثبت listenerها صریح و مبتنی بر DI است. حالت رایج:

```csharp
services.AddNaravelEvents();
events.Listen<OrderPaid>((evt, context, ct) => listener.HandleAsync(evt, context, ct));
await events.DispatchAsync(new OrderPaid(orderId));
```

dispatcher یک رویداد تایپ‌شده را می‌پذیرد و listenerهای مرتبط را اجرا می‌کند. subscriptionهای `Listen<TEvent>` به
scope همان dispatcher محدودند، پس از listenerهای DI و به ترتیب ثبت اجرا می‌شوند و handle قابل dispose برمی‌گردانند.
این سیستم از موارد زیر پشتیبانی می‌کند:

- ترتیب ثبت (اول ثبت‌شده، اول اجرا)
- توقف انتشار (`EventDispatchContext` یا الگوی return `bool`)
- exception هر listener اجرای dispatch را متوقف کرده و به caller منتقل می‌شود
- overloadهای `CancellationToken` برای listenerهای async
- adapter صف برای listenerهای queued که انتخاب‌شده‌ها را از طریق worker Queue اجرا کند

Adapter `Naravel.Events.Queue` اختیاری و جدا از هستهٔ بسته طراحی می‌شود تا وابستگی به Queue ایجاد نشود. این adapter در پروژهٔ اختیاری قرار می‌گیرد و نه داخل بستهٔ اصلی.

اجرای listener با نخستین exception متوقف می‌شود و همان خطا به caller می‌رسد. این رفتار معنای معمول exception در .NET را حفظ می‌کند و مانع گزارش موفقیت dispatch ناقص می‌شود؛ برنامه‌هایی که best-effort مستقل می‌خواهند باید خطاهای listener را در خود listener بگیرند و ثبت کنند.

listener plans یک‌بار ساخته و برای هر نوع رویداد cache می‌شوند؛ در مسیر داغ هیچ reflection لازم نیست. این موضوع با تعریف انجام و نیازهای سرعت هم‌راستا است.

## قراردادها و کیفیت

- همهٔ ورودی‌های async `CancellationToken` می‌گیرند؛ هیچ sync-over-async مجاز نیست.
- جست‌وجوی listenerها بر اساس نوع دقیق رویداد انجام می‌شود و cache می‌گردد؛ هیچ scan زمان اجرا یا `Type.GetType` در مسیر داغ مجاز نیست.
- `EventFake` assertion قطعی برای نوع رویداد dispatch‌شده، تعداد، ترتیب dispatch و reset ارائه می‌دهد؛ stop propagation با dispatcher واقعی تست می‌شود.
- `Meter` و `ActivitySource` برای رویدادهای dispatched و خطاهای listener منتشر می‌شود.
- adapter صف از قرارداد تأییدشدهٔ `IJobDispatcher` استفاده می‌کند و با تستی پوشش می‌یابد که listener را در worker Memory queue اجرا کند.
- بستهٔ اصلی بدون وابستگی باقی می‌ماند؛ adapter queued فقط به interfaceهای Queue و بستهٔ Events وابسته است.

## راستی‌آزمایی لازم پس از تأیید

تست‌ها باید ترتیب ثبت، چند listener، stop propagation، سیاست خطا، cancellation، رفتار `Extend` (اگر اعمال شود)، و اجرای listener queued از طریق Memory queue را پوشش دهند. همچنین یک benchmark برای مسیر داغ dispatch اضافه شود. مستندات انگلیسی و فارسی، parity matrix و وضعیت نهایی stage پس از پیاده‌سازی به‌روزرسانی شوند. پیش از بسته‌شدن stage، دستور restore/build/test راه‌حل اجرا شود.

## گزینه‌های بررسی‌شده

1. **فقط استفاده از delegateها / DI بومی.** رد شد؛ مسیر مشترک به اندازهٔ کافی Laravel-like نیست و مدل ثبت صریح و adapter صف را پوشش نمی‌دهد.
2. **افزودن کشف listener با reflection در زمان اجرا.** رد شد؛ کندتر، شکننده‌تر و با قوانین سرعت و صراحت سازگار نیست.
3. **پیوند مستقیم رویدادها به Queue.** رد شد؛ این کار dependency cycle می‌سازد و دو مسئولیت جدا را با هم مخلوط می‌کند.
4. **انتخاب یک abstraction کامل MediatR-like.** رد شد؛ برای یک feature کوچک، abstraction سنگین و opinionated است.

## تأیید

مالک این محدوده را در ۲۰۲۶-۱۰-۰۷ تأیید کرد: dispatcher نازک برای رویدادهای تایپ‌شده با ثبت صریح، ترتیب اجرا، stop propagation، adapter صف و fake تست. هیچ کشف PHP-like یا reflection-based در این مرحله مجاز نیست.
