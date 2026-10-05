# Filesystem

`Naravel.Filesystem` درایورهای ذخیره‌سازی نام‌دار را از طریق `Naravel.Foundation` فراهم می‌کند. درایورهای داخلی شامل فایل محلی و Amazon S3/S3-compatible هستند.

## ثبت سرویس

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelFilesystem(builder.Configuration);

var app = builder.Build();
app.Run();
```

بخش تنظیمات به‌صورت پیش‌فرض `Filesystem` است. برای استفاده از نام دیگری، آن را به‌عنوان آرگومان دوم بدهید:

```csharp
builder.Services.AddNaravelFilesystem(builder.Configuration, "ObjectStorage");
```

همچنین می‌توان از overload مبتنی بر `Action<FilesystemOptions>` استفاده کرد. `StorageManager` به‌صورت singleton ثبت می‌شود؛ درایور پیش‌فرض `IStorageDriver` نیز برای تزریق مستقیم در دسترس است. اگر انتخاب زمان اجرای دیسک لازم است، `StorageManager` را تزریق کنید.

## تنظیمات

```json
{
  "Filesystem": {
    "Default": "local",
    "Stores": {
      "local": {
        "Driver": "local",
        "Root": "storage/uploads",
        "BaseUrl": "/storage"
      },
      "archive": {
        "Driver": "s3",
        "Key": "${S3_ACCESS_KEY}",
        "Secret": "${S3_SECRET_KEY}",
        "Bucket": "documents",
        "Region": "us-east-1"
      },
      "minio": {
        "Driver": "s3",
        "Key": "${S3_ACCESS_KEY}",
        "Secret": "${S3_SECRET_KEY}",
        "Bucket": "documents",
        "Endpoint": "http://localhost:9000",
        "Region": "us-east-1"
      }
    }
  }
}
```

برای اطلاعات محرمانه از environment variable یا secret provider استفاده کنید؛ کلید واقعی را commit نکنید. `Default` دیسک پیش‌فرض را انتخاب می‌کند و مقدار پیش‌فرض آن `local` است. نام storeها به بزرگی و کوچکی حروف حساس نیست. مقدارهای داخلی `Driver` برابر `local` و `s3` هستند.

مقدارهای پیش‌فرض درایور local عبارت‌اند از `Root = "wwwroot/uploads"` و `BaseUrl = "/storage"`. اگر `Endpoint` داده نشود، درایور S3 از endpoint استاندارد AWS استفاده می‌کند. برای endpoint سازگار با S3، `Region` منطقهٔ امضای درخواست و `Endpoint` نشانی سرویس را تعیین می‌کند.

## انتخاب دیسک

اگر دیسک پیش‌فرض کافی است، `IStorageDriver` را تزریق کنید. برای انتخاب زمان اجرا از `StorageManager.Disk(name)` استفاده کنید:

```csharp
using Naravel.Filesystem;

public sealed class ArchiveService(StorageManager storage)
{
    public IStorageDriver GetArchive() => storage.Disk("archive");
}
```

نام متد `Disk()` برای اصطلاحات Filesystem حفظ شده است؛ resolution، cache، باطل‌سازی هنگام reload، افزودن driver در زمان اجرا و آزادسازی منابع را Foundation انجام می‌دهد.

## Upload از controller

به‌جای اعتماد به مسیر یا نام فایل ورودی کاربر، یک storage key تولید کنید:

```csharp
using Microsoft.AspNetCore.Mvc;
using Naravel.Filesystem;

[ApiController]
[Route("documents")]
public sealed class DocumentsController(IStorageDriver storage) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        var key = $"{Guid.NewGuid():N}/{Path.GetFileName(file.FileName)}";
        await using var contents = file.OpenReadStream();
        await storage.PutAsync(key, contents, cancellationToken);
        return Ok(new { key });
    }
}
```

عملیات `IStorageDriver` ناهمگام هستند و `CancellationToken` می‌پذیرند. `GetUrlAsync` در درایور local نشانی زیر `BaseUrl` می‌سازد؛ پارامتر انقضا فقط برای URL امضاشدهٔ S3 کاربرد دارد.

## ساخت URL امضاشدهٔ S3

```csharp
var url = await storage.Disk("archive").GetUrlAsync(
    key,
    expiration: TimeSpan.FromMinutes(10),
    cancellationToken);
```

URLهای local امضا نمی‌شوند و انقضا ندارند. دسترسی به مسیر static-file مربوط را جداگانه محافظت کنید.

## افزودن driver در زمان اجرا

manager مربوط به Foundation از `Extend` زمان اجرا پشتیبانی می‌کند. پس از ساخته‌شدن service provider می‌توان driver سفارشی اضافه کرد:

```csharp
var manager = app.Services.GetRequiredService<StorageManager>();
manager.Extend("scratch", _ => new LocalStorageDriver("storage/scratch", "/scratch"));
var scratch = manager.Disk("scratch");
```

manager مالک driverهایی است که factory برمی‌گرداند و هنگام dispose شدن manager آن‌ها را آزاد می‌کند. از برگرداندن singleton متعلق به container استفاده نکنید، مگر اینکه manager اجازهٔ آزادسازی آن را داشته باشد.

## امنیت و محدودیت‌ها

- مسیرهای local نسبت به ریشهٔ دیسک هستند. تلاش برای خروج از ریشه با `UnauthorizedAccessException` رد می‌شود؛ هنگام تغییر مسیرها تست‌های regression را حفظ کنید.
- مهاجرت به Foundation کلیدهای تنظیمات را از `DefaultDisk`/`Disks` به `Default`/`Stores` تغییر داده و APIهای قدیمی `IStorageManager` و factory را حذف کرده است. هنگام مهاجرت، مصرف‌کننده‌ها را به‌روزرسانی کنید.
- انتخاب region در S3، endpoint و آزادسازی client تست واحد دارند، اما این پروژه تست یکپارچه با AWS/S3 زنده اجرا نمی‌کند.
- درایور local endpoint عمومی HTTP ایجاد نمی‌کند. static files یا endpoint مجاز دانلود را جداگانه در ASP.NET Core تنظیم کنید.
- عملیات Filesystem سیستم Laravel برای امضای URL، visibility/policy یا abstraction ابری فراتر از driverهای مستندشده را ارائه نمی‌کند.
