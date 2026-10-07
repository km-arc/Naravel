# PDR-011 — رویدادها (Events)

**داوری با اولویت .NET بومی:** .NET dispatcher رویداد برنامه‌ای داخلی و سازگار با DI ندارد. `event` و delegateهای CLR ناشر را به نمونه‌های مشخص مشترک گره می‌زنند و `System.Diagnostics.DiagnosticListener` کانال تشخیصی است، نه باس رویداد برنامه. کتابخانه‌های سبک MediatR وابستگی ثالث هستند (قاعدهٔ ۸ در AGENTS.md افزودن آن را بدون PDR ممنوع می‌کند) و مفاهیم request/response و pipeline را می‌آورند که Naravel به آن‌ها نیاز ندارد. یک dispatcher کوچک و typed روی `Microsoft.Extensions.DependencyInjection` ارزش واقعی اضافه می‌کند و کوچک می‌ماند.

وضعیت: **تأییدشده در ۲۰۲۶-۱۰-۰۷.** مالک پیشنهاد زیر را همان‌گونه که نوشته شده تأیید کرد (هر هشت مورد بخش «تأیید درخواست‌شده»). R06.T02 تا T04 می‌توانند ادامه یابند (Gate در `roadmap/stages/R06-events.md`).

## زمینه

سطح Laravel: `Illuminate\Contracts\Events\Dispatcher` شامل `listen`، `dispatch`، `subscribe`، `until`، listenerهای wildcard، کشف خودکار listener، listenerهای صف‌شونده (`ShouldQueue`) و `Event::fake()`. مبنای بومی: delegateها، DI و hosted serviceها. ارزش در .NET: رویدادهای برنامهٔ جداشده از هم با چرخهٔ عمر صریح و قابل‌آزمون برای listener، listenerهای resolve‌شده از DI، telemetry و fake — بدون مکانیک PHP (رشتهٔ جادویی، wildcard روی نام کلاس، پویش پوشه/کشف مبتنی بر reflection).

محدودیت‌هایی که طراحی را شکل می‌دهند:
- **قاعدهٔ ۳ (وابستگی یک‌طرفه):** هستهٔ `Naravel.Events` نباید به `Naravel.Queue` وابسته باشد. listenerهای صف‌شونده در یک پروژهٔ آداپتر اختیاری قرار می‌گیرند.
- **S2:** هیچ reflection در مسیر داغ dispatch نباشد؛ برنامهٔ listenerها برای هر نوع رویداد یک بار ساخته و cache می‌شود.
- **OD-07:** هیچ نوع CLR نباید از داده‌های ذخیره‌شده resolve شود مگر از طریق رجیستری alias صریح. payload مربوط به listenerهای صف‌شونده باید از رجیستری نوع job در R00 پیروی کند.
- Events درایور قابل‌جایگزینی ندارد، پس از `Manager<TDriver, TOptions>` **استفاده نمی‌کند** (قاعدهٔ ۲ در AGENTS.md فقط برای ماژول‌های مبتنی بر درایور است). این یک عدم‌استفادهٔ آگاهانه است، نه جاافتادگی.

## تصمیم پیشنهادی

### بسته‌ها

- `Naravel.Events` — قراردادها، dispatcher، options، telemetry و `EventFake`؛ فقط به abstractionهای `Microsoft.Extensions.*` وابسته است (manager فونداسیون لازم ندارد، بالا را ببینید).
- `Naravel.Events.Queue` (اختیاری) — listenerهای صف‌شونده را از طریق `IJobDispatcher` ارسال می‌کند؛ به `Naravel.Events` و `Naravel.Queue` ارجاع می‌دهد.
- `tests/Naravel.Events.Tests`؛ هر دو پروژهٔ منبع و پروژهٔ تست به `Naravel.slnx` اضافه می‌شوند.

### شروع سریع (S1: سه خط کد کاربر)

```csharp
services.AddNaravelEvents();
services.AddEventListener<OrderPaid, SendReceipt>();          // listener کلاسی که از DI resolve می‌شود
await events.DispatchAsync(new OrderPaid(orderId));           // IEventDispatcher تزریق‌شده
// یا بدون کلاس:
using var subscription = events.Listen<OrderPaid>((e, ct) => ValueTask.CompletedTask);
```

### قراردادها

- `IEventDispatcher` — `ValueTask DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)` و `IDisposable Listen<TEvent>(Func<TEvent, CancellationToken, ValueTask> listener)`. رویدادها نوع‌های ساده‌ی .NET (record) هستند؛ کلاس پایه یا marker لازم نیست.
- `IEventListener<in TEvent>` — `ValueTask HandleAsync(TEvent @event, CancellationToken ct)`.
- `IEventSubscriber` — `void Subscribe(IEventRegistrar events)`؛ یک کلاس چند listener ثبت می‌کند (subscriberهای Laravel). با `AddEventSubscriber<T>()` ثبت می‌شود.
- `IStoppableEvent` (اختیاری، به سبک PSR-14) — `bool PropagationStopped { get; }`؛ بخش توقف انتشار را ببینید.
- `EventsOptions` — سیاست استثنا (پایین). پیکربندی store/driver وجود ندارد.

### رفتار

1. **تطبیق.** listener مربوط به `TEvent` رویدادهایی را دریافت می‌کند که نوع زمان اجرایشان `TEvent` است یا از آن ارث می‌برد/آن را پیاده می‌کند (کلاس پایه و interface). مجموعهٔ listenerهای هر نوع رویداد مشخص، در نخستین dispatch یک بار محاسبه و به‌صورت آرایه cache می‌شود (تنها استفاده از reflection، که هرگز در هر dispatch تکرار نمی‌شود). listenerهای wildcard/الگوی رشته‌ای پورت نمی‌شوند.
2. **ترتیب.** listenerها به‌ترتیب ثبت و پشت‌سرهم اجرا می‌شوند: ابتدا listenerهای ثبت‌شده در DI به ترتیب ثبت سرویس، سپس ثبت‌های `Listen` در زمان اجرا به ترتیب فراخوانی. در نسخهٔ ۱ عدد اولویت وجود ندارد.
3. **ثبت در زمان اجرا.** `Listen` و `IDisposable` برگشتی از snapshot با copy-on-write استفاده می‌کنند؛ تغییر مجموعهٔ listenerها فقط برنامه‌های cache‌شدهٔ متأثر را باطل می‌کند. dispatch در مسیر داغ هیچ lockی نمی‌گیرد.
4. **توقف انتشار.** اگر رویداد `IStoppableEvent` را پیاده کند، dispatcher پیش از فراخوانی هر listener مقدار `PropagationStopped` را بررسی می‌کند و با true شدن آن متوقف می‌شود. این جایگزین قرارداد Laravel («listener مقدار `false` برگرداند») با یک سیگنال صریح و typed است.
5. **سیاست استثنا.** پیش‌فرض `Propagate`: نخستین استثنا به فراخواننده پرتاب می‌شود و listenerهای باقی‌مانده اجرا نمی‌شوند (رفتار Laravel). گزینهٔ opt-in با نام `ContinueAndAggregate`: همهٔ listenerها اجرا می‌شوند و در پایان یک `AggregateException` پرتاب می‌شود. لغو (cancellation) همیشه بی‌درنگ منتشر می‌شود.
6. **طول عمر.** `IEventDispatcher` singleton است. listenerهای Singleton یا Transient بدون ساخت scope resolve می‌شوند؛ listener ثبت‌شده به‌صورت Scoped درون یک `IServiceScope` تازه برای همان dispatch resolve می‌شود. طول عمر هنگام ثبت معلوم است، پس برنامه این را یک بار تعیین می‌کند.
7. **بدون جمع‌آوری پاسخ / `until`.** `DispatchAsync` نتیجهٔ listenerها را برنمی‌گرداند. `until` در Laravel (نخستین پاسخ غیر null) پورت نمی‌شود.
8. **Telemetry (S3).** `Meter` و `ActivitySource` با نام `Naravel.Events`: تعداد dispatch، تعداد listener، مدت اجرای listener و خطاها، با برچسب نوع رویداد. هدف در حالت پایدار، نبودن allocation اضافه در هر dispatch جز خود فراخوانی listenerهاست (در R06.T04 سنجیده می‌شود).
9. **Fake (S3).** `EventFake : IEventDispatcher` رویدادهای dispatch‌شده را ثبت می‌کند و می‌تواند listenerها را اجرا یا نادیده بگیرد؛ assertionها: `AssertDispatched<T>(predicate?)`، `AssertNotDispatched<T>()`، `AssertDispatchedTimes<T>(n)` و `AssertNothingDispatched()`.

### listenerهای صف‌شونده (آداپتر `Naravel.Events.Queue`)

- `AddQueuedEventListener<TEvent, TListener>(Action<DispatchOptions>? configure = null)` یک listener هسته ثبت می‌کند که یک job صف می‌سازد و از طریق `IJobDispatcher` می‌فرستد؛ worker بعداً `TListener` را resolve و `HandleAsync` را فراخوانی می‌کند. هسته هرگز به صف ارجاع نمی‌دهد.
- job و payload رویداد باید در رجیستری نوع/alias موجود job ثبت شوند؛ نوع ثبت‌نشده هنگام ثبت (نه هنگام dispatch) خطا می‌دهد (OD-07). رویداد باید توسط serializer صف قابل سریال‌سازی باشد.
- ارسال به صف در لحظهٔ `DispatchAsync` انجام می‌شود؛ معناشناسی after-commit خارج از دامنه است (هنوز abstraction تراکنش پایگاه‌داده وجود ندارد).

### صریحاً پورت نمی‌شود

رویدادهای model/observer (خارج از دامنهٔ R06)؛ listenerهای wildcard؛ کشف خودکار listener با پویش assemblyها (فراخوانی صریح `AddEventListener` مسیر پشتیبانی‌شده است؛ helper مبتنی بر source generator یا پویش assembly می‌تواند بعداً در PDR جداگانه پیشنهاد شود)؛ `until`؛ `ShouldDispatchAfterCommit`؛ helper سراسری `event()` و facade ایستا.

## راستی‌آزمایی لازم پس از تأیید

تست‌ها شامل: ترتیب و چند listener؛ تطبیق نوع پایه و interface؛ `Listen` و لغو اشتراک در زمان اجرا و ابطال برنامه؛ توقف انتشار؛ هر دو سیاست استثنا و cancellation؛ هر طول عمر listener؛ subscriberها؛ assertionهای `EventFake`؛ انتشار Meter/ActivitySource. `Naravel.Events.Queue` تستی با صف Memory دارد که اجرای listener را درون worker ثابت می‌کند. یک benchmark تعداد dispatch در ثانیه و allocation در حالت پایدار را می‌سنجد (R06.T04). راهنماها (EN/FA)، جدول‌های parity، وضعیت و changelog به‌روزرسانی می‌شوند و PDR به هر دو زبان وجود دارد.

## گزینه‌های بررسی‌شده

1. **فقط `event`/delegateهای CLR.** به‌عنوان پاسخ کامل رد شد: listener از DI، سیاست ترتیب/استثنا، telemetry و fake ندارد. delegateها از طریق `Listen` درون dispatcher در دسترس می‌مانند.
2. **پذیرش MediatR (یا مشابه).** رد شد: وابستگی ثالث و مسئلهٔ متفاوت (request/response و pipeline)؛ قاعدهٔ ۸ در AGENTS.md.
3. **ساخت Events روی `Manager<TDriver, TOptions>`.** رد شد: چیزی برای جابه‌جایی وجود ندارد؛ manager پیکربندی بی‌ارزش اضافه می‌کند.
4. **قرار دادن listenerهای صف‌شونده در هسته.** رد شد: وابستگی Events به Queue ایجاد می‌کند (قاعدهٔ ۳) و احتمال چرخه با قابلیت‌های مبتنی بر Queue را پدید می‌آورد.
5. **کشف listener با reflection/attribute.** رد شد: مکانیک PHP است (قاعدهٔ ۶)، با S2 و trimming/AOT ناسازگار است و ثبت صریح به‌اندازهٔ کافی ساده است.
6. **برگرداندن `bool` از listener برای توقف انتشار.** به نفع سیگنال typed با نام `IStoppableEvent` رد شد؛ مقدار برگشتی جادویی راحت اشتباه می‌شود و برای delegateها و listenerهای async یکنواخت بیان نمی‌شود.

## تأیید درخواست‌شده

از مالک خواسته می‌شود هر مورد زیر را تأیید، اصلاح یا رد کند. در صورت تأیید بدون توضیح، پیش‌فرض‌های بالا اعمال می‌شود.

1. dispatcher کوچک روی DI؛ بدون لایهٔ Manager/driver؛ بدون وابستگی MediatR.
2. تطبیق بر اساس نوع زمان اجرا شامل نوع‌های پایه و interfaceها؛ بدون listener wildcard.
3. اجرا به ترتیب ثبت، بدون عدد اولویت در نسخهٔ ۱.
4. `IStoppableEvent` برای توقف انتشار.
5. سیاست پیش‌فرض استثنا `Propagate` و گزینهٔ opt-in با نام `ContinueAndAggregate`.
6. dispatcher به‌صورت singleton؛ listenerهای Scoped در هر dispatch scope مخصوص دریافت می‌کنند.
7. listenerهای صف‌شونده فقط در `Naravel.Events.Queue` و با رجیستری نوع job موجود (OD-07).
8. در R06 فقط ثبت صریح (بدون کشف با پویش assembly).

تأیید مالک: تأییدشده در ۲۰۲۶-۱۰-۰۷، هر هشت مورد، بدون اصلاح.
