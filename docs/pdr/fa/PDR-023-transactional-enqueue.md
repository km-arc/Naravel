# PDR-023 — درج تراکنشی job در صف پایگاه داده (Outbox در ذات طراحی)

**حکم native-first:** `SaveChanges` در EF Core همهٔ تغییرات را اتمیک commit می‌کند. صف Database، jobها را ردیف جدول `QueueJobs` نگه می‌دارد؛ پس یک job می‌تواند فقط یک ردیف دیگر در `DbContext` خودِ فراخواننده باشد. زیرساخت جدید لازم نیست.

وضعیت: **تأیید‌شده 2026-10-28** با دستور مالک («PDR را بساز، سپس گزینهٔ ۲ را پیاده کن»).

## زمینه

تحلیل تراکنش و dispatch (2026-10-28) یک شکاف dual-write یافت. `JobDispatcher.DispatchAsync` از `IQueueDriver` می‌فرستد و `DatabaseQueueDriver` یک `DbContext` **جدا** از `IDbContextFactory` می‌سازد و فوراً ذخیره می‌کند. پس job هیچ‌وقت بخشی از تراکنش فراخواننده نیست، حتی اگر جدول صف و جداول کسب‌وکار در یک پایگاه داده باشند:

- commit موفق و dispatch ناموفق یا crash پیش از آن: پیام **بی‌صدا گم می‌شود**.
- dispatch موفق و سپس rollback: job برای داده‌ای که وجود ندارد اجرا می‌شود.
- crash worker بعد از اجرا و پیش از ack: اجرای دوباره (at-least-once، قبلاً مستند شده؛ jobها باید idempotent باشند).

## تصمیم

افزودن `DatabaseJobEnqueuer` اختیاری به `Naravel.Queue.Database`:

```csharp
db.Orders.Add(order);
enqueuer.Enqueue(db, new SendReceiptJob(order.Id));   // ردیابی می‌شود، هنوز ارسال نشده
await db.SaveChangesAsync();                          // سفارش و job با هم commit می‌شوند، یا هیچ‌کدام
```

- `Enqueue(DbContext, IJob, Action<DispatchOptions>? = null)` پیام را دقیقاً مثل `DispatchAsync` می‌سازد (همان serializer، alias registry، صف، attempts، تأخیر، اولویت و trace) و یک `JobRecord` به context اضافه می‌کند و شناسهٔ job را برمی‌گرداند. هیچ I/O انجام نمی‌دهد.
- workerِ موجود نقش relay را دارد: همین حالا `QueueJobs` را poll و ردیف را با compare-and-swap رزرو می‌کند. جدول دوم، فرایند relay و پکیج جدید لازم نیست.
- برای جلوگیری از تکرار `BuildMessage`، `Naravel.Queue` یک `IJobMessageFactory` عمومی کوچک می‌گیرد که `JobDispatcher` پیاده می‌کند.

### قرارداد

- context باید `JobRecord` را map کرده باشد (`ConfigureQueueJobs`). job با همان تراکنشی commit می‌شود که `SaveChanges` استفاده می‌کند، از جمله تراکنش صریح.
- `IUniqueJob` با `ArgumentException` **رد می‌شود** (gate آن در cache است و نمی‌تواند به تراکنش پایگاه داده بپیوندد). chain و batch با این متد پشتیبانی نمی‌شوند.
- `options.Connection` نادیده گرفته می‌شود: job در همین پایگاه داده ذخیره می‌شود و worker باید connection به نام `database` و همان صف را مصرف کند.
- تحویل همچنان at-least-once است. این کار گم‌شدن و پیام فانتوم را می‌بندد، نه تکرار را؛ jobها باید idempotent بمانند.
- فراخوانندگان فعلی `DispatchAsync` بدون تغییر می‌مانند.

## تصمیم‌گرفته‌نشده (خارج از دامنه)

Outbox برای Redis، Kafka و RabbitMQ (جدول و relay جدا با قواعد ترتیب، پاکسازی و ارسال تکراری). نیاز به PDR جداگانه دارد.

## گزینه‌های ردشده

۱) callback مثل `afterCommit`: فقط پیام فانتوم را حل می‌کند. ۲) پیوستن ضمنی `PushAsync` به تراکنش محیطی: ضمنی و غیرقابل‌پیش‌بینی. ۳) پکیج Outbox کامل: به تعویق افتاد.
