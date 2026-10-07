# PDR-014 — اعلان‌ها (`Naravel.Notifications`)

**داوری با اولویت .NET بومی:** DI و APIهای provider در .NET کانال‌های تحویل مستقل را پوشش می‌دهند. Naravel فقط وقتی باید قرارداد fan-out تایپ‌شدهٔ کوچکی اضافه کند که یک اعلان باید به چند کانال صریح برسد؛ trait مدل، مسیریابی ضمنی یا پشتهٔ transport دیگری اضافه نشود.

وضعیت: **تأییدشده در ۲۰۲۶-۱۰-۰۷.**

## زمینه

Notificationهای Laravel یک اعلان typed را از طریق یک یا چند channel ارسال می‌کنند و routing گیرنده و پشتیبانی صف دارند. در .NET برنامه می‌تواند مستقیماً هر provider را فراخوانی کند، اما انتخاب تکراری channel و آماده‌سازی تست ممکن است به boilerplate برنامه تبدیل شود. لایهٔ هماهنگی حداقلی می‌تواند ارزش داشته باشد، به شرط حفظ channelهای بومی.

سطح Laravel: `Illuminate\Contracts\Notifications\Dispatcher`، channel manager و routing گیرنده. مبنای بومی: DI، interfaceهای typed برای providerها، Mail و Queue. مطابق نقشهٔ راه این پیشنهاد به R07 و R09 وابسته است و به ماژول Events وابسته نیست.

## تصمیم پیشنهادی

بستهٔ کوچک `Naravel.Notifications` با قراردادهای صریح اعلان و channel اضافه شود. فراخواننده گیرنده و نام channelهای انتخابی را صریح می‌دهد. channel از attributeهای مدل استنباط نشود و routing سراسری اعلان اضافه نشود.

حالت رایج:

```csharp
services.AddNaravelNotifications();
await notifications.SendAsync(recipient, new InvoiceReady(invoiceId), ["mail"], cancellationToken);
```

`INotificationSender`، `INotificationChannel<TNotification>` و context اعلان شامل گیرندهٔ صریح و cancellation token تعریف شوند. ترتیب ثبت DI به معنی انتخاب channel نیست؛ نام‌های درخواستی به ترتیب caller اجرا می‌شوند. channel ناموجود با خطای روشن مواجه شود. شکست channel به‌صورت پیش‌فرض dispatch را متوقف کند و گزارش شود؛ تحویل جزئی نباید موفقیت کامل جلوه داده شود.

یکپارچه‌سازی Mail و Queue در adapterهای جدا می‌ماند. notificationهای queued از aliasهای صریح job و قراردادهای تأییدشدهٔ Queue استفاده کنند؛ نام دلخواه CLR serialization نشود. برنامه‌ها بتوانند channelهای دیگر را بدون تغییر core فراهم کنند. `NotificationFake` برای assertion قطعی ارائه شود.

## قراردادها و کیفیت

- نوع اعلان یک شیء معمول typed است؛ trait مدل، کشف reflection و propertyهای ضمنی گیرنده وجود ندارد.
- dispatch async و دارای `CancellationToken` است.
- fake گیرنده، اعلان و channelهای درخواستی را ثبت کند؛ payload حساس در telemetry ذخیره نشود.
- `Meter` و `ActivitySource` برای dispatch و نتیجهٔ channel ارائه شود و فقط tagهای bounded شامل نام channel و outcome باشند.
- بستهٔ core وابستگی provider نداشته باشد؛ adapterهای channel مستقل و opt-in باشند.

## راستی‌آزمایی لازم پس از تأیید

ترتیب چند channel، channel ناموجود، cancellation، شکست channel و گزارش تحویل جزئی، assertionهای fake، یکپارچگی Mail و اجرای صف با Memory queue تست شوند. اگر dispatch مسیر داغ اندازه‌گیری‌شده است، benchmark اضافه شود. مستندات EN/FA، parity، changelog و checklist مرحله به‌روزرسانی و سپس راستی‌آزمایی کامل solution و roadmap checker اجرا شود.

## گزینه‌های بررسی‌شده

1. **فراخوانی مستقیم هر provider.** برای یک channel معتبر و ترجیحی است؛ برای fan-out یک notification typed به شکل یکنواخت و قابل تست، تکراری می‌شود.
2. **trait مدل و routing استنباط‌شده به سبک Laravel.** رد شد؛ گیرنده و channel باید در .NET صریح باشند.
3. **قرار دادن همهٔ transportها در بستهٔ Notifications.** رد شد؛ Mail و Queue adapterهای اختیاری می‌مانند و مالکیت provider جداست.
4. **وابستگی اجباری به Events.** رد شد؛ تحویل notification هماهنگی صریح است و dependencyهای R10 در نقشهٔ راه شامل R06 نیستند.

## تأیید

مالک این پیشنهاد را در ۲۰۲۶-۱۰-۰۷، شامل fan-out صریح channelهای typed، وابستگی‌های Mail/Queue ذکرشده و adapterهای opt-in جداگانه تأیید کرد.
