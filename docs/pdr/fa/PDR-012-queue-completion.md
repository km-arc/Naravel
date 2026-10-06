# PDR-012 — تکمیل Queue، بخش A

**داوری با اولویت .NET بومی:** مبنا، hosted serviceها، cancellation، channelها و acknowledgement بومی هر provider در .NET است. Naravel فقط عملیات پایدار صف و رفتار یکسان میان driverها را اضافه کند که این primitiveها فراهم نمی‌کنند؛ مدل میزبانی بومی را جایگزین نکند.

وضعیت: **تأییدشده در ۲۰۲۶-۱۰-۰۶.** مهاجرت async به RabbitMQ.Client 7.x طبق OD-02 فقط برای R07 از پیش مجاز است؛ پیش از تغییر مدیریت مرکزی بسته‌ها، نسخهٔ دقیق را بررسی کنید.

## زمینه

R07 شکاف‌های عملیاتی Naravel.Queue موجود را کامل می‌کند: رسیدگی به شکست دائمی، شمارش تلاش پس از reclaim و timeout، batch پایدار، کنترل worker، API ناهمگام RabbitMQ، observability و fake تست. طراحی باید registry صریح aliasهای job در R00 و مالکیت Manager/driver ثبت‌شده در PDR-006 را حفظ کند. middlewareهای یکتایی، جلوگیری از هم‌پوشانی و محدودسازی نرخ در R08 می‌مانند؛ job رمزگذاری‌شده در R18.

سطح Laravel: provider مربوط به failed job و عملیات retry/forget، batchها، کنترل worker و رفتار attempt/timeout در job. مبنای بومی: `BackgroundService`، `CancellationToken`، `Activity`/`Meter` و primitiveهای acknowledgement/visibility هر broker.

## تصمیم پیشنهادی

### ثبت و dispatch ساده

مدل manager/driver فعلی حفظ و به‌جای resolver دیگری، یک ورودی builder افزایشی ارائه شود:

```csharp
services.AddNaravelQueue(configuration, queue => queue.AddRedis());
services.AddNaravelQueueWorker();
await dispatcher.DispatchAsync(new SendInvoice(invoiceId), cancellationToken);
```

نام دقیق builder و dispatcher پیش از پیاده‌سازی باید با سطح registration فعلی بررسی شود. مسیر معمول ثبت و dispatch حداکثر سه خط کد کاربر داشته باشد؛ registration اختصاصی providerهای فعلی نیز هنگام مهاجرت کار کند یا به‌عنوان breaking change نسخهٔ پیش از ۱ مستند شود.

### Jobهای ناموفق

`IFailedJobStore` با پیاده‌سازی پایدار Database و Redis و پیاده‌سازی درون‌حافظه‌ای برای تست اضافه شود. شکست نهایی با شناسهٔ failure idempotent، envelope سریال‌شدهٔ اصلی، alias، connection، queue، تعداد تلاش و جزئیات خطا ثبت شود. از registry alias در R00 استفاده شود؛ نوع CLR هرگز از دادهٔ payload ذخیره‌شده resolve نشود. ثبت پیش از acknowledgement یا حذف reservation منبع انجام شود و رفتار at-least-once در مرز غیرتراکنشی broker/store پذیرفته شود.

`RetryAsync(id)`، `RetryAllAsync`، `ForgetAsync(id)` و `FlushAsync` ارائه شوند. Retry با envelope در R00 به connection و queue اصلی publish می‌کند و فقط پس از انتشار موفق رکورد شکست را حذف می‌کند. `RetryAllAsync` رکوردها را مستقل پردازش می‌کند؛ موفقیت جزئی گزارش می‌شود و رکورد انتشارناموفق باقی می‌ماند. Forget یک رکورد را حذف می‌کند؛ Flush همهٔ رکوردهای شکست را بدون dispatch حذف می‌کند. همهٔ APIهای ذخیره و dispatch async و دارای cancellation باشند.

### شمارش تلاش و timeout

reservationی که پس از timeout مربوط به visibility/lease دوباره reclaim می‌شود، پیش از تحویل مجدد `Attempts` را افزایش دهد. تحویل اولیه قرارداد فعلی R00 را حفظ کند؛ تست‌های قراردادی باید مشخص کنند شمارش قابل‌مشاهده از یک شروع می‌شود یا صفر. پس از `MaxAttempts` تحویل/reclaim، worker به‌جای retry بی‌پایان job را به مسیر شکست دائمی بفرستد.

`Job.Timeout` مجازی و nullable اضافه شود؛ null از مقدار پیش‌فرض worker استفاده می‌کند. timeout مؤثر اجرا کوچک‌ترین مقدار غیرتهی میان timeout خود job و worker است. timeout، token مربوط به job را cancel می‌کند و طبق سیاست معمول attempt/failure پیش می‌رود؛ نباید job را بی‌سروصدا موفق اعلام کند. هنگام startup اگر visibility timeout از بیشینهٔ timeout مرتبط job/worker به‌علاوهٔ حاشیهٔ ایمنی کمتر باشد، warning ثبت شود. لغو کدی که token را نادیده می‌گیرد تضمین نشود.

### Batchهای پایدار

`IBatchRepository` با پیاده‌سازی Database و Redis اضافه شود. شناسهٔ batch، شمارنده‌های pending/succeeded/failed، `AllowFailures`، وضعیت لغو و وضعیت اجرای callbackها پایدار شوند. `CancelAsync` از اجرای jobهای batch که هنوز شروع نشده‌اند جلوگیری می‌کند؛ jobهای در حال اجرا به‌اجبار متوقف نمی‌شوند. بدون `AllowFailures`، نخستین شکست جلوی jobهای dispatch‌نشدهٔ باقی‌مانده را می‌گیرد و batch را failed می‌کند؛ با آن، بقیه ادامه می‌یابند و batch پس از تعیین تکلیف همه terminal می‌شود.

به‌جای delegate، alias callback و payload typed ذخیره شود. callbackهای typed از نوع then/catch/finally در registry صریح alias ثبت شوند؛ از lookup نام نوع یا reflection بر اساس دادهٔ ذخیره‌شده استفاده نشود. پیشرفت callback قبل/بعد از اجرا ذخیره شود تا recovery ممکن باشد و اجرای at-least-once در crash فرایند مستند شود؛ callbackها باید idempotent باشند. تست باید repository را دوباره بسازد و بقای وضعیت را ثابت کند.

### کنترل worker

گزینه‌های `StopWhenEmpty`، `MaxJobs`، `MaxRuntime` و `Rest` اضافه شوند. `StopWhenEmpty` پس از poll خالی خارج می‌شود؛ `MaxJobs` تعداد reservationهای تکمیل‌شده در هر اجرا را محدود می‌کند؛ `MaxRuntime` پس از گذشت زمان، گرفتن کار جدید را متوقف می‌کند و اجازه می‌دهد رفتار cancellation/timeout کار فعال اجرا شود؛ `Rest` مکث پس از poll خالی و پیش از poll بعدی است. پیش‌فرض‌ها رفتار worker پیوستهٔ فعلی را حفظ کنند. `CancellationToken` همچنان سیگنال فوری خاموش‌شدن host باشد.

### RabbitMQ، observability و fake

Provider مربوط به RabbitMQ به APIهای async در RabbitMQ.Client 7.x مهاجرت کند، lock تک‌کاناله و تمام sync-over-async حذف شوند و معنای ack/release/fail حفظ شود. پیش از ویرایش `Directory.Packages.props` metadata آخرین نسخهٔ پشتیبانی‌شدهٔ 7.x بررسی شود؛ وابستگی دیگری اضافه نشود.

از `Meter` با نام `Naravel.Queue` و counterهای processed، failed و retried به‌علاوهٔ histogram مدت پردازش استفاده شود. `ActivitySource` با نام `Naravel.Queue` باشد؛ context ردیابی W3C (`traceparent`/`tracestate`) در envelope پیام حمل و هنگام پردازش restore شود. tagهای metric کم‌کاردینالیتی باشند و شناسهٔ job یا payload را شامل نشوند.

`Naravel.Queue.Testing` با Fake شامل `AssertDispatched<T>` و `AssertChained` اضافه شود. این fake باید همان قرارداد عمومی dispatch را بدون broker یا worker اجرا کند.

## وابستگی‌ها و مرزها

هیچ وابستگی تازه‌ای جز مهاجرت RabbitMQ.Client 7.x که OD-02 مجاز کرده تأیید نمی‌شود. پروژه‌های provider اختیاری بمانند و نسخهٔ بسته‌ها متمرکز مدیریت شود. middlewareهای R08، رمزگذاری R18، adapterهای Events/Mail، رمزنگاری سفارشی، فعال‌سازی job با reflection یا جایگزین `BackgroundService` پیاده نشوند.

## راستی‌آزمایی لازم پس از تأیید

تست‌های متمرکز Memory برای کنترل worker، لغو timeout و اتمام سقف تلاش؛ تست SQLite برای retry job ناموفق و پایداری batch؛ تست Redis برای storeهای پایدار؛ و اجرای contract suite مربوط به RabbitMQ از R01 با سرویس env-gated اضافه/اجرا شوند. نبود `.GetAwaiter().GetResult()` در پروژهٔ RabbitMQ بررسی شود. assertionهای MeterListener و Fake، benchmarkهای Queue، مستندات Queue انگلیسی/فارسی، parity tableها، changelog و checklist مرحله به‌روزرسانی شوند. راستی‌آزمایی کامل solution و `roadmap/check.py` اجرا شود. سرویس خارجی اجرا‌نشده باید `NOT RUN` گزارش شود، نه موفق.

## گزینه‌های بررسی‌شده

۱. **اتکا به hosted service تنها برای failed job و batch:** رد؛ hosting رکورد شکست، قصد retry یا وضعیت batch را پس از restart پایدار نمی‌کند.
۲. **ذخیرهٔ نام نوع CLR یا delegate در رکورد سریال‌شده:** رد طبق OD-07 و به‌دلیل ناایمن و شکننده بودن در deployها؛ از alias صریح و registry callback typed استفاده شود.
۳. **retry روی connection پیش‌فرض فعلی:** رد؛ پس از تغییر تنظیمات ممکن است routing عوض شود. connection و queue اصلی از envelope حفظ شوند.
۴. **فرض اینکه timeout ثابت می‌کند کد کاربر متوقف شده:** رد؛ cancellation تعاونی است. timeout گزارش و قواعد retry/failure اعمال شود، بدون ادعای توقف اجباری.
۵. **نگه‌داشتن API همگام RabbitMQ.Client 6.x پشت wrapper ناهمگام:** رد؛ blocking و مشکل lock کانال باقی می‌مانند و مهاجرت async مصوب را کامل نمی‌کند.
۶. **تضمین exactly-once برای callback هنگام crash:** رد؛ بدون transaction مشترک میان اثرهای جانبی callback و repository چنین تضمینی ممکن نیست. پیشرفت ذخیره و اجرای at-least-once/idempotent مشخص شود.

## تأیید

مالک این پیشنهاد را در ۲۰۲۶-۱۰-۰۶ تأیید کرد. معنای تحویل رکوردهای شکست، قرارداد شمارش attempt، سیاست timeout/visibility، رفتار شکست batch و callback، پیش‌فرض‌های worker، قرارداد metric، API fake و quickstart برای پیاده‌سازی R07 تأیید شده‌اند.
