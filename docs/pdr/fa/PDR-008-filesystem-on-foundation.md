# PDR-008 — انتقال Filesystem به Foundation

وضعیت: **در ۲۰۲۶-۱۰-۰۴ تأیید و پیاده‌سازی شد.** مالک این PDR، شامل مهاجرت API عمومی/تنظیمات و تصمیم‌های مربوط به Region و طول عمر کلاینت S3، را تأیید کرد. build کامل solution و هر ۱۷۲ تست برای Stage 2 موفق بود.

## زمینه

`Naravel.Filesystem` اکنون `StorageManager`، `IFilesystemDriverFactory` و `FilesystemDriverFactory` اختصاصی دارد. این manager دیسک‌ها را خودش cache می‌کند، از `IOptions<FilesystemOptions>` استفاده می‌کند و تنظیمات را با `DefaultDisk`/`Disks` می‌خواند. `Naravel.Foundation` رفتار مشترک لازم برای ماژول‌های مبتنی بر driver را فراهم می‌کند: cache کردن driverهای نام‌دار، `Extend` در زمان اجرا، باطل‌سازی cache هنگام تغییر تنظیمات و آزادسازی driverها.

درایور local یک وصلهٔ امنیتی مهم برای محدودکردن مسیر به ریشهٔ دیسک و تست‌های regression دارد. مهاجرت باید این تضمین‌ها را حفظ کند. `DiskOptions.Region` اکنون اثری ندارد و `S3StorageDriver` کلاینت AWS را مالک می‌شود ولی آن را آزاد نمی‌کند.

## تصمیم

### Manager و ثبت سرویس‌ها

- از `StorageManager : Manager<IStorageDriver, FilesystemOptions>` استفاده شود و `Disk(string? name = null)` به‌عنوان نام کاربری filesystem برای `Driver(name)` حفظ شود.
- `FilesystemOptions` از `ManagerOptions` ارث‌بری کند؛ کلیدهای تنظیمات `Default` و `Stores` باشند، نه `DefaultDisk` و `Disks`.
- درایورهای داخلی از الگوی Foundation یعنی `IDriverRegistry<IStorageDriver>` / `AddNaravelDriver` ثبت شوند. ورودی `AddNaravelFilesystem` حفظ شود و نام بخش تنظیمات اختیاری باشد؛ مقدار پیش‌فرض `Filesystem` است.
- مسیر تکراری manager/factory (`IFilesystemDriverFactory`، `FilesystemDriverFactory` و abstraction قدیمی `IStorageManager`) حذف شود، نه اینکه Foundation پشت آن‌ها قرار بگیرد. این تغییر API عمومی است و باید در یادداشت انتشار ذکر شود.
- درایور سفارشی زمان اجرا با `Extend` به‌ارث‌رسیده از manager اضافه شود؛ registry یا cache دیگری ساخته نشود.

نمونهٔ تنظیمات:

```json
{
  "Filesystem": {
    "Default": "local",
    "Stores": {
      "local": { "Driver": "local", "Root": "storage/uploads", "BaseUrl": "/storage" },
      "archive": { "Driver": "s3", "Key": "...", "Secret": "...", "Bucket": "archive", "Endpoint": "https://s3.example.test", "Region": "us-east-1" }
    }
  }
}
```

### طول عمر driver و تنظیمات S3

- اجباری‌کردن disposal در قرارداد `IStorageDriver` انجام نشود. Foundation driverهای cacheشده‌ای را که `IDisposable` یا `IAsyncDisposable` باشند آزاد می‌کند؛ `S3StorageDriver`، `IAmazonS3` متعلق به خودش را با `IDisposable` آزاد کند.
- `DiskOptions.Region` مؤثر شود: برای endpoint استاندارد AWS از `AmazonS3Config.RegionEndpoint` و برای `ServiceURL` سفارشی S3-compatible از `AuthenticationRegion` استفاده شود (SDK با تنظیم service URL سفارشی، `RegionEndpoint` را پاک می‌کند). رفتار پیش‌فرض در نبود `Region` حفظ شود.
- در این مرحله `AWSSDK.S3` ارتقا داده نشود؛ ارتقای آن جداگانه به تأیید مالک نیاز دارد.

### API، ایمنی و تست‌ها

- عملیات async فعلی حفظ شوند و `CancellationToken` اختیاری به `GetUrlAsync` اضافه شود تا APIهای async مرتبط با I/O با قرارداد مخزن هماهنگ باشند.
- بررسی امنیتی containment برای جلوگیری از path traversal بدون تغییر حفظ شود و تست‌های regression آن باقی بمانند.
- تست‌های قرارداد درایور local برای exists/get/stream/put/delete/copy/move/size/url اضافه شوند؛ تست‌های manager برای چند store نام‌دار، `Extend` زمان اجرا، reload تنظیمات، انتخاب پیش‌فرض و disposal نوشته شوند؛ و برای S3 تستی با client/factory قابل تزریق یا ابزار تست از قبل تأییدشده افزوده شود. بدون تصمیم PDR وابستگی جدید اضافه نشود.
- `docs/en/filesystem.md` و `docs/fa/filesystem.md` برای تنظیمات، انتخاب دیسک، upload در controller، temporary URL، driver سفارشی، محدودیت‌ها و رفتار امنیتی نوشته شوند. parity tableها، وضعیت ماژول و changelog نیز به‌روزرسانی شوند.

## گزینه‌های بررسی‌شده

۱. **حفظ manager/factory اختصاصی:** رد؛ رفتار Foundation برای resolution، cache، `Extend`، reload و disposal را تکرار می‌کند.
۲. **قرار دادن Foundation زیر manager فعلی:** رد؛ دو API مدیریت ایجاد می‌کند و سطح forwarding غیرضروری باقی می‌ماند.
۳. **اجباری‌کردن disposable بودن همهٔ `IStorageDriver`ها:** رد؛ disposal برای برخی driverها اختیاری است و Foundation هر دو قرارداد disposal را بدون تحمیل آن به driverهای local/custom پشتیبانی می‌کند.
۴. **تغییر نام `Disk()` به `Driver()`:** رد؛ `Disk()` مفهوم کاربری Filesystem است و در ماژول موجود است؛ می‌تواند به Foundation `Driver()` واگذار شود.

## پیامدها

- پروژهٔ runtime یا تست جدیدی لازم نیست؛ پروژه‌های فعلی `Naravel.Filesystem` و `Naravel.Filesystem.Tests` مهاجرت می‌کنند.
- شکل تنظیمات و abstraction مربوط به manager تغییر می‌کند. `DefaultDisk`/`Disks` قدیمی و APIهای `IStorageManager`/factory در این مهاجرت حذف می‌شوند.
- مسئولیت cache کردن driver، باطل‌سازی زندهٔ تنظیمات، extension زمان اجرا و disposal به Foundation سپرده می‌شود؛ رفتار اختصاصی storage در قرارداد و driverهای خودش می‌ماند.
- وصلهٔ امنیتی containment مرحلهٔ ۰ الزامی است و هیچ مرحله‌ای از مهاجرت نباید آن را تضعیف کند.

## ثبت تأیید

مالک تصمیم‌های پیشنهادی را در ۲۰۲۶-۱۰-۰۴ تأیید کرد. وصلهٔ containment مسیر حفظ شد و تست‌ها و مستندات EN/FA کامل هستند. build solution موفق شد و هر ۱۷۲ تست در ۲۰۲۶-۱۰-۰۴ گذر کرد؛ تست یکپارچه با AWS/S3 واقعی اجرا نشد.
